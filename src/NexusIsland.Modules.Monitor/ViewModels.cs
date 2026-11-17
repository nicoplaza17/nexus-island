using System.Windows.Input;
using System.Windows.Threading;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Island;
using NexusIsland.Core.Monitoring;
using NexusIsland.Core.Mvvm;

using NexusIsland.Modules.Monitor.Properties;

namespace NexusIsland.Modules.Monitor;

/// <summary>Panel "Sistema". Se actualiza 1 vez por segundo solo mientras está a la vista.</summary>
public sealed class MonitorPanelViewModel : ObservableObject, IIslandAware, IAccessibleActivity
{
    private readonly ISystemMetricsSource _source;
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private int _ticks;
    private string _cpu = "—", _mem = "—", _down = "—", _up = "—", _network = "", _battery = "", _temp = "";
    private double _cpuValue, _memValue;
    private bool _hasBattery, _hasTemp, _offline;

    public MonitorPanelViewModel(ISystemMetricsSource source)
    {
        _source = source;
        _tick.Tick += (_, _) => Refresh(includeTemperature: ++_ticks % 5 == 0); // la temperatura, cada 5 s
    }

    public string CpuText { get => _cpu; private set => SetProperty(ref _cpu, value); }
    public double CpuValue { get => _cpuValue; private set => SetProperty(ref _cpuValue, value); }
    public string MemoryText { get => _mem; private set => SetProperty(ref _mem, value); }
    public double MemoryValue { get => _memValue; private set => SetProperty(ref _memValue, value); }
    public string DownloadText { get => _down; private set => SetProperty(ref _down, value); }
    public string UploadText { get => _up; private set => SetProperty(ref _up, value); }
    public string NetworkText { get => _network; private set => SetProperty(ref _network, value); }
    public bool IsOffline { get => _offline; private set => SetProperty(ref _offline, value); }
    public string BatteryText { get => _battery; private set => SetProperty(ref _battery, value); }
    public bool HasBattery { get => _hasBattery; private set => SetProperty(ref _hasBattery, value); }
    public string TemperatureText { get => _temp; private set => SetProperty(ref _temp, value); }
    public bool HasTemperature { get => _hasTemp; private set => SetProperty(ref _hasTemp, value); }

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => string.Format(Strings.A11y_Panel, CpuText, MemoryText, NetworkText)
        + (HasBattery ? string.Format(Strings.A11y_PanelBattery, BatteryText) : "");

    public void OnIslandStateChanged(IslandState state, bool isShown)
    {
        if (isShown && state == IslandState.Expanded) _tick.Start();
        else _tick.Stop();
    }

    public void Stop() => _tick.Stop();

    public void Refresh(bool includeTemperature)
    {
        var s = _source.Sample(includeTemperature);
        if (s.CpuPercent is { } cpu)
        {
            CpuText = $"{cpu:0} %";
            CpuValue = cpu / 100;
        }
        MemoryText = string.Format(Strings.Memory_Of, MetricFormat.Gigabytes(s.MemoryUsedBytes), MetricFormat.Gigabytes(s.MemoryTotalBytes));
        MemoryValue = s.MemoryPercent / 100;
        DownloadText = "↓ " + MetricFormat.Rate(s.DownloadBytesPerSecond);
        UploadText = "↑ " + MetricFormat.Rate(s.UploadBytesPerSecond);
        IsOffline = s.Connectivity is Connectivity.None or Connectivity.LocalOnly;
        NetworkText = s.Connectivity switch
        {
            Connectivity.Internet => s.NetworkName ?? Strings.Net_Connected,
            Connectivity.Constrained => string.Format(Strings.Net_Captive, s.NetworkName ?? Strings.Net_Network),
            Connectivity.LocalOnly => Strings.Net_LocalOnly,
            Connectivity.None => Strings.Net_None,
            _ => Strings.Net_Network,
        };
        HasBattery = s.HasBattery && s.BatteryPercent is not null;
        BatteryText = HasBattery ? $"{s.BatteryPercent} %{(s.IsPluggedIn ? Strings.Battery_Plugged : "")}" : "";
        if (includeTemperature)
        {
            HasTemperature = s.ApproxTemperatureCelsius is not null;
            TemperatureText = s.ApproxTemperatureCelsius is { } t ? string.Format(Strings.Temp_Approx, t.ToString("0")) : "";
        }
    }
}

/// <summary>Una alerta del monitoreo (CPU, RAM, batería, red).</summary>
public sealed class MonitorAlertViewModel : ObservableObject, IAccessibleActivity
{
    private string _detail;

    private MonitorAlertViewModel(string glyph, string title, string detail, bool critical, bool canOpenPanel = true)
    {
        Glyph = glyph;
        Title = title;
        _detail = detail;
        IsCritical = critical;
        CanOpenPanel = canOpenPanel;
        DismissCommand = new RelayCommand(() => Dismissed?.Invoke());
        OpenPanelCommand = new RelayCommand(() => PanelRequested?.Invoke());
    }

    public event Action? Dismissed;
    public event Action? PanelRequested;

    public string Glyph { get; }
    public string Title { get; }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public bool IsCritical { get; }
    public bool IsNotCritical => !IsCritical;
    public bool CanOpenPanel { get; }
    public ICommand DismissCommand { get; }
    public ICommand OpenPanelCommand { get; }

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => string.Format(Strings.A11y_Alert, Title, Detail);

    public void SetCulprit(string? process)
    {
        if (!string.IsNullOrEmpty(process)) Detail = string.Format(Strings.Alert_Culprit, Detail, process);
    }

    public static MonitorAlertViewModel CpuHigh(double value) =>
        new("\uE9D9", string.Format(Strings.Alert_CpuTitle, value.ToString("0")), Strings.Alert_CpuDetail, critical: false);

    public static MonitorAlertViewModel MemoryHigh(double value) =>
        new("\uE964", string.Format(Strings.Alert_MemTitle, value.ToString("0")), Strings.Alert_MemDetail, critical: false);

    public static MonitorAlertViewModel Battery(int percent, bool critical) =>
        new(critical ? "\uEBA0" : "\uEBA2", string.Format(Strings.Alert_BatteryTitle, percent),
            critical ? Strings.Alert_BatteryCritical : Strings.Alert_BatteryLow, critical);

    public static MonitorAlertViewModel Offline() =>
        new("\uEB55", Strings.Alert_OfflineTitle, Strings.Alert_OfflineDetail, critical: true, canOpenPanel: false);

    public static MonitorAlertViewModel Restored(string? network) =>
        new("\uE701", Strings.Net_Connected, network is null ? Strings.Alert_RestoredDetail : string.Format(Strings.Alert_RestoredOn, network),
            critical: false, canOpenPanel: false);
}
