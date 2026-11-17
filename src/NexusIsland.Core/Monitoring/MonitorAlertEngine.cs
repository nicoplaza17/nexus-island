namespace NexusIsland.Core.Monitoring;

public enum MonitorAlertKind
{
    CpuHigh,
    MemoryHigh,
    BatteryLow,      // ≤ 20 %
    BatteryCritical, // ≤ 10 %
    Offline,
}

/// <summary>Una alerta que empieza (Raised) o termina (Raised = false).</summary>
public sealed record MonitorAlertEvent(MonitorAlertKind Kind, bool Raised, double Value);

/// <summary>Umbrales. Los valores por defecto evitan falsas alarmas por picos breves.</summary>
public sealed record MonitorThresholds
{
    public double CpuPercent { get; init; } = 90;
    public TimeSpan CpuSustain { get; init; } = TimeSpan.FromSeconds(30);
    public double MemoryPercent { get; init; } = 90;
    public TimeSpan MemorySustain { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>Margen para dar la alerta por terminada (evita que "parpadee" cerca del umbral).</summary>
    public double Hysteresis { get; init; } = 10;

    public int BatteryLowPercent { get; init; } = 20;
    public int BatteryCriticalPercent { get; init; } = 10;

    /// <summary>Tiempo sin Internet antes de avisar (los cortes de 1-2 s no molestan).</summary>
    public TimeSpan OfflineSustain { get; init; } = TimeSpan.FromSeconds(5);
}

/// <summary>Qué alertas están activadas (desde la configuración).</summary>
public sealed record MonitorAlertSwitches(bool Cpu = true, bool Memory = true, bool Battery = true, bool Network = true);

/// <summary>
/// Reglas de alerta: decide, muestra a muestra, cuándo empieza y cuándo termina cada alerta.
/// Lógica pura (sin Windows ni reloj propio): testeable.
/// </summary>
public sealed class MonitorAlertEngine(MonitorThresholds thresholds)
{
    private DateTimeOffset? _cpuHighSince, _memHighSince, _offlineSince;
    private bool _cpuActive, _memActive, _offlineActive, _batteryLowShown, _batteryCriticalShown;

    public MonitorThresholds Thresholds => thresholds;

    public IReadOnlyList<MonitorAlertEvent> Evaluate(SystemSnapshot s, MonitorAlertSwitches enabled)
    {
        var events = new List<MonitorAlertEvent>();

        if (enabled.Cpu && s.CpuPercent is { } cpu)
            Sustained(cpu, thresholds.CpuPercent, thresholds.CpuSustain, s.At, ref _cpuHighSince, ref _cpuActive, MonitorAlertKind.CpuHigh, events);
        else if (!enabled.Cpu) Reset(ref _cpuHighSince, ref _cpuActive, MonitorAlertKind.CpuHigh, events);

        if (enabled.Memory)
            Sustained(s.MemoryPercent, thresholds.MemoryPercent, thresholds.MemorySustain, s.At, ref _memHighSince, ref _memActive, MonitorAlertKind.MemoryHigh, events);
        else Reset(ref _memHighSince, ref _memActive, MonitorAlertKind.MemoryHigh, events);

        if (enabled.Battery) EvaluateBattery(s, events);

        if (enabled.Network) EvaluateNetwork(s, events);
        else Reset(ref _offlineSince, ref _offlineActive, MonitorAlertKind.Offline, events);

        return events;
    }

    private void Sustained(double value, double threshold, TimeSpan sustain, DateTimeOffset now,
        ref DateTimeOffset? since, ref bool active, MonitorAlertKind kind, List<MonitorAlertEvent> events)
    {
        if (value >= threshold)
        {
            since ??= now;
            if (!active && now - since.Value >= sustain)
            {
                active = true;
                events.Add(new(kind, true, value));
            }
        }
        else
        {
            since = null;
            if (active && value < threshold - thresholds.Hysteresis)
            {
                active = false;
                events.Add(new(kind, false, value));
            }
        }
    }

    private void EvaluateBattery(SystemSnapshot s, List<MonitorAlertEvent> events)
    {
        if (!s.HasBattery || s.BatteryPercent is not { } pct) return;
        if (s.IsPluggedIn)
        {
            // Al enchufarla se rearman los avisos para la próxima descarga y se cierran los que estaban.
            if (_batteryCriticalShown) events.Add(new(MonitorAlertKind.BatteryCritical, false, pct));
            else if (_batteryLowShown) events.Add(new(MonitorAlertKind.BatteryLow, false, pct));
            _batteryLowShown = _batteryCriticalShown = false;
            return;
        }
        if (pct <= thresholds.BatteryCriticalPercent && !_batteryCriticalShown)
        {
            _batteryCriticalShown = _batteryLowShown = true;
            events.Add(new(MonitorAlertKind.BatteryCritical, true, pct));
        }
        else if (pct <= thresholds.BatteryLowPercent && !_batteryLowShown)
        {
            _batteryLowShown = true;
            events.Add(new(MonitorAlertKind.BatteryLow, true, pct));
        }
    }

    private void EvaluateNetwork(SystemSnapshot s, List<MonitorAlertEvent> events)
    {
        if (s.Connectivity == Connectivity.Unknown) return;
        var offline = s.Connectivity is Connectivity.None or Connectivity.LocalOnly;
        if (offline)
        {
            _offlineSince ??= s.At;
            if (!_offlineActive && s.At - _offlineSince.Value >= thresholds.OfflineSustain)
            {
                _offlineActive = true;
                events.Add(new(MonitorAlertKind.Offline, true, 0));
            }
        }
        else
        {
            _offlineSince = null;
            if (_offlineActive)
            {
                _offlineActive = false;
                events.Add(new(MonitorAlertKind.Offline, false, 0));
            }
        }
    }

    private static void Reset(ref DateTimeOffset? since, ref bool active, MonitorAlertKind kind, List<MonitorAlertEvent> events)
    {
        since = null;
        if (!active) return;
        active = false;
        events.Add(new(kind, false, 0));
    }
}
