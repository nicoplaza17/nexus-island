namespace NexusIsland.Core.Security;

/// <summary>Registros de Windows que vigila el módulo de seguridad.</summary>
public static class SecurityLogs
{
    public const string Security = "Security";
    public const string System = "System";
    public const string Defender = "Microsoft-Windows-Windows Defender/Operational";
    public const string PowerShell = "Microsoft-Windows-PowerShell/Operational";
    public const string TaskScheduler = "Microsoft-Windows-TaskScheduler/Operational";
    public const string Firewall = "Microsoft-Windows-Windows Firewall With Advanced Security/Firewall";

    /// <summary>Qué eventos interesan de cada registro (para pedirle a Windows solo esos).</summary>
    public static readonly IReadOnlyDictionary<string, int[]> WatchedIds = new Dictionary<string, int[]>
    {
        [Security] = [4625, 4624, 4720, 4732, 1102],
        [System] = [7045],
        [Defender] = [1116, 1117, 5001, 5007],
        [PowerShell] = [4104],
        [TaskScheduler] = [106],
        [Firewall] = [2082],
    };
}

/// <summary>De dónde llegan los eventos (en Windows: los registros de eventos, en tiempo real).</summary>
public interface ISecurityEventSource : IDisposable
{
    /// <summary>Llega un evento nuevo (en un hilo cualquiera).</summary>
    event Action<SecurityEvent>? EventArrived;

    /// <summary>Registros que se pueden leer y los que no (por permisos), para mostrarlo en la isla.</summary>
    IReadOnlyList<string> ActiveLogs { get; }
    IReadOnlyList<string> UnavailableLogs { get; }

    void Start();
}

/// <summary>Un evento de Windows, reducido a lo que usan las reglas (campos con nombre de EventData/UserData).</summary>
public sealed record SecurityEvent(string Log, int Id, DateTimeOffset Time, IReadOnlyDictionary<string, string> Data)
{
    public string Get(string name) => Data.TryGetValue(name, out var v) ? v : "";
}

public enum AlertSeverity
{
    /// <summary>Solo queda en el historial (por ejemplo, un servicio de una app de la Store).</summary>
    Info,
    /// <summary>Aviso en la isla.</summary>
    Warning,
    /// <summary>Alerta en la isla, por encima de todo.</summary>
    Critical,
}

/// <param name="Key">Para no repetir la misma alerta (regla + origen).</param>
/// <param name="Evidence">Los datos del evento en texto ("campo: valor"), para el historial y para la explicación con IA.</param>
public sealed record SecurityAlert(
    string RuleId, string Key, AlertSeverity Severity, string Title, string Detail, string Mitre,
    DateTimeOffset Time, string Evidence);
