namespace NexusIsland.Core.Monitoring;

/// <summary>
/// Uso de CPU a partir de los contadores acumulados de Windows (GetSystemTimes).
/// Ojo: el tiempo "kernel" de Windows INCLUYE el tiempo ocioso.
/// </summary>
public sealed class CpuUsageCalculator
{
    private ulong _idle, _kernel, _user;
    private bool _hasPrevious;

    /// <summary>Devuelve el uso en % desde la muestra anterior, o null si es la primera.</summary>
    public double? Next(ulong idle, ulong kernel, ulong user)
    {
        double? result = null;
        if (_hasPrevious && idle >= _idle && kernel >= _kernel && user >= _user)
        {
            var idleDelta = idle - _idle;
            var total = (kernel - _kernel) + (user - _user);
            if (total > 0) result = Math.Clamp(100.0 * (total - Math.Min(idleDelta, total)) / total, 0, 100);
        }
        (_idle, _kernel, _user, _hasPrevious) = (idle, kernel, user, true);
        return result;
    }
}

/// <summary>Velocidad de red a partir de los bytes acumulados (bajada y subida).</summary>
public sealed class NetworkRateCalculator
{
    private long _received, _sent;
    private DateTimeOffset _at;
    private bool _hasPrevious;

    public (double? Down, double? Up) Next(long totalReceived, long totalSent, DateTimeOffset at)
    {
        (double?, double?) result = (null, null);
        var seconds = (at - _at).TotalSeconds;
        // Si un contador "retrocede" (se reconectó la placa, cambió de interfaz) se descarta esa muestra.
        if (_hasPrevious && seconds > 0 && totalReceived >= _received && totalSent >= _sent)
            result = ((totalReceived - _received) / seconds, (totalSent - _sent) / seconds);
        (_received, _sent, _at, _hasPrevious) = (totalReceived, totalSent, at, true);
        return result;
    }
}

public static class MetricFormat
{
    /// <summary>"512 B/s", "1,4 MB/s".</summary>
    public static string Rate(double? bytesPerSecond)
    {
        if (bytesPerSecond is not { } v) return "—";
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return i == 0 ? $"{v:0} {units[i]}" : $"{v:0.#} {units[i]}";
    }

    /// <summary>"7,4 GB".</summary>
    public static string Gigabytes(ulong bytes) => $"{bytes / 1024d / 1024 / 1024:0.0} GB";
}
