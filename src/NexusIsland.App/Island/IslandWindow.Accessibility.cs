using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexusIsland.App.Properties;
using NexusIsland.Core.Activities;
using NexusIsland.Services.Windows.Input;
using NexusIsland.Services.Windows.Shell;

namespace NexusIsland.App.Island;

/// <summary>
/// Accesibilidad de la isla:
///  - Narrador: nombre accesible con el resumen de lo que se ve + anuncios cuando algo pide atención.
///  - Modo teclado (Ctrl+Alt+K): la isla toma el foco, Tab recorre los botones, ←/→ cambian de página,
///    Esc sale y devuelve el foco a la app anterior.
///  - Alto contraste: usa los colores del sistema y quita las sombras.
/// </summary>
public partial class IslandWindow
{
    private const int KeyboardHotkeyId = 0x4E4B; // "NK"
    private const string KeyboardModeResource = "IslandKeyboardMode";

    private GlobalHotkey? _keyboardHotkey;
    private bool _keyboardMode;
    private IntPtr _focusReturn;
    private string _accessibleName = "";
    private Dictionary<string, object>? _normalPalette;

    private void InitializeAccessibility()
    {
        PreviewKeyDown += OnKeyboardModeKey;
        Deactivated += (_, _) => { if (_keyboardMode) ExitKeyboardMode(returnFocus: false); };
        ApplyContrast();
        SystemParameters.StaticPropertyChanged += OnSystemParameterChanged;
        Closed += (_, _) => SystemParameters.StaticPropertyChanged -= OnSystemParameterChanged;
    }

    // ----------------------------------------------------------------- Narrador

    /// <summary>
    /// Nombre accesible de la ventana de la isla: el resumen de la actividad visible. Va en la ventana (y no en
    /// el Border de la isla) porque un Border no aparece en el árbol de accesibilidad. Solo se recalcula si hay
    /// un lector de pantalla u otra herramienta de accesibilidad conectada.
    /// </summary>
    private void UpdateAccessibleName()
    {
        if (!AutomationInteropProvider.ClientsAreListening && _accessibleName.Length > 0) return;
        var name = (_shownActivity?.ViewModel as IAccessibleActivity)?.AccessibleSummary ?? "Nexus Island";
        if (name == _accessibleName) return;
        _accessibleName = name;
        AutomationProperties.SetName(this, name);
    }

    /// <summary>Hace que el lector de pantalla diga el texto, sin mover el foco (región "en vivo", cortés).</summary>
    private void Announce(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !AutomationPeer.ListenerExists(AutomationEvents.LiveRegionChanged)) return;
        LiveRegion.Text = text;
        AutomationProperties.SetName(LiveRegion, text);
        var peer = UIElementAutomationPeer.FromElement(LiveRegion) ?? UIElementAutomationPeer.CreatePeerForElement(LiveRegion);
        peer?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    private void AnnouncePage(int number, int count, IslandActivity page)
    {
        var summary = (page.ViewModel as IAccessibleActivity)?.AccessibleSummary ?? page.ModuleId;
        Announce(string.Format(Strings.A11y_Page, number, count, summary));
    }

    // ----------------------------------------------------------------- modo teclado

    private void RegisterKeyboardModeHotkey()
    {
        _keyboardHotkey = new GlobalHotkey(_hwnd, KeyboardHotkeyId);
        if (_keyboardHotkey.Register(HotkeyModifiers.Control | HotkeyModifiers.Alt, (uint)KeyInterop.VirtualKeyFromKey(Key.K)))
            _logger.LogInformation("Modo teclado disponible con Ctrl+Alt+K");
        else
            _logger.LogWarning("No se pudo registrar Ctrl+Alt+K (modo teclado): otra app ya lo usa");
    }

    private void ToggleKeyboardMode()
    {
        if (_keyboardMode) ExitKeyboardMode(returnFocus: true);
        else EnterKeyboardMode();
    }

    private void EnterKeyboardMode()
    {
        if (_userHidden)
        {
            _userHidden = false;
            ApplyVisibility();
        }
        if (_fullscreenSuppressed || _hwnd == IntPtr.Zero) return; // con un juego o video en pantalla completa, no

        _focusReturn = FullscreenDetector.GetForegroundWindow();
        _keyboardMode = true;
        _logger.LogInformation("Modo teclado: activado");

        Application.Current.Resources[KeyboardModeResource] = true; // los botones pasan a aceptar el foco
        OverlayWindowStyles.SetActivatable(_hwnd, true);
        _stateMachine.Hold();
        // Windows deja traer la ventana al frente porque esta app acaba de recibir el atajo.
        Activate();
        FocusFirstControlLater();

        var summary = (_shownActivity?.ViewModel as IAccessibleActivity)?.AccessibleSummary;
        Announce(Strings.A11y_KeyboardMode + (summary is null ? "" : " " + summary));
    }

    private void ExitKeyboardMode(bool returnFocus)
    {
        if (!_keyboardMode) return;
        _keyboardMode = false;
        _logger.LogInformation("Modo teclado: desactivado");

        Keyboard.ClearFocus();
        Application.Current.Resources[KeyboardModeResource] = false;
        OverlayWindowStyles.SetActivatable(_hwnd, false);
        _stateMachine.Release();
        if (returnFocus) OverlayWindowStyles.ReturnFocusTo(_focusReturn);
        _focusReturn = IntPtr.Zero;
    }

    /// <summary>
    /// Pone el foco en el primer botón del contenido nuevo (después de que la plantilla se dibuje); si la
    /// página no tiene botones (el reloj), en los controles de la isla.
    /// </summary>
    private void FocusFirstControlLater() =>
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, () =>
        {
            if (!_keyboardMode) return;
            if (Keyboard.FocusedElement is UIElement { IsVisible: true } focused && ExpandedHost.IsAncestorOf((DependencyObject)focused))
                return;
            if (FirstFocusable(ExpandedHost) is { } first) first.Focus();
            else ExpandedLayer.MoveFocus(new TraversalRequest(FocusNavigationDirection.First));
        });

    private static UIElement? FirstFocusable(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is System.Windows.Controls.Primitives.ButtonBase { Focusable: true, IsVisible: true, IsEnabled: true } button)
                return button;
            if (child is UIElement { IsVisible: false }) continue;
            if (FirstFocusable(child) is { } found) return found;
        }
        return null;
    }

    private void OnKeyboardModeKey(object sender, KeyEventArgs e)
    {
        if (!_keyboardMode) return;
        switch (e.Key)
        {
            case Key.Escape:
                ExitKeyboardMode(returnFocus: true);
                e.Handled = true;
                break;
            case Key.Left:
            case Key.Right:
                Page(e.Key == Key.Left ? -1 : 1);
                e.Handled = true;
                break;
        }
    }

    // ----------------------------------------------------------------- alto contraste

    private void OnSystemParameterChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SystemParameters.HighContrast)) Dispatcher.BeginInvoke(ApplyContrast);
    }

    /// <summary>
    /// Con un tema de contraste de Windows, la isla usa los colores del sistema (fondo, texto, resaltado)
    /// en lugar de su paleta oscura translúcida, y quita las sombras.
    /// </summary>
    private void ApplyContrast()
    {
        var resources = Application.Current.Resources;
        string[] keys = ["IslandBackground", "IslandStroke", "IslandForeground", "IslandForegroundMuted", "IslandAccent"];
        _normalPalette ??= keys.ToDictionary(k => k, k => resources[k]);

        // NEXUS_HIGH_CONTRAST=1 → simula el alto contraste sin cambiar el tema de Windows (diagnóstico).
        var high = SystemParameters.HighContrast || Environment.GetEnvironmentVariable("NEXUS_HIGH_CONTRAST") == "1";
        if (high)
        {
            resources["IslandBackground"] = SystemColors.WindowBrush;
            resources["IslandStroke"] = SystemColors.WindowTextBrush;
            resources["IslandForeground"] = SystemColors.WindowTextBrush;
            resources["IslandForegroundMuted"] = SystemColors.WindowTextBrush;
            resources["IslandAccent"] = SystemColors.HighlightBrush;
        }
        else
        {
            foreach (var (key, value) in _normalPalette) resources[key] = value;
        }

        IslandShape.BorderThickness = new Thickness(high ? 2 : 1);
        ShadowInner.Visibility = ShadowOuter.Visibility = high ? Visibility.Collapsed : Visibility.Visible;
        _logger.LogInformation("Alto contraste: {State}", high ? "sí" : "no");
    }
}
