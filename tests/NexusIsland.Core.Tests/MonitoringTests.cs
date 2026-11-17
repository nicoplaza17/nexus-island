using NexusIsland.Core.Monitoring;

namespace NexusIsland.Core.Tests;

public class CalculatorTests
{
    [Fact]
    public void Cpu_usage_from_system_times()
    {
        var calc = new CpuUsageCalculator();
        Assert.Null(calc.Next(idle: 1000, kernel: 2000, user: 1000)); // primera muestra
        // 100 de tiempo total (kernel incluye ocioso): 25 ocioso → 75 % de uso
        Assert.Equal(75, calc.Next(idle: 1025, kernel: 2050, user: 1050)!.Value, 3);
        Assert.Null(calc.Next(idle: 0, kernel: 0, user: 0)); // contador que retrocede: se descarta
    }

    [Fact]
    public void Network_rate_per_second()
    {
        var calc = new NetworkRateCalculator();
        var t0 = DateTimeOffset.UnixEpoch;
        Assert.Equal((null, null), calc.Next(1000, 500, t0));
        var (down, up) = calc.Next(3000, 1500, t0.AddSeconds(2));
        Assert.Equal(1000, down);
        Assert.Equal(500, up);
        Assert.Equal((null, null), calc.Next(10, 10, t0.AddSeconds(3))); // se reinició el contador
    }

    [Fact]
    public void Formats()
    {
        Assert.Equal("—", MetricFormat.Rate(null));
        Assert.Equal("512 B/s", MetricFormat.Rate(512));
        Assert.Equal($"{1.5:0.#} MB/s", MetricFormat.Rate(1.5 * 1024 * 1024));
        Assert.Equal($"{16.0:0.0} GB", MetricFormat.Gigabytes(16UL * 1024 * 1024 * 1024));
    }
}

public class MonitorAlertEngineTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    private static readonly MonitorAlertSwitches All = new();

    private static SystemSnapshot S(int second, double cpu = 10, double memPct = 50, int? battery = null,
        bool plugged = true, Connectivity net = Connectivity.Internet) => new()
    {
        At = T0.AddSeconds(second),
        CpuPercent = cpu,
        MemoryTotalBytes = 1000,
        MemoryUsedBytes = (ulong)(memPct * 10),
        HasBattery = battery is not null,
        BatteryPercent = battery,
        IsPluggedIn = plugged,
        Connectivity = net,
    };

    [Fact]
    public void Cpu_alert_only_after_sustained_load_and_clears_with_hysteresis()
    {
        var engine = new MonitorAlertEngine(new MonitorThresholds());
        Assert.Empty(engine.Evaluate(S(0, cpu: 95), All));
        Assert.Empty(engine.Evaluate(S(20, cpu: 97), All));
        var raised = Assert.Single(engine.Evaluate(S(30, cpu: 96), All));
        Assert.Equal(new MonitorAlertEvent(MonitorAlertKind.CpuHigh, true, 96), raised);
        Assert.Empty(engine.Evaluate(S(33, cpu: 99), All));       // ya avisó: no repite
        Assert.Empty(engine.Evaluate(S(36, cpu: 85), All));       // bajó, pero dentro del margen
        var cleared = Assert.Single(engine.Evaluate(S(39, cpu: 40), All));
        Assert.False(cleared.Raised);
    }

    [Fact]
    public void Short_spikes_never_alert()
    {
        var engine = new MonitorAlertEngine(new MonitorThresholds());
        for (var t = 0; t < 120; t += 3)
            Assert.Empty(engine.Evaluate(S(t, cpu: t % 2 == 0 ? 100 : 20), All)); // picos que se cortan
    }

    [Fact]
    public void Memory_alert_after_ten_seconds()
    {
        var engine = new MonitorAlertEngine(new MonitorThresholds());
        Assert.Empty(engine.Evaluate(S(0, memPct: 93), All));
        Assert.Equal(MonitorAlertKind.MemoryHigh, Assert.Single(engine.Evaluate(S(10, memPct: 93), All)).Kind);
    }

    [Fact]
    public void Battery_alerts_once_per_level_and_rearm_when_plugged()
    {
        var engine = new MonitorAlertEngine(new MonitorThresholds());
        Assert.Empty(engine.Evaluate(S(0, battery: 15, plugged: true), All)); // enchufada: nada
        Assert.Equal(MonitorAlertKind.BatteryLow, Assert.Single(engine.Evaluate(S(3, battery: 20, plugged: false), All)).Kind);
        Assert.Empty(engine.Evaluate(S(6, battery: 18, plugged: false), All));
        Assert.Equal(MonitorAlertKind.BatteryCritical, Assert.Single(engine.Evaluate(S(9, battery: 10, plugged: false), All)).Kind);
        Assert.False(Assert.Single(engine.Evaluate(S(12, battery: 11, plugged: true), All)).Raised); // al enchufar se cierra
        Assert.Equal(MonitorAlertKind.BatteryCritical, Assert.Single(engine.Evaluate(S(15, battery: 9, plugged: false), All)).Kind);
    }

    [Fact]
    public void Desktop_without_battery_never_alerts()
    {
        var engine = new MonitorAlertEngine(new MonitorThresholds());
        Assert.Empty(engine.Evaluate(S(0, battery: null, plugged: false), All));
    }

    [Fact]
    public void Offline_after_five_seconds_then_restored()
    {
        var engine = new MonitorAlertEngine(new MonitorThresholds());
        Assert.Empty(engine.Evaluate(S(0, net: Connectivity.None), All));
        Assert.Empty(engine.Evaluate(S(3, net: Connectivity.Internet), All)); // corte de 3 s: no molesta
        Assert.Empty(engine.Evaluate(S(6, net: Connectivity.LocalOnly), All));
        Assert.True(Assert.Single(engine.Evaluate(S(11, net: Connectivity.LocalOnly), All)).Raised);
        Assert.False(Assert.Single(engine.Evaluate(S(14, net: Connectivity.Internet), All)).Raised);
    }

    [Fact]
    public void Captive_portal_and_unknown_are_not_offline()
    {
        var engine = new MonitorAlertEngine(new MonitorThresholds());
        Assert.Empty(engine.Evaluate(S(0, net: Connectivity.Constrained), All));
        Assert.Empty(engine.Evaluate(S(60, net: Connectivity.Unknown), All));
    }

    [Fact]
    public void Disabling_an_alert_closes_it()
    {
        var engine = new MonitorAlertEngine(new MonitorThresholds());
        engine.Evaluate(S(0, cpu: 99), All);
        Assert.True(Assert.Single(engine.Evaluate(S(30, cpu: 99), All)).Raised);
        var closed = Assert.Single(engine.Evaluate(S(33, cpu: 99), All with { Cpu = false }));
        Assert.Equal((MonitorAlertKind.CpuHigh, false), (closed.Kind, closed.Raised));
        Assert.Empty(engine.Evaluate(S(36, cpu: 99), All with { Cpu = false }));
    }
}
