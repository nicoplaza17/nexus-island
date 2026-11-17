using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NexusIsland.App.Island;
using NexusIsland.App.Properties;
using NexusIsland.Core;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Media;
using NexusIsland.Core.Settings;
using NexusIsland.Core.Translation;
using NexusIsland.Services.Windows.Display;
using NexusIsland.Services.Windows.Startup;

namespace NexusIsland.App.Settings;

/// <summary>
/// Ventana de configuración. Cada cambio se aplica y se guarda al instante (no hay botón "Guardar").
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsStore _settings;
    private readonly IslandWindow _island;
    private readonly IMediaSessionService _media;
    private readonly IReadOnlyList<IIslandModule> _modules;
    private readonly IReadOnlySet<string> _runningModules;
    private readonly ILogger<SettingsWindow> _logger;
    private readonly Action _restart;
    private readonly ITranslationService _translator;
    private bool _loading;
    private bool _capturingHotkey;

    public SettingsWindow(SettingsStore settings, IslandWindow island, IMediaSessionService media,
        IReadOnlyList<IIslandModule> modules, IReadOnlySet<string> runningModules,
        ILogger<SettingsWindow> logger, Action restart, ITranslationService translator)
    {
        _translator = translator;
        _settings = settings;
        _island = island;
        _media = media;
        _modules = modules;
        _runningModules = runningModules;
        _logger = logger;
        _restart = restart;

        // Tema Fluent de WPF (.NET 9+): controles con estilo Windows 11, claro u oscuro según el sistema.
        // Microsoft lo marca como experimental (WPF0001); se aplica solo a esta ventana.
#pragma warning disable WPF0001
        ThemeMode = ThemeMode.System;
#pragma warning restore WPF0001

        InitializeComponent();
        LoadValues();
        HookEvents();
    }

    // ------------------------------------------------------------------ carga

    private void LoadValues()
    {
        _loading = true;
        var s = _settings.Current;

        StartWithWindowsBox.IsChecked = s.General.StartWithWindows;
        UpdateStartupWarning();
        HideFullscreenBox.IsChecked = s.General.HideInFullscreen;
        LanguageBox.SelectedItem = LanguageBox.Items.OfType<ComboBoxItem>()
            .FirstOrDefault(i => (string)i.Tag == AppLanguage.Normalize(s.General.Language)) ?? LanguageBox.Items[0];
        UpdateLanguageRestartPanel();

        SizeSmall.IsChecked = s.Island.Size == IslandSize.Small;
        SizeNormal.IsChecked = s.Island.Size == IslandSize.Normal;
        SizeLarge.IsChecked = s.Island.Size == IslandSize.Large;

        BuildMonitorList();

        HotkeyText.Text = _island.HotkeyText;

        ModuleList.Children.Clear();
        foreach (var module in _modules)
        {
            var box = new CheckBox { Content = module.DisplayName, Tag = module.Id, IsChecked = s.IsModuleEnabled(module.Id) };
            box.Checked += OnModuleToggled;
            box.Unchecked += OnModuleToggled;
            ModuleList.Children.Add(box);
        }
        UpdateRestartPanel();

        NotifyTrackBox.IsChecked = s.Media.NotifyOnTrackChange;
        ShowContentBox.IsChecked = s.Notifications.ShowContent;
        BuildMutedList();
        NotificationStatus.Text = NexusIsland.Services.Windows.Notifications.PackageIdentity.HasIdentity
            ? Strings.Settings_HasIdentity
            : Strings.Settings_NoIdentity;
        TaskDownloadsBox.IsChecked = s.Tasks.Downloads;
        TaskCopiesBox.IsChecked = s.Tasks.FileCopies;
        TaskInstallersBox.IsChecked = s.Tasks.Installers;
        AlertCpuBox.IsChecked = s.Monitor.AlertCpu;
        AlertMemoryBox.IsChecked = s.Monitor.AlertMemory;
        AlertBatteryBox.IsChecked = s.Monitor.AlertBattery;
        AlertNetworkBox.IsChecked = s.Monitor.AlertNetwork;
        BuildAppList();
        UpdateTranslatorStatus();

        var version = typeof(SettingsWindow).Assembly.GetName().Version;
        VersionText.Text = $"Nexus Island {version?.ToString(3)} · .NET {Environment.Version}\n{Strings.Settings_ConfigFile} {_settings.FilePath}";
        _loading = false;
    }

    private void BuildMonitorList()
    {
        MonitorList.Children.Clear();
        var island = _settings.Current.Island;
        var monitors = DisplayService.GetMonitors();
        var docked = island.Placement == IslandPlacementMode.DockedTopCenter;
        var selected = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, island.MonitorDevice, StringComparison.OrdinalIgnoreCase));
        if (selected.Handle == IntPtr.Zero) selected = monitors.FirstOrDefault(m => m.IsPrimary);

        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            var label = $"Monitor {i + 1}{(m.IsPrimary ? Strings.Settings_Primary : "")} · {m.Bounds.Width}×{m.Bounds.Height} · {m.Scale:P0}";
            var radio = new RadioButton
            {
                GroupName = "Monitor",
                Content = label,
                Tag = m.DeviceName,
                IsChecked = docked && m.DeviceName == selected.DeviceName,
                Margin = new Thickness(0, 2, 0, 2),
            };
            radio.Checked += (_, _) =>
            {
                if (_loading) return;
                _island.DockOnMonitor((string)radio.Tag);
                FloatingInfo.Visibility = Visibility.Collapsed;
            };
            MonitorList.Children.Add(radio);
        }

        FloatingInfo.Visibility = docked ? Visibility.Collapsed : Visibility.Visible;
        FloatingInfo.Text = Strings.Settings_Floating;
    }

    private void UpdateTranslatorStatus()
    {
        var bytes = _translator.InstalledBytes();
        TranslatorStatus.Text = bytes > 0
            ? string.Format(Strings.Settings_ModelsInstalled, bytes / 1048576.0)
            : Strings.Settings_NoModels;
        DeleteModelsButton.IsEnabled = bytes > 0;
    }

    private void BuildAppList()
    {
        AppList.Children.Clear();
        var media = _settings.Current.Media;

        // Apps vistas en esta sesión + las ya ignoradas (aunque hoy no hayan sonado).
        var apps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, name) in _media.SeenApps) apps[id] = name;
        foreach (var ignored in media.IgnoredApps) apps.TryAdd(ignored.Id, ignored.Name);

        foreach (var (id, name) in apps.OrderBy(a => a.Value, StringComparer.CurrentCultureIgnoreCase))
        {
            var box = new CheckBox
            {
                Content = name,
                ToolTip = id, // el identificador técnico, por si dos apps se llaman igual
                Tag = (id, name),
                IsChecked = media.IsIgnored(id),
                Margin = new Thickness(0, 2, 0, 2),
            };
            box.Checked += OnIgnoredToggled;
            box.Unchecked += OnIgnoredToggled;
            AppList.Children.Add(box);
        }
        NoAppsText.Visibility = apps.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Apps silenciadas (tildadas). Al destildar, vuelve a mostrarse; la fila queda hasta cerrar la ventana.</summary>
    private void BuildMutedList()
    {
        MutedList.Children.Clear();
        foreach (var app in _settings.Current.Notifications.MutedApps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var box = new CheckBox { Content = app.Name, ToolTip = app.Id, IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
            box.Checked += (_, _) => Save(s => { if (!s.Notifications.IsMuted(app.Id)) s.Notifications.MutedApps.Add(app); });
            box.Unchecked += (_, _) => Save(s => s.Notifications.MutedApps.RemoveAll(a => string.Equals(a.Id, app.Id, StringComparison.OrdinalIgnoreCase)));
            MutedList.Children.Add(box);
        }
        NoMutedText.Visibility = MutedList.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ eventos

    private void HookEvents()
    {
        StartWithWindowsBox.Checked += (_, _) => SetStartup(true);
        StartWithWindowsBox.Unchecked += (_, _) => SetStartup(false);
        HideFullscreenBox.Checked += (_, _) => Save(s => s.General.HideInFullscreen = true);
        HideFullscreenBox.Unchecked += (_, _) => Save(s => s.General.HideInFullscreen = false);
        LanguageBox.SelectionChanged += (_, _) =>
        {
            if (LanguageBox.SelectedItem is not ComboBoxItem { Tag: string lang }) return;
            Save(s => s.General.Language = lang);
            UpdateLanguageRestartPanel();
        };
        LanguageRestartButton.Click += (_, _) => _restart();

        SizeSmall.Checked += (_, _) => Save(s => s.Island.Size = IslandSize.Small);
        SizeNormal.Checked += (_, _) => Save(s => s.Island.Size = IslandSize.Normal);
        SizeLarge.Checked += (_, _) => Save(s => s.Island.Size = IslandSize.Large);

        NotifyTrackBox.Checked += (_, _) => Save(s => s.Media.NotifyOnTrackChange = true);
        NotifyTrackBox.Unchecked += (_, _) => Save(s => s.Media.NotifyOnTrackChange = false);

        ShowContentBox.Checked += (_, _) => Save(s => s.Notifications.ShowContent = true);
        ShowContentBox.Unchecked += (_, _) => Save(s => s.Notifications.ShowContent = false);

        TaskDownloadsBox.Checked += (_, _) => Save(s => s.Tasks.Downloads = true);
        TaskDownloadsBox.Unchecked += (_, _) => Save(s => s.Tasks.Downloads = false);
        TaskCopiesBox.Checked += (_, _) => Save(s => s.Tasks.FileCopies = true);
        TaskCopiesBox.Unchecked += (_, _) => Save(s => s.Tasks.FileCopies = false);
        TaskInstallersBox.Checked += (_, _) => Save(s => s.Tasks.Installers = true);
        TaskInstallersBox.Unchecked += (_, _) => Save(s => s.Tasks.Installers = false);

        AlertCpuBox.Checked += (_, _) => Save(s => s.Monitor.AlertCpu = true);
        AlertCpuBox.Unchecked += (_, _) => Save(s => s.Monitor.AlertCpu = false);
        AlertMemoryBox.Checked += (_, _) => Save(s => s.Monitor.AlertMemory = true);
        AlertMemoryBox.Unchecked += (_, _) => Save(s => s.Monitor.AlertMemory = false);
        AlertBatteryBox.Checked += (_, _) => Save(s => s.Monitor.AlertBattery = true);
        AlertBatteryBox.Unchecked += (_, _) => Save(s => s.Monitor.AlertBattery = false);
        AlertNetworkBox.Checked += (_, _) => Save(s => s.Monitor.AlertNetwork = true);
        AlertNetworkBox.Unchecked += (_, _) => Save(s => s.Monitor.AlertNetwork = false);

        ChangeHotkeyButton.Click += (_, _) => StartHotkeyCapture();
        PreviewKeyDown += OnPreviewKeyDown;
        Deactivated += (_, _) => { if (_capturingHotkey) StopHotkeyCapture(Strings.Hotkey_Cancelled); };

        RestartButton.Click += (_, _) => _restart();
        DeleteModelsButton.Click += (_, _) =>
        {
            try { _translator.DeleteModels(); }
            catch (Exception ex) { _logger.LogWarning(ex, "No se pudieron borrar los modelos de traducción"); }
            UpdateTranslatorStatus();
        };
        RefreshAppsButton.Click += (_, _) => BuildAppList();
        OpenSettingsFolderButton.Click += (_, _) => OpenFolder(Path.GetDirectoryName(_settings.FilePath)!);
        OpenLogsButton.Click += (_, _) => OpenFolder(AppPaths.Logs);

        // Si la isla se movió (arrastre) mientras la ventana está abierta, se refleja.
        _settings.Changed += OnSettingsChangedElsewhere;
        Closed += (_, _) => _settings.Changed -= OnSettingsChangedElsewhere;
    }

    private void OnSettingsChangedElsewhere(AppSettings s)
    {
        if (_loading) return;
        _loading = true;
        BuildMonitorList();
        _loading = false;
    }

    private void Save(Action<AppSettings> change)
    {
        if (_loading) return;
        _settings.Update(change);
    }

    private void SetStartup(bool enabled)
    {
        if (_loading) return;
        try
        {
            if (enabled) StartupRegistration.Enable();
            else StartupRegistration.Disable();
            _settings.Update(s => s.General.StartWithWindows = enabled);
            _logger.LogInformation("Inicio con Windows {State}", enabled ? "activado" : "desactivado");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "No se pudo cambiar el inicio con Windows");
            MessageBox.Show(this, Strings.Settings_StartupError,
                "Nexus Island", MessageBoxButton.OK, MessageBoxImage.Warning);
            _loading = true;
            StartWithWindowsBox.IsChecked = !enabled;
            _loading = false;
        }
        UpdateStartupWarning();
    }

    private void UpdateStartupWarning() =>
        StartupWarning.Visibility = StartWithWindowsBox.IsChecked == true && StartupRegistration.GetState() == StartupState.DisabledByUser
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnModuleToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: string id } box) return;
        _settings.Update(s => s.Modules[id] = box.IsChecked == true);
        UpdateRestartPanel();
    }

    private void UpdateRestartPanel()
    {
        var pending = _modules.Any(m => _settings.Current.IsModuleEnabled(m.Id) != _runningModules.Contains(m.Id));
        RestartPanel.Visibility = pending ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>El idioma se aplica al arrancar: si el elegido no es el que está corriendo, ofrece reiniciar.</summary>
    private void UpdateLanguageRestartPanel() =>
        LanguageRestartPanel.Visibility = AppLanguage.Normalize(_settings.Current.General.Language) != App.StartupLanguage
            ? Visibility.Visible
            : Visibility.Collapsed;

    private void OnIgnoredToggled(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not CheckBox { Tag: (string id, string name) } box) return;
        var ignore = box.IsChecked == true;
        _settings.Update(s =>
        {
            s.Media.IgnoredApps.RemoveAll(a => string.Equals(a.Id, id, StringComparison.OrdinalIgnoreCase));
            if (ignore) s.Media.IgnoredApps.Add(new IgnoredApp(id, name));
        });
    }

    // ------------------------------------------------------------------ atajo de teclado

    private void StartHotkeyCapture()
    {
        _capturingHotkey = true;
        HotkeyText.Text = Strings.Hotkey_Press;
        HotkeyStatus.Text = Strings.Hotkey_Help;
        ChangeHotkeyButton.IsEnabled = false;
    }

    private void StopHotkeyCapture(string status)
    {
        _capturingHotkey = false;
        HotkeyText.Text = _island.HotkeyText;
        HotkeyStatus.Text = status;
        ChangeHotkeyButton.IsEnabled = true;
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!_capturingHotkey) return;
        e.Handled = true;

        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (key == Key.Escape)
        {
            StopHotkeyCapture(Strings.Hotkey_Cancelled);
            return;
        }
        // Solo modificadores todavía: esperar la tecla principal.
        if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
            or Key.LWin or Key.RWin or Key.None)
            return;

        var mods = Keyboard.Modifiers;
        var win = Keyboard.IsKeyDown(Key.LWin) || Keyboard.IsKeyDown(Key.RWin);
        if (mods == ModifierKeys.None && !win)
        {
            HotkeyStatus.Text = Strings.Hotkey_NeedModifier;
            return;
        }

        var hotkey = new HotkeySettings
        {
            Ctrl = mods.HasFlag(ModifierKeys.Control),
            Alt = mods.HasFlag(ModifierKeys.Alt),
            Shift = mods.HasFlag(ModifierKeys.Shift),
            Win = win,
            Key = key.ToString(),
        };

        StopHotkeyCapture(_island.TryApplyHotkey(hotkey)
            ? string.Format(Strings.Hotkey_Done, hotkey)
            : string.Format(Strings.Hotkey_Taken, hotkey, _island.HotkeyText));
    }

    private void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo abrir {Path}", path);
        }
    }
}
