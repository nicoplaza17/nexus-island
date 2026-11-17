using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Geometry;
using NexusIsland.Core.Island;
using NexusIsland.Core.Settings;
using NexusIsland.Services.Windows.Display;
using NexusIsland.Services.Windows.Input;
using NexusIsland.Services.Windows.Shell;

namespace NexusIsland.App.Island;

/// <summary>
/// Ventana de la isla: conecta la máquina de estados (Core), el árbitro de actividades,
/// el animador, la ubicación y los servicios de Windows (pantalla completa, atajo global, pantallas).
/// </summary>
public partial class IslandWindow : Window
{
    private const int ToggleHotkeyId = 0x4E49; // "NI"
    private static readonly bool PinExpandedForTests = Environment.GetEnvironmentVariable("NEXUS_PIN_EXPANDED") == "1";
    private const double BaseWidth = 460, BaseHeight = 220; // lienzo a tamaño "Normal"

    private readonly ILogger<IslandWindow> _logger;
    private readonly ActivityArbiter _arbiter;
    private readonly ClockViewModel _clock;
    private readonly SettingsStore _settings;
    private double _appliedScale = 1.0;
    private HotkeySettings? _registeredHotkey;
    private readonly IslandStateMachine _stateMachine = new();
    private readonly IslandAnimator _animator;
    private readonly DispatcherTimer _stateTimer = new(DispatcherPriority.Normal);
    private readonly DispatcherTimer _housekeeping = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };

    private IntPtr _hwnd;
    private IslandPlacementController? _placement;
    private GlobalHotkey? _hotkey;

    private bool _userHidden;
    private bool _fullscreenSuppressed;
    private bool _wantVisible = true;
    private IntPtr _lastForeground;
    private IslandActivity? _shownActivity;

    private bool _pressed;
    private PixelPoint _pressCursor;

    public IslandWindow(ILogger<IslandWindow> logger, ActivityArbiter arbiter, ClockViewModel clock, SettingsStore settings)
    {
        _logger = logger;
        _arbiter = arbiter;
        _clock = clock;
        _settings = settings;
        InitializeComponent();
        ApplyScale(_settings.Current.Island.Scale, reposition: false);
        _settings.Changed += OnSettingsChanged;

        _animator = new IslandAnimator(IslandFrame, IslandShape, ShadowInner, ShadowOuter, ContentHost,
            CollapsedLayer, PeekLayer, ExpandedLayer)
        {
            // Respeta "Efectos de animación" desactivado en la configuración de accesibilidad de Windows.
            ReduceMotion = !SystemParameters.ClientAreaAnimation,
        };

        _animator.Settled += frames => _logger.LogDebug("Animación terminada en {Frames} cuadros", frames);
        _stateMachine.StateChanged += OnStateChanged;
        _stateMachine.TimerRequested += OnTimerRequested;
        _stateTimer.Tick += (_, _) =>
        {
            _stateTimer.Stop();
            _stateMachine.TimerElapsed(_stateMachine.PendingTimer);
        };

        IslandShape.MouseEnter += (_, _) =>
        {
            _logger.LogDebug("MouseEnter (cursor {Cursor})", DisplayService.GetCursorPosition());
            if (!IsDraggingOrPressed) _stateMachine.PointerEntered();
        };
        IslandShape.MouseLeave += (_, _) =>
        {
            _logger.LogDebug("MouseLeave (cursor {Cursor})", DisplayService.GetCursorPosition());
            // Diagnóstico (NEXUS_PIN_EXPANDED=1): la isla no se contrae al irse el mouse (pruebas automáticas
            // mientras alguien usa la PC).
            if (!IsDraggingOrPressed && !PinExpandedForTests) _stateMachine.PointerExited();
        };
        IslandShape.MouseLeftButtonDown += OnPressed;
        IslandShape.MouseMove += OnMouseMoved;
        IslandShape.MouseLeftButtonUp += OnReleased;
        IslandShape.LostMouseCapture += (_, _) =>
        {
            _logger.LogDebug("LostMouseCapture (pressed {Pressed})", _pressed);
            FinishPress(cancelled: true);
        };

        _housekeeping.Tick += (_, _) => Housekeeping();
        SourceInitialized += OnSourceInitialized;
        Closed += (_, _) => { _hotkey?.Dispose(); _housekeeping.Stop(); };

        // Actividades: el reloj siempre está (prioridad mínima); los módulos agregan las suyas.
        _arbiter.CurrentChanged += current =>
        {
            // Algo urgente (alerta) o un panel que el usuario abrió le gana a la página elegida a mano.
            if (current is not null && current.Priority >= ActivityPriority.Urgent) _pinnedKey = null;
            RefreshDisplay();
        };
        _arbiter.ActivitiesChanged += RefreshDisplay;
        _arbiter.AttentionRequested += OnAttentionRequested;

        IslandShape.MouseWheel += OnMouseWheel;
        LauncherButton.Click += (_, _) => ToggleLauncher();
        _clock.Refresh(force: true);
        _arbiter.Publish(new IslandActivity(ClockViewModel.ActivityKey, ClockViewModel.ModuleId,
            ActivityPriority.Idle, _clock));
    }

    /// <summary>True si el usuario no la ocultó (aunque puede estar apartada por pantalla completa).</summary>
    public bool IsUserVisible => !_userHidden;

    public IslandState State => _stateMachine.State;

    /// <summary>Atajo actualmente registrado (para mostrarlo en la bandeja y en la isla).</summary>
    public string HotkeyText => (_registeredHotkey ?? _settings.Current.Hotkey).ToString();

    /// <summary>Ancla la isla en un monitor (desde la configuración).</summary>
    public void DockOnMonitor(string? deviceName) => _placement?.DockOnMonitor(deviceName);

    /// <summary>
    /// Intenta registrar un atajo nuevo. Si Windows lo rechaza (otra app ya lo usa), vuelve al anterior
    /// y devuelve false; la configuración no se toca.
    /// </summary>
    public bool TryApplyHotkey(HotkeySettings hotkey)
    {
        if (_hotkey is null) return false;
        if (TryRegister(hotkey))
        {
            _settings.Update(s => s.Hotkey = hotkey.Clone());
            return true;
        }
        if (_registeredHotkey is not null) TryRegister(_registeredHotkey);
        return false;
    }

    private bool TryRegister(HotkeySettings hotkey)
    {
        if (_hotkey is null || !Enum.TryParse<Key>(hotkey.Key, ignoreCase: true, out var key)) return false;
        var modifiers = HotkeyModifiers.None;
        if (hotkey.Ctrl) modifiers |= HotkeyModifiers.Control;
        if (hotkey.Alt) modifiers |= HotkeyModifiers.Alt;
        if (hotkey.Shift) modifiers |= HotkeyModifiers.Shift;
        if (hotkey.Win) modifiers |= HotkeyModifiers.Win;

        if (!_hotkey.Register(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)))
        {
            _logger.LogWarning("No se pudo registrar {Hotkey}: otra app ya lo usa", hotkey);
            return false;
        }
        _registeredHotkey = hotkey.Clone();
        _clock.SetHotkeyText(hotkey.ToString());
        _logger.LogInformation("Atajo global {Hotkey} registrado", hotkey);
        return true;
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        if (Math.Abs(settings.Island.Scale - _appliedScale) > 0.001)
            ApplyScale(settings.Island.Scale, reposition: true);
    }

    /// <summary>Tamaño de la isla: escala todo el contenido y el lienzo de la ventana.</summary>
    private void ApplyScale(double scale, bool reposition)
    {
        _appliedScale = scale;
        Width = BaseWidth * scale;
        Height = BaseHeight * scale;
        Root.LayoutTransform = scale == 1.0 ? Transform.Identity : new ScaleTransform(scale, scale);
        _logger.LogInformation("Tamaño de la isla: {Scale:P0}", scale);
        if (reposition)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () => _placement?.Revalidate());
    }

    private bool IsDraggingOrPressed => _pressed || (_placement?.IsDragging ?? false);

    /// <summary>Mostrar/ocultar por pedido del usuario (bandeja o atajo de teclado).</summary>
    public void ToggleUserVisibility()
    {
        _userHidden = !_userHidden;
        _logger.LogInformation("Isla {Action} por el usuario", _userHidden ? "ocultada" : "mostrada");
        ApplyVisibility();
    }

    // ----------------------------------------------------------------- arranque y mensajes de Windows

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        OverlayWindowStyles.Apply(_hwnd);
        HwndSource.FromHwnd(_hwnd)?.AddHook(WndProc);

        _placement = new IslandPlacementController(_hwnd, _logger, _settings);
        _placement.PlaceInitial();

        _hotkey = new GlobalHotkey(_hwnd, ToggleHotkeyId);
        TryRegister(_settings.Current.Hotkey);

        _housekeeping.Start();
        if (Environment.GetEnvironmentVariable("NEXUS_BENCH") == "1") StartBenchmark();
        // Diagnóstico: arrancar expandida (para probar los controles sin usar el mouse).
        if (Environment.GetEnvironmentVariable("NEXUS_START_EXPANDED") == "1") _stateMachine.Clicked();
        _logger.LogInformation("Ventana de la isla creada (HWND 0x{Hwnd:X}), reducir movimiento: {Reduce}",
            _hwnd.ToInt64(), _animator.ReduceMotion);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (OverlayWindowStyles.TryHandleMouseActivate(msg, out var result))
        {
            handled = true;
            return result;
        }

        if (_hotkey?.Matches(msg, wParam) == true)
        {
            ToggleUserVisibility();
            handled = true;
            return IntPtr.Zero;
        }

        var systemEvent = SystemMessages.Classify(msg, wParam);
        if (systemEvent != SystemEvent.None) OnSystemEvent(systemEvent);

        if (DisplayChangeMessages.IsDisplayChange(msg, wParam))
        {
            // Se deja terminar a WPF (por ejemplo el ajuste de DPI) y después se reubica.
            Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
            {
                _logger.LogInformation("Cambio de pantallas detectado (mensaje 0x{Msg:X})", msg);
                _placement?.Revalidate();
            });
        }

        return IntPtr.Zero;
    }

    /// <summary>La PC volvió de suspensión/hibernación (los servicios conviene reconectarlos).</summary>
    public event Action? SystemResumed;

    private void OnSystemEvent(SystemEvent systemEvent)
    {
        switch (systemEvent)
        {
            case SystemEvent.Suspending:
                _logger.LogInformation("La PC se está suspendiendo");
                _stateMachine.ForceCollapse();
                break;

            case SystemEvent.Resumed:
                _logger.LogInformation("La PC volvió de suspensión: se reacomoda la isla en 2 s");
                // Se espera un momento: al despertar, monitores y servicios tardan en estabilizarse.
                var delay = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                delay.Tick += (_, _) =>
                {
                    delay.Stop();
                    _placement?.Revalidate();
                    if (IsVisible) OverlayWindowStyles.BringToTopmost(_hwnd);
                    if (_registeredHotkey is not null) TryRegister(_registeredHotkey);
                    _clock.Refresh(force: true);
                    SystemResumed?.Invoke();
                };
                delay.Start();
                break;

            case SystemEvent.TaskbarCreated:
                // El Explorador se reinició: el área de trabajo pudo cambiar y otras ventanas "topmost" reaparecieron.
                _logger.LogInformation("El Explorador de Windows se reinició: se reacomoda la isla");
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    _placement?.Revalidate();
                    if (IsVisible) OverlayWindowStyles.BringToTopmost(_hwnd);
                });
                break;
        }
    }

    // ----------------------------------------------------------------- estados y animación

    private void OnStateChanged(IslandState previous, IslandState next)
    {
        _logger.LogDebug("Estado: {Previous} → {Next}", previous, next);
        _animator.AnimateTo(next);
        NotifyAware(_shownActivity, next, isShown: true);

        if (next == IslandState.Collapsed)
        {
            // Al contraerse: se vuelve a lo más importante y se cierran los paneles (herramientas, portapapeles).
            _pinnedKey = null;
            foreach (var panel in _arbiter.Ranked.Where(a => a.Priority >= ActivityPriority.Panel).ToList())
                _arbiter.Remove(panel.Key);
            RefreshDisplay();
        }
    }

    // ----------------------------------------------------------------- páginas y panel de herramientas

    private const string LauncherKey = "launcher";
    private string? _pinnedKey;
    private LauncherViewModel? _launcher;

    /// <summary>Botones del panel de herramientas: los aportan los módulos que implementan IQuickActionProvider.</summary>
    public void SetQuickActionProviders(IEnumerable<IQuickActionProvider> providers)
    {
        _launcher = new LauncherViewModel(providers, run: action =>
        {
            _arbiter.Remove(LauncherKey);
            _logger.LogInformation("Herramienta: {Action}", action.Label);
            try { action.Execute(); }
            catch (Exception ex) { _logger.LogError(ex, "Falló la herramienta {Action}", action.Id); }
        });
        LauncherButton.Visibility = _launcher.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ToggleLauncher()
    {
        if (_launcher is null) return;
        if (_arbiter.Contains(LauncherKey))
        {
            _arbiter.Remove(LauncherKey);
            return;
        }
        _arbiter.Publish(new IslandActivity(LauncherKey, LauncherKey, ActivityPriority.Panel, _launcher));
    }

    /// <summary>Actividades entre las que se puede pasar de página (los paneles no cuentan).</summary>
    private List<IslandActivity> Pages() =>
        _arbiter.Ranked.Where(a => a.Priority < ActivityPriority.Panel).ToList();

    /// <summary>Muestra la actividad que corresponde: un panel abierto, la página elegida a mano, o la más importante.</summary>
    private void RefreshDisplay()
    {
        if (_pinnedKey is not null && !_arbiter.Contains(_pinnedKey)) _pinnedKey = null;
        var current = _arbiter.Current;
        var target = current is not null && current.Priority >= ActivityPriority.Panel
            ? current
            : (_pinnedKey is not null ? _arbiter.Ranked.First(a => a.Key == _pinnedKey) : current);

        if (!ReferenceEquals(target, _shownActivity)) ShowActivity(target);
        UpdatePager();
    }

    private void Page(int direction)
    {
        var pages = Pages();
        if (pages.Count < 2 || _shownActivity is null || _shownActivity.Priority >= ActivityPriority.Panel) return;
        var index = pages.FindIndex(a => a.Key == _shownActivity.Key);
        var next = pages[((index < 0 ? 0 : index) + direction + pages.Count) % pages.Count];
        _pinnedKey = next.Key == _arbiter.Current?.Key ? null : next.Key;
        RefreshDisplay();
    }

    private void OnMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_stateMachine.State != IslandState.Expanded) return;
        Page(e.Delta > 0 ? -1 : 1);
        e.Handled = true;
    }

    /// <summary>Puntitos de página (solo con la isla expandida y si hay más de una actividad).</summary>
    private void UpdatePager()
    {
        PagerDots.Children.Clear();
        var pages = Pages();
        if (pages.Count < 2 || _shownActivity is null || _shownActivity.Priority >= ActivityPriority.Panel)
        {
            PagerDots.Visibility = Visibility.Collapsed;
            return;
        }

        PagerDots.Visibility = Visibility.Visible;
        foreach (var page in pages)
        {
            var isCurrent = page.Key == _shownActivity.Key;
            var dot = new System.Windows.Shapes.Ellipse
            {
                Width = isCurrent ? 7 : 6,
                Height = isCurrent ? 7 : 6,
                Margin = new Thickness(3, 0, 3, 0),
                Fill = (Brush)FindResource(isCurrent ? "IslandForeground" : "IslandForegroundMuted"),
                Opacity = isCurrent ? 1 : 0.45,
                Cursor = Cursors.Hand,
                ToolTip = page.ModuleId,
                Tag = page.Key,
            };
            dot.MouseLeftButtonDown += (s, e) =>
            {
                var key = (string)((FrameworkElement)s).Tag;
                _pinnedKey = key == _arbiter.Current?.Key ? null : key;
                RefreshDisplay();
                e.Handled = true; // que el clic no cierre la isla
            };
            PagerDots.Children.Add(dot);
        }
    }

    // ----------------------------------------------------------------- actividades

    /// <summary>Muestra la actividad elegida por el árbitro en las tres capas (con un fundido breve).</summary>
    private void ShowActivity(IslandActivity? activity)
    {
        var previous = _shownActivity;
        _shownActivity = activity;
        if (previous is not null && !ReferenceEquals(previous.ViewModel, activity?.ViewModel))
            NotifyAware(previous, _stateMachine.State, isShown: false);

        _logger.LogInformation("Actividad visible: {Key}", activity?.Key ?? "(ninguna)");
        SetHost(CollapsedHost, activity, "Compact");
        SetHost(PeekHost, activity, "Peek");
        SetHost(ExpandedHost, activity, "Expanded");
        NotifyAware(activity, _stateMachine.State, isShown: true);
    }

    private void SetHost(System.Windows.Controls.ContentControl host, IslandActivity? activity, string variant)
    {
        if (activity is null)
        {
            host.Content = null;
            return;
        }

        var key = $"{activity.ModuleId}.{variant}";
        if (TryFindResource(key) is not DataTemplate template)
        {
            _logger.LogWarning("Falta la plantilla \"{Key}\" del módulo {Module}", key, activity.ModuleId);
            template = null!;
        }
        host.ContentTemplate = template;
        host.Content = activity.ViewModel;

        if (!_animator.ReduceMotion)
            host.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    private static void NotifyAware(IslandActivity? activity, IslandState state, bool isShown)
    {
        if (activity?.ViewModel is IIslandAware aware) aware.OnIslandStateChanged(state, isShown);
    }

    /// <summary>Notificación temporal (por ejemplo, cambió la canción): solo si la isla está a la vista.</summary>
    private void OnAttentionRequested(IslandActivity activity, TimeSpan duration)
    {
        if (!_wantVisible || !IsVisible) return;
        _logger.LogDebug("Notificación temporal de {Key} por {Seconds} s", activity.Key, duration.TotalSeconds);
        _stateMachine.ShowTransient(duration);
    }

    private void OnTimerRequested(IslandTimer timer, TimeSpan delay)
    {
        _stateTimer.Stop();
        if (timer == IslandTimer.None) return;
        _stateTimer.Interval = delay;
        _stateTimer.Start();
    }

    // ----------------------------------------------------------------- clic y arrastre

    private void OnPressed(object sender, MouseButtonEventArgs e)
    {
        _pressed = true;
        _pressCursor = DisplayService.GetCursorPosition();
        var captured = IslandShape.CaptureMouse();
        _logger.LogDebug("MouseDown en {Cursor}, captura {Captured}", _pressCursor, captured);
        e.Handled = true;
    }

    private void OnMouseMoved(object sender, MouseEventArgs e)
    {
        if (!_pressed || _placement is null) return;
        var cursor = DisplayService.GetCursorPosition();

        if (!_placement.IsDragging)
        {
            var scale = DisplayService.GetScaleForWindow(_hwnd);
            var threshold = Math.Max(SystemParameters.MinimumHorizontalDragDistance, 4) * scale;
            if (Math.Abs(cursor.X - _pressCursor.X) < threshold && Math.Abs(cursor.Y - _pressCursor.Y) < threshold)
                return;

            // Empieza el arrastre: la isla se contrae para que sea fácil de ubicar.
            _stateMachine.ForceCollapse();
            _placement.BeginDrag(_pressCursor);
        }
        _placement.DragTo(cursor);
    }

    private void OnReleased(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _logger.LogDebug("MouseUp (pressed {Pressed})", _pressed);
        FinishPress(cancelled: false);
    }

    private void FinishPress(bool cancelled)
    {
        if (!_pressed) return;
        _pressed = false;
        var wasDragging = _placement?.IsDragging ?? false;
        if (IslandShape.IsMouseCaptured) IslandShape.ReleaseMouseCapture();

        if (wasDragging)
        {
            _placement!.EndDrag();
        }
        else if (!cancelled)
        {
            _stateMachine.Clicked();
            return;
        }

        // Después de arrastrar, se re-evalúa si el mouse quedó encima de la isla.
        if (IslandShape.IsMouseOver) _stateMachine.PointerEntered();
        else _stateMachine.PointerExited();
    }

    // ----------------------------------------------------------------- visibilidad

    private void ApplyVisibility()
    {
        var want = !_userHidden && !_fullscreenSuppressed;
        if (want == _wantVisible && (want == IsVisible)) return;
        _wantVisible = want;

        if (want)
        {
            if (!IsVisible)
            {
                Root.Opacity = 0;
                Show(); // ShowActivated=False: aparece sin activarse
            }
            OverlayWindowStyles.BringToTopmost(_hwnd);
            Fade(to: 1, onCompleted: null);
        }
        else
        {
            _stateMachine.ForceCollapse();
            Fade(to: 0, onCompleted: () => { if (!_wantVisible) Hide(); });
        }
    }

    private void Fade(double to, Action? onCompleted)
    {
        var duration = _animator.ReduceMotion ? TimeSpan.Zero : TimeSpan.FromMilliseconds(160);
        var animation = new DoubleAnimation(to, duration) { EasingFunction = new QuadraticEase() };
        if (onCompleted is not null) animation.Completed += (_, _) => onCompleted();
        Root.BeginAnimation(OpacityProperty, animation);
    }

    // ----------------------------------------------------------------- tareas periódicas (1 vez por segundo)

    private void Housekeeping()
    {
        _clock.Refresh();
        if (_hwnd == IntPtr.Zero) return;

        // Pantalla completa en el monitor de la isla → apartarse.
        var monitor = DisplayService.GetMonitorForWindow(_hwnd).Handle;
        var fullscreen = _settings.Current.General.HideInFullscreen && FullscreenDetector.IsFullscreenOnMonitor(monitor, _hwnd);
        if (fullscreen != _fullscreenSuppressed)
        {
            _fullscreenSuppressed = fullscreen;
            _logger.LogInformation("Pantalla completa {State}", fullscreen ? "detectada: isla apartada" : "terminada: isla visible");
            ApplyVisibility();
        }

        // Si cambió la app activa, se re-afirma el "siempre encima" (otras apps topmost pueden taparla).
        var foreground = FullscreenDetector.GetForegroundWindow();
        if (foreground != _lastForeground)
        {
            _logger.LogDebug("Cambió la ventana activa → re-afirmar 'siempre encima'");
            _lastForeground = foreground;
            if (IsVisible) OverlayWindowStyles.BringToTopmost(_hwnd);
        }
    }

    /// <summary>
    /// Modo benchmark (NEXUS_BENCH=1): recorre Contraída → Vistazo → Expandida sin usar el mouse,
    /// para medir el costo de CPU de las animaciones de forma repetible.
    /// </summary>
    private void StartBenchmark()
    {
        IslandState[] cycle = [IslandState.Peek, IslandState.Expanded, IslandState.Collapsed];
        var i = 0;
        var ms = int.TryParse(Environment.GetEnvironmentVariable("NEXUS_BENCH_MS"), out var v) && v > 0 ? v : 700;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
        timer.Tick += (_, _) =>
        {
            var state = cycle[i++ % cycle.Length];
            _animator.AnimateTo(state);
            NotifyAware(_shownActivity, state, isShown: true);
        };
        timer.Start();
        _logger.LogWarning("Modo benchmark activo: la isla cambia de estado sola cada {Ms} ms", ms);
    }
}
