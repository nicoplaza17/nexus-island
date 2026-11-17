using System.Windows.Input;
using System.Windows.Threading;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Island;
using NexusIsland.Core.Monitoring;
using NexusIsland.Core.Mvvm;

namespace NexusIsland.Modules.Monitor;

/// <summary>Panel "Sistema". Se actualiza 1 vez por segundo solo mientras está a la vista.</summary>
public sealed class MonitorPanelViewModel : ObservableObject, IIslandAware
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
        MemoryText = $"{MetricFormat.Gigabytes(s.MemoryUsedBytes)} de {MetricFormat.Gigabytes(s.MemoryTotalBytes)}";
        MemoryValue = s.MemoryPercent / 100;
        DownloadText = "↓ " + MetricFormat.Rate(s.DownloadBytesPerSecond);
        UploadText = "↑ " + MetricFormat.Rate(s.UploadBytesPerSecond);
        IsOffline = s.Connectivity is Connectivity.None or Connectivity.LocalOnly;
        NetworkText = s.Connectivity switch
        {
            Connectivity.Internet => s.NetworkName ?? "Conectado",
            Connectivity.Constrained => $"{s.NetworkName ?? "Red"} · pide iniciar sesión",
            Connectivity.LocalOnly => "Red local sin Internet",
            Connectivity.None => "Sin conexión",
            _ => "Red",
        };
        HasBattery = s.HasBattery && s.BatteryPercent is not null;
        BatteryText = HasBattery ? $"{s.BatteryPercent} %{(s.IsPluggedIn ? " · enchufada" : "")}" : "";
        if (includeTemperature)
        {
            HasTemperature = s.ApproxTemperatureCelsius is not null;
            TemperatureText = s.ApproxTemperatureCelsius is { } t ? $"{t:0} °C aprox." : "";
        }
    }
}

/// <summary>Una alerta del monitoreo (CPU, RAM, batería, red).</summary>
public sealed class MonitorAlertViewModel : ObservableObject
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

    public void SetCulprit(string? process)
    {
        if (!string.IsNullOrEmpty(process)) Detail = $"{Detail} Más uso: {process}.";
    }

    public static MonitorAlertViewModel CpuHigh(double value) =>
        new("\uE9D9", $"CPU al {value:0} %", "Hace más de 30 s que el procesador está casi al máximo.", critical: false);

    public static MonitorAlertViewModel MemoryHigh(double value) =>
        new("\uE964", $"Memoria al {value:0} %", "Queda poca RAM libre: la PC puede ponerse lenta.", critical: false);

    public static MonitorAlertViewModel Battery(int percent, bool critical) =>
        new(critical ? "\uEBA0" : "\uEBA2", $"Batería al {percent} %",
            critical ? "Enchufá la notebook: queda muy poca carga." : "Conviene enchufar la notebook.", critical);

    public static MonitorAlertViewModel Offline() =>
        new("\uEB55", "Sin Internet", "Se perdió la conexión. Te aviso cuando vuelva.", critical: true, canOpenPanel: false);

    public static MonitorAlertViewModel Restored(string? network) =>
        new("\uE701", "Conectado", network is null ? "Volvió la conexión a Internet." : $"Volvió la conexión ({network}).",
            critical: false, canOpenPanel: false);
}
