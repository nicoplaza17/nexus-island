using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NexusIsland.Services.Windows.Properties;
using NexusIsland.Core.Monitoring;
using WinNet = Windows.Networking.Connectivity;

namespace NexusIsland.Services.Windows.Monitoring;

/// <summary>
/// Métricas del sistema con APIs documentadas de Windows:
///  - CPU: GetSystemTimes · RAM: GlobalMemoryStatusEx · Batería: GetSystemPowerStatus
///  - Red: bytes de las interfaces con puerta de enlace (así no se cuentan dos veces los adaptadores
///    virtuales de VMware/Hyper-V) · Conectividad: Windows.Networking.Connectivity (WinRT)
///  - Temperatura: zonas térmicas ACPI vía PDH (ver ThermalZoneReader): APROXIMADA.
/// </summary>
public sealed class SystemMetricsService : ISystemMetricsSource
{
    private static readonly TimeSpan InterfaceCacheTime = TimeSpan.FromSeconds(30);

    private readonly ILogger<SystemMetricsService> _logger;
    private readonly CpuUsageCalculator _cpu = new();
    private readonly NetworkRateCalculator _net = new();
    private readonly ThermalZoneReader _thermal;
    private readonly object _sync = new();
    private NetworkInterface[] _interfaces = [];
    private DateTimeOffset _interfacesAt;
    private Connectivity _connectivity = Connectivity.Unknown;
    private string? _networkName;

    public SystemMetricsService(ILogger<SystemMetricsService> logger)
    {
        _logger = logger;
        _thermal = new ThermalZoneReader(logger);
        WinNet.NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
        RefreshConnectivity();
    }

    public event Action? ConnectivityChanged;

    public SystemSnapshot Sample(bool includeTemperature, bool includeNetworkRate = true)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.Now;

            double? cpu = null;
            if (GetSystemTimes(out var idle, out var kernel, out var user))
                cpu = _cpu.Next(idle.ToUInt64(), kernel.ToUInt64(), user.ToUInt64());

            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            GlobalMemoryStatusEx(ref mem);

            GetSystemPowerStatus(out var power);
            var hasBattery = power.BatteryFlag != 128 && power.BatteryFlag != 255; // 128 = sin batería, 255 = desconocido

            double? down = null, up = null;
            if (includeNetworkRate)
            {
                var (received, sent) = ReadNetworkBytes(now);
                (down, up) = _net.Next(received, sent, now);
            }

            return new SystemSnapshot
            {
                At = now,
                CpuPercent = cpu,
                MemoryTotalBytes = mem.ullTotalPhys,
                MemoryUsedBytes = mem.ullTotalPhys - mem.ullAvailPhys,
                HasBattery = hasBattery,
                BatteryPercent = hasBattery && power.BatteryLifePercent <= 100 ? power.BatteryLifePercent : null,
                IsPluggedIn = power.ACLineStatus == 1,
                Connectivity = _connectivity,
                NetworkName = _networkName,
                DownloadBytesPerSecond = down,
                UploadBytesPerSecond = up,
                ApproxTemperatureCelsius = includeTemperature ? _thermal.Read() : null,
            };
        }
    }

    // ------------------------------------------------------------------ red

    private (long Received, long Sent) ReadNetworkBytes(DateTimeOffset now)
    {
        if (now - _interfacesAt > InterfaceCacheTime)
        {
            _interfacesAt = now;
            try
            {
                _interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up
                        && n.NetworkInterfaceType is not (NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                        && n.GetIPProperties().GatewayAddresses.Count > 0)
                    .ToArray();
            }
            catch (NetworkInformationException ex)
            {
                _logger.LogDebug(ex, "No se pudieron listar las interfaces de red");
            }
        }

        long received = 0, sent = 0;
        foreach (var nic in _interfaces)
        {
            try
            {
                var stats = nic.GetIPStatistics();
                received += stats.BytesReceived;
                sent += stats.BytesSent;
            }
            catch (NetworkInformationException)
            {
                _interfacesAt = default; // la interfaz desapareció: se vuelve a listar en la próxima muestra
            }
        }
        return (received, sent);
    }

    private void OnNetworkStatusChanged(object? sender)
    {
        RefreshConnectivity();
        lock (_sync) _interfacesAt = default;
        ConnectivityChanged?.Invoke();
    }

    private void RefreshConnectivity()
    {
        try
        {
            var profile = WinNet.NetworkInformation.GetInternetConnectionProfile();
            _networkName = profile?.ProfileName;
            _connectivity = profile?.GetNetworkConnectivityLevel() switch
            {
                WinNet.NetworkConnectivityLevel.InternetAccess => Connectivity.Internet,
                WinNet.NetworkConnectivityLevel.ConstrainedInternetAccess => Connectivity.Constrained,
                WinNet.NetworkConnectivityLevel.LocalAccess => Connectivity.LocalOnly,
                WinNet.NetworkConnectivityLevel.None => Connectivity.None,
                null => Connectivity.None, // sin perfil con Internet = sin conexión
                _ => Connectivity.Unknown,
            };
            _logger.LogDebug("Conectividad: {Level} ({Name})", _connectivity, _networkName ?? "sin nombre");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo leer la conectividad");
            _connectivity = Connectivity.Unknown;
        }
    }

    // ------------------------------------------------------------------ procesos (solo para alertas)

    public async Task<string?> GetTopCpuProcessAsync()
    {
        var first = CpuTimesByName();
        var started = Stopwatch.StartNew();
        await Task.Delay(1000);
        var second = CpuTimesByName();
        var elapsed = started.Elapsed.TotalMilliseconds;

        var top = second
            .Select(kv => (Name: kv.Key, Delta: kv.Value - first.GetValueOrDefault(kv.Key)))
            .OrderByDescending(x => x.Delta)
            .FirstOrDefault();
        if (top.Name is null || top.Delta <= 0) return null;
        var percent = top.Delta / elapsed / Environment.ProcessorCount * 100;
        return $"{FriendlyName(top.Name)} ({percent:0} %)";
    }

    public string? GetTopMemoryProcess()
    {
        var top = Snapshot(p => p.WorkingSet64).OrderByDescending(kv => kv.Value).FirstOrDefault();
        return top.Key is null ? null : $"{FriendlyName(top.Key)} ({top.Value / 1024d / 1024 / 1024:0.0} GB)";
    }

    /// <summary>Suma por nombre: Chrome, por ejemplo, son decenas de procesos "chrome".</summary>
    private static Dictionary<string, double> CpuTimesByName() =>
        Snapshot(p => (long)p.TotalProcessorTime.TotalMilliseconds).ToDictionary(kv => kv.Key, kv => (double)kv.Value);

    private static Dictionary<string, long> Snapshot(Func<Process, long> measure)
    {
        var result = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in Process.GetProcesses())
        {
            try
            {
                // "Idle", "System" y la propia isla (no tiene sentido que se señale a sí misma).
                if (p.Id is 0 or 4 || p.Id == Environment.ProcessId) continue;
                result[p.ProcessName] = result.GetValueOrDefault(p.ProcessName) + measure(p);
            }
            catch (Exception) { /* procesos protegidos o que terminaron: se ignoran */ }
            finally { p.Dispose(); }
        }
        return result;
    }

    private static string FriendlyName(string processName) => processName.ToLowerInvariant() switch
    {
        "chrome" => "Google Chrome",
        "msedge" => "Microsoft Edge",
        "firefox" => "Firefox",
        "code" => "Visual Studio Code",
        "devenv" => "Visual Studio",
        "explorer" => Strings.Process_Explorer,
        "msmpeng" => Strings.Process_Defender,
        "searchindexer" => Strings.Process_SearchIndexer,
        _ => processName,
    };

    public void Dispose()
    {
        WinNet.NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
        _thermal.Dispose();
    }

    // ------------------------------------------------------------------ Win32

    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low, High;
        public readonly ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus, BatteryFlag, BatteryLifePercent, SystemStatusFlag;
        public int BatteryLifeTime, BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);
    [DllImport("kernel32.dll")] private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX status);
    [DllImport("kernel32.dll")] private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS status);
}
