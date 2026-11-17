namespace NexusIsland.Core.Monitoring;

/// <summary>Nivel de conectividad (equivale a NetworkConnectivityLevel de Windows).</summary>
public enum Connectivity
{
    Unknown,
    None,
    LocalOnly,
    Constrained, // portal cautivo (Wi-Fi de hotel/aeropuerto que pide login)
    Internet,
}

/// <summary>Foto del estado del sistema en un instante.</summary>
public sealed record SystemSnapshot
{
    public DateTimeOffset At { get; init; }

    /// <summary>Uso total de CPU, 0 a 100 (null en la primera muestra: hacen falta dos para calcularlo).</summary>
    public double? CpuPercent { get; init; }

    public ulong MemoryUsedBytes { get; init; }
    public ulong MemoryTotalBytes { get; init; }
    public double MemoryPercent => MemoryTotalBytes == 0 ? 0 : 100.0 * MemoryUsedBytes / MemoryTotalBytes;

    /// <summary>False en PCs de escritorio (sin batería).</summary>
    public bool HasBattery { get; init; }
    public int? BatteryPercent { get; init; }
    public bool IsPluggedIn { get; init; }

    public Connectivity Connectivity { get; init; }

    /// <summary>Nombre de la red (por ejemplo, el de la Wi-Fi), si Windows lo informa.</summary>
    public string? NetworkName { get; init; }

    /// <summary>Velocidad de bajada/subida en bytes por segundo (null en la primera muestra).</summary>
    public double? DownloadBytesPerSecond { get; init; }
    public double? UploadBytesPerSecond { get; init; }

    /// <summary>Temperatura aproximada (zona térmica ACPI), si existe un sensor con un valor creíble.</summary>
    public double? ApproxTemperatureCelsius { get; init; }
}

/// <summary>Fuente de métricas del sistema (la implementación real está en Services.Windows).</summary>
public interface ISystemMetricsSource : IDisposable
{
    /// <summary>
    /// Toma una muestra. Lo básico (CPU, RAM, batería, conectividad) es muy barato; la velocidad de red y
    /// la temperatura cuestan más y solo se piden cuando se muestran (panel abierto).
    /// </summary>
    SystemSnapshot Sample(bool includeTemperature, bool includeNetworkRate = true);

    /// <summary>Cambió la conectividad. Puede llegar desde un hilo secundario.</summary>
    event Action? ConnectivityChanged;

    /// <summary>La app que más CPU usa ahora (mide durante ~1 s). Solo para alertas: es más costoso.</summary>
    Task<string?> GetTopCpuProcessAsync();

    /// <summary>La app que más memoria usa ahora.</summary>
    string? GetTopMemoryProcess();
}
