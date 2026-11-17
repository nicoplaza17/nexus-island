using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Monitoring;
using NexusIsland.Core.Settings;

namespace NexusIsland.Modules.Monitor;

/// <summary>
/// Monitoreo del sistema. Dos partes:
///  - Panel "Sistema" (botón ⊞): CPU, RAM, red, batería y temperatura aprox.; se actualiza solo mientras se ve.
///  - Alertas en segundo plano (cada 3 s, costo mínimo): CPU o RAM altas sostenidas, batería baja,
///    sin Internet. Cada una se puede desactivar en la configuración.
/// </summary>
public sealed class MonitorModule : IIslandModule, IQuickActionProvider
{
    public const string ModuleId = "monitor";
    public const string AlertModuleId = "monitor.alert"; // plantillas distintas para las alertas
    private const string PanelKey = "monitor.panel";
    private static readonly TimeSpan AlertAttention = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan RestoredNoticeTime = TimeSpan.FromSeconds(4);

    private readonly ISystemMetricsSource _source;
    private readonly SettingsStore _settings;
    private readonly ILogger<MonitorModule> _logger;
    private readonly MonitorAlertEngine _engine;
    private readonly DispatcherTimer _alertTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<MonitorAlertKind, MonitorAlertViewModel> _alerts = [];
    private IActivityHost? _host;
    private SynchronizationContext? _ui;
    private MonitorPanelViewModel? _panel;

    public MonitorModule(ISystemMetricsSource source, SettingsStore settings, ILogger<MonitorModule> logger)
    {
        _source = source;
        _settings = settings;
        _logger = logger;
        // NEXUS_MONITOR_TEST=1: umbrales mínimos para probar las alertas sin forzar la PC.
        _engine = new MonitorAlertEngine(Environment.GetEnvironmentVariable("NEXUS_MONITOR_TEST") == "1"
            ? new MonitorThresholds { CpuPercent = 1, CpuSustain = TimeSpan.FromSeconds(2), MemoryPercent = 1, MemorySustain = TimeSpan.FromSeconds(2) }
            : new MonitorThresholds());
    }

    public string Id => ModuleId;
    public string DisplayName => "Monitoreo del sistema";
    public Uri? TemplatesUri { get; } =
        new("pack://application:,,,/NexusIsland.Modules.Monitor;component/MonitorTemplates.xaml", UriKind.Absolute);

    public Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _ui = SynchronizationContext.Current;
        _panel = new MonitorPanelViewModel(_source);
        _alertTimer.Tick += (_, _) => EvaluateAlerts();
        _alertTimer.Start();
        _source.ConnectivityChanged += () => _ui?.Post(_ => EvaluateAlerts(), null);
        EvaluateAlerts();
        return Task.CompletedTask;
    }

    public IEnumerable<QuickAction> GetQuickActions() =>
        [new("monitor.panel", "Sistema", "\uE9D9", 50, OpenPanel)];

    private void OpenPanel()
    {
        if (_host is null || _panel is null) return;
        _panel.Refresh(includeTemperature: true);
        _host.Publish(new IslandActivity(PanelKey, ModuleId, ActivityPriority.Panel, _panel));
    }

    private readonly System.Diagnostics.Stopwatch _cost = new();
    private int _checks;

    private void EvaluateAlerts()
    {
        _cost.Start();
        try { EvaluateAlertsCore(); }
        finally
        {
            _cost.Stop();
            // Diagnóstico (NEXUS_LOG=debug): costo promedio de la vigilancia cada 20 revisiones (~1 min).
            if (++_checks % 20 == 0)
            {
                _logger.LogDebug("Vigilancia: {Avg:F2} ms por revisión (promedio de 20)", _cost.Elapsed.TotalMilliseconds / 20);
                _cost.Reset();
            }
        }
    }

    private void EvaluateAlertsCore()
    {
        var m = _settings.Current.Monitor;
        var switches = new MonitorAlertSwitches(m.AlertCpu, m.AlertMemory, m.AlertBattery, m.AlertNetwork);
        SystemSnapshot snapshot;
        try
        {
            // Para las alertas no hace falta la velocidad de red ni la temperatura (lo más caro de medir).
            snapshot = _source.Sample(includeTemperature: false, includeNetworkRate: false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo tomar una muestra del sistema");
            return;
        }
        foreach (var e in _engine.Evaluate(snapshot, switches)) Handle(e, snapshot);
    }

    private void Handle(MonitorAlertEvent e, SystemSnapshot s)
    {
        if (_host is null) return;
        _logger.LogInformation("Alerta {Kind} {State} ({Value:0})", e.Kind, e.Raised ? "activada" : "terminada", e.Value);

        if (!e.Raised)
        {
            if (e.Kind == MonitorAlertKind.Offline)
            {
                // Volvió Internet: aviso breve y se va solo.
                var back = Show(MonitorAlertKind.Offline, MonitorAlertViewModel.Restored(s.NetworkName), ActivityPriority.Active);
                _host.RequestAttention(Key(MonitorAlertKind.Offline), RestoredNoticeTime);
                var close = new DispatcherTimer { Interval = RestoredNoticeTime + TimeSpan.FromSeconds(1) };
                close.Tick += (_, _) => { close.Stop(); if (_alerts.GetValueOrDefault(MonitorAlertKind.Offline) == back) Dismiss(MonitorAlertKind.Offline); };
                close.Start();
            }
            else
            {
                Dismiss(e.Kind);
                if (e.Kind == MonitorAlertKind.BatteryCritical) Dismiss(MonitorAlertKind.BatteryLow);
            }
            return;
        }

        switch (e.Kind)
        {
            case MonitorAlertKind.CpuHigh:
                var cpu = Show(e.Kind, MonitorAlertViewModel.CpuHigh(e.Value), ActivityPriority.Urgent);
                _ = FillTopCpuAsync(cpu);
                break;
            case MonitorAlertKind.MemoryHigh:
                var mem = Show(e.Kind, MonitorAlertViewModel.MemoryHigh(e.Value), ActivityPriority.Urgent);
                mem.SetCulprit(_source.GetTopMemoryProcess());
                break;
            case MonitorAlertKind.BatteryLow:
                Show(e.Kind, MonitorAlertViewModel.Battery((int)e.Value, critical: false), ActivityPriority.Urgent);
                break;
            case MonitorAlertKind.BatteryCritical:
                Dismiss(MonitorAlertKind.BatteryLow);
                Show(e.Kind, MonitorAlertViewModel.Battery((int)e.Value, critical: true), ActivityPriority.Urgent);
                break;
            case MonitorAlertKind.Offline:
                // Mientras no haya Internet queda en la isla (prioridad activa: no tapa la música para siempre).
                Show(e.Kind, MonitorAlertViewModel.Offline(), ActivityPriority.Active);
                break;
        }
        _host.RequestAttention(Key(e.Kind), AlertAttention);
    }

    private async Task FillTopCpuAsync(MonitorAlertViewModel vm)
    {
        try { vm.SetCulprit(await _source.GetTopCpuProcessAsync()); }
        catch (Exception ex) { _logger.LogDebug(ex, "No se pudo identificar qué app usa la CPU"); }
    }

    private MonitorAlertViewModel Show(MonitorAlertKind kind, MonitorAlertViewModel vm, int priority)
    {
        vm.Dismissed += () => Dismiss(kind);
        vm.PanelRequested += () => { Dismiss(kind); OpenPanel(); };
        _alerts[kind] = vm;
        _host?.Publish(new IslandActivity(Key(kind), AlertModuleId, priority, vm));
        return vm;
    }

    private void Dismiss(MonitorAlertKind kind)
    {
        if (_alerts.Remove(kind)) _host?.Remove(Key(kind));
    }

    private static string Key(MonitorAlertKind kind) => $"monitor.alert.{kind}";

    public void Dispose()
    {
        _alertTimer.Stop();
        _panel?.Stop();
    }
}
