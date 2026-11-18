using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NexusIsland.App.Island;
using NexusIsland.App.Tray;
using NexusIsland.Core;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Logging;
using NexusIsland.Core.Media;
using NexusIsland.Core.Settings;
using NexusIsland.App.Settings;
using NexusIsland.Services.Windows.Startup;
using NexusIsland.Modules.Media;
using NexusIsland.Modules.Tools.Clipboard;
using NexusIsland.Modules.Tools.Reminders;
using NexusIsland.Modules.Tools.Stopwatch;
using NexusIsland.Modules.Tools.Timer;
using NexusIsland.Modules.Tools.Translate;
using NexusIsland.Core.Translation;
using NexusIsland.Services.Windows.Translation;
using NexusIsland.Core.Tools;
using NexusIsland.Services.Windows.Clipboard;
using NexusIsland.Core.Monitoring;
using NexusIsland.Modules.Monitor;
using NexusIsland.Services.Windows.Monitoring;
using NexusIsland.Core.Notifications;
using NexusIsland.Modules.Notifications;
using NexusIsland.Services.Windows.Notifications;
using NexusIsland.Core.Tasks;
using NexusIsland.Modules.Tasks;
using NexusIsland.Services.Windows.Tasks;
using NexusIsland.Services.Windows.Media;

namespace NexusIsland.App;

public partial class App : Application
{
    // "Local\" = una instancia por sesión de usuario de Windows.
    private const string SingleInstanceMutexName = @"Local\NexusIsland.SingleInstance";

    private Mutex? _singleInstanceMutex;
    private ServiceProvider? _services;
    private ILogger<App>? _logger;
    private SettingsWindow? _settingsWindow;
    private IIslandModule[] _allModules = [];
    private HashSet<string> _runningModuleIds = new(StringComparer.OrdinalIgnoreCase);
    private bool _restartRequested;

    /// <summary>Idioma con el que arrancó la interfaz ("auto", "es" o "en"); cambiarlo pide reiniciar.</summary>
    public static string StartupLanguage { get; private set; } = AppLanguage.Auto;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // 1) Instancia única: si ya hay una isla corriendo, esta se cierra sin hacer nada.
        _singleInstanceMutex = new Mutex(initiallyOwned: true, SingleInstanceMutexName, out var createdNew);
        if (!createdNew)
        {
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown();
            return;
        }

        // 2) Servicios (inyección de dependencias) + logging a archivo.
        _services = ConfigureServices();
        _logger = _services.GetRequiredService<ILogger<App>>();
        HookUnhandledExceptions();

        _logger.LogInformation("Nexus Island {Version} iniciando — Windows {Os}, .NET {Runtime}, proceso {Arch}",
            typeof(App).Assembly.GetName().Version, Environment.OSVersion.Version,
            Environment.Version, Environment.Is64BitProcess ? "x64" : "x86");

        ConfigureRendering();
        _logger.LogInformation("Identidad de paquete: {Identity}", PackageIdentity.FullName ?? "ninguna (sin acceso a notificaciones)");

        var settings = _services.GetRequiredService<SettingsStore>();
        // NEXUS_LANG=en|es|auto → fuerza el idioma sin tocar la configuración (diagnóstico).
        ApplyLanguage(Environment.GetEnvironmentVariable("NEXUS_LANG") ?? settings.Current.General.Language);
        SyncStartupRegistration(settings.Current);

        // 3) Módulos activados (configuración) + plantillas visuales de cada uno.
        // NEXUS_DISABLE_MODULES=media,otro → además desactiva módulos (diagnóstico).
        var disabled = (Environment.GetEnvironmentVariable("NEXUS_DISABLE_MODULES") ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        _allModules = _services.GetServices<IIslandModule>().ToArray();
        var modules = _allModules
            .Where(m => settings.Current.IsModuleEnabled(m.Id) && !disabled.Contains(m.Id, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        _runningModuleIds = modules.Select(m => m.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var off in _allModules.Except(modules))
            _logger.LogInformation("Módulo '{Module}' desactivado", off.DisplayName);
        LoadModuleTemplates(modules);

        // 4) La isla y el ícono de la bandeja.
        var island = _services.GetRequiredService<IslandWindow>();
        island.SetQuickActionProviders(modules.OfType<IQuickActionProvider>());
        island.Show();

        // Al volver de suspensión, los avisos multimedia de Windows a veces dejan de llegar: se reconecta.
        var media = _services.GetRequiredService<IMediaSessionService>();
        island.SystemResumed += async () =>
        {
            try { await media.ReconnectAsync(); }
            catch (Exception ex) { _logger?.LogWarning(ex, "No se pudo reconectar la multimedia tras suspensión"); }
        };

        var tray = _services.GetRequiredService<TrayIconService>();
        tray.SettingsRequested += OpenSettings;
        tray.Initialize();
        if (Environment.GetEnvironmentVariable("NEXUS_OPEN_SETTINGS") == "1") OpenSettings(); // diagnóstico

        // 5) Módulos: cada uno arranca por separado; si uno falla, el resto sigue funcionando.
        foreach (var module in modules) StartModule(module);

        LogMemoryUsageLater();
    }

    /// <summary>
    /// Idioma de la interfaz. Tiene que aplicarse antes de crear ventanas y plantillas: los textos de XAML
    /// ({x:Static}) se leen una sola vez al cargarse. Por eso cambiarlo requiere reiniciar.
    /// </summary>
    private void ApplyLanguage(string? setting)
    {
        StartupLanguage = AppLanguage.Normalize(setting);
        if (AppLanguage.Resolve(setting, CultureInfo.CurrentUICulture) is { } culture)
        {
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        _logger?.LogInformation("Idioma: {Setting} → interfaz en {Culture}", StartupLanguage, CultureInfo.CurrentUICulture.Name);
    }

    /// <summary>Abre la configuración (una sola ventana; si ya está abierta, la trae al frente).</summary>
    public void OpenSettings()
    {
        if (_settingsWindow is { IsLoaded: true })
        {
            if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
            _settingsWindow.Activate();
            return;
        }

        if (_services is null) return;
        _settingsWindow = new SettingsWindow(
            _services.GetRequiredService<SettingsStore>(),
            _services.GetRequiredService<IslandWindow>(),
            _services.GetRequiredService<IMediaSessionService>(),
            _allModules,
            _runningModuleIds,
            _services.GetRequiredService<ILogger<SettingsWindow>>(),
            restart: Restart,
            _services.GetRequiredService<ITranslationService>());
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    /// <summary>Reinicia la app (por ejemplo, para aplicar módulos activados/desactivados).</summary>
    public void Restart()
    {
        _logger?.LogInformation("Reinicio pedido por el usuario");
        _restartRequested = true;
        Shutdown();
    }

    /// <summary>
    /// Si el inicio con Windows está activado pero apunta a otra ubicación del ejecutable
    /// (por ejemplo, se movió la carpeta), se actualiza. Si el usuario lo desactivó desde el
    /// Administrador de tareas, no se toca.
    /// </summary>
    private void SyncStartupRegistration(AppSettings settings)
    {
        try
        {
            var state = StartupRegistration.GetState();
            if (settings.General.StartWithWindows && (state == StartupState.Disabled || StartupRegistration.PointsElsewhere()))
            {
                StartupRegistration.Enable();
                _logger?.LogInformation("Inicio con Windows actualizado a {Path}", StartupRegistration.CurrentExecutable);
            }
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "No se pudo sincronizar el inicio con Windows");
        }
    }

    private void LoadModuleTemplates(IEnumerable<IIslandModule> modules)
    {
        // Varios módulos pueden compartir un mismo diccionario (las herramientas): se carga una sola vez.
        foreach (var uri in modules.Select(m => m.TemplatesUri).OfType<Uri>().Distinct())
        {
            try
            {
                Resources.MergedDictionaries.Add(new ResourceDictionary { Source = uri });
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "No se pudieron cargar las plantillas {Uri}", uri);
            }
        }
    }

    private async void StartModule(IIslandModule module)
    {
        try
        {
            var arbiter = _services!.GetRequiredService<ActivityArbiter>();
            await module.StartAsync(arbiter, CancellationToken.None);
            _logger?.LogInformation("Módulo \"{Module}\" iniciado", module.DisplayName);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "El módulo \"{Module}\" no pudo iniciar", module.DisplayName);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _logger?.LogInformation("Nexus Island cerrando (código {Code})", e.ApplicationExitCode);
        _services?.Dispose(); // libera el ícono de bandeja y cierra el archivo de log
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();

        // El reinicio se lanza recién ahora, con el candado de instancia única ya liberado.
        if (_restartRequested && Environment.ProcessPath is { } exe)
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false });

        base.OnExit(e);
    }

    /// <summary>
    /// Render por software por defecto. Medido en la Etapa 0 (Release, Win 11, 125%):
    ///   GPU      ≈ 70 MB de RAM privada (WPF carga Direct3D + el driver de video)
    ///   Software ≈ 19 MB de RAM privada
    /// La isla es chica y es una ventana con transparencia, así que el costo de CPU por software es bajo.
    /// Para diagnosticar con GPU: variable de entorno NEXUS_RENDER=gpu.
    /// </summary>
    private void ConfigureRendering()
    {
        var useGpu = string.Equals(Environment.GetEnvironmentVariable("NEXUS_RENDER"), "gpu",
            StringComparison.OrdinalIgnoreCase);
        if (!useGpu)
            System.Windows.Media.RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
        _logger?.LogInformation("Modo de render: {Mode}", useGpu ? "GPU" : "software");
    }

    private static ServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        services.AddLogging(builder =>
        {
            // NEXUS_LOG=debug registra también los cambios de estado de la isla (diagnóstico).
            var level = string.Equals(Environment.GetEnvironmentVariable("NEXUS_LOG"), "debug",
                StringComparison.OrdinalIgnoreCase) ? LogLevel.Debug : LogLevel.Information;
            builder.ClearProviders();
            builder.SetMinimumLevel(level);
            builder.AddProvider(new FileLoggerProvider(AppPaths.Logs, level));
        });

        // Configuración del usuario: settings.json en la carpeta de datos (AppPaths.RoamingData)
        services.AddSingleton(sp => new SettingsStore(
            System.IO.Path.Combine(AppPaths.RoamingData, "settings.json"), sp.GetRequiredService<ILogger<SettingsStore>>()));

        // Núcleo de la isla
        services.AddSingleton<ActivityArbiter>();
        services.AddSingleton<NexusIsland.Services.Windows.Accessibility.TextScaleWatcher>();
        services.AddSingleton<ClockViewModel>();
        services.AddSingleton<IslandWindow>();
        services.AddSingleton<TrayIconService>();

        // Servicios de Windows
        services.AddSingleton<IMediaSessionService, GsmtcMediaSessionService>();
        services.AddSingleton<IAudioLevelMeter, CoreAudioLevelMeter>();

        services.AddSingleton<IClipboardMonitor, ClipboardMonitor>();
        services.AddSingleton<ISystemMetricsSource, SystemMetricsService>();
        services.AddSingleton<INotificationSource, UserNotificationService>();
        services.AddSingleton<IBackgroundTaskSource, BackgroundTaskService>();
        services.AddSingleton(sp => new ReminderStore(
            System.IO.Path.Combine(AppPaths.RoamingData, "reminders.json"), sp.GetRequiredService<ILogger<ReminderStore>>()));

        // Módulos (se activan/desactivan desde la Configuración). El orden define el orden en la configuración.
        services.AddSingleton<IIslandModule, MediaModule>();
        services.AddSingleton<IIslandModule, TimerModule>();
        services.AddSingleton<IIslandModule, StopwatchModule>();
        services.AddSingleton<IIslandModule, ReminderModule>();
        services.AddSingleton<IIslandModule, ClipboardModule>();
        services.AddSingleton<ITranslationService, LocalTranslationService>();
        services.AddSingleton<IIslandModule, TranslateModule>();
        services.AddSingleton<IIslandModule, MonitorModule>();
        services.AddSingleton<IIslandModule, NotificationsModule>();
        services.AddSingleton<IIslandModule, TasksModule>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private void HookUnhandledExceptions()
    {
        // Errores en el hilo de la interfaz: se registran y la app sigue viva.
        DispatcherUnhandledException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "Excepción no controlada en el hilo de UI");
            args.Handled = true;
        };

        // Errores en otros hilos: no se pueden "salvar", pero al menos quedan en el log.
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _logger?.LogCritical(args.ExceptionObject as Exception, "Excepción fatal (terminando: {Terminating})",
                args.IsTerminating);

        // Tareas async cuyo error nadie observó.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _logger?.LogError(args.Exception, "Excepción no observada en una Task");
            args.SetObserved();
        };
    }

    /// <summary>Registra el consumo de RAM 10 s después de arrancar (criterio de la Etapa 0).</summary>
    private void LogMemoryUsageLater()
    {
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            using var p = Process.GetCurrentProcess();
            p.Refresh();
            _logger?.LogInformation(
                "Memoria: privada ≈ {Private:F1} MB, conjunto de trabajo ≈ {WorkingSet:F1} MB, heap .NET ≈ {Managed:F1} MB",
                p.PrivateMemorySize64 / 1048576.0, p.WorkingSet64 / 1048576.0, GC.GetTotalMemory(false) / 1048576.0);
        };
        timer.Start();
    }
}
