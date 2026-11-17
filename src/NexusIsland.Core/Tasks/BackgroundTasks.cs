namespace NexusIsland.Core.Tasks;

public enum TaskKind
{
    Download,
    FileCopy,
    Install,
}

public enum TaskState
{
    Running,
    Completed,
    Failed,
    /// <summary>Terminó pero no se sabe si bien o mal (por ejemplo, un setup.exe que se cerró).</summary>
    Ended,
    /// <summary>Se canceló o desapareció sin terminar (descarga cancelada).</summary>
    Cancelled,
}

/// <summary>Una tarea en segundo plano detectada (descarga, copia, instalación).</summary>
/// <param name="Id">Identificador estable mientras dura la tarea (ruta del archivo, id de ventana, id de proceso).</param>
/// <param name="Progress">0 a 1 si se conoce; null si no (las descargas no informan el total).</param>
/// <param name="Location">Carpeta o archivo relacionado (para "Abrir carpeta").</param>
public sealed record BackgroundTask(
    string Id,
    TaskKind Kind,
    string Title,
    TaskState State,
    DateTimeOffset StartedAt,
    DateTimeOffset UpdatedAt,
    double? Progress = null,
    long? Bytes = null,
    double? BytesPerSecond = null,
    string? Detail = null,
    string? Location = null);

/// <summary>Fuente de tareas (implementación real en Services.Windows).</summary>
public interface IBackgroundTaskSource : IDisposable
{
    /// <summary>Una tarea empezó, avanzó o terminó. Puede llegar desde un hilo secundario.</summary>
    event Action<BackgroundTask>? Changed;

    void Start(TaskWatchOptions options);

    void UpdateOptions(TaskWatchOptions options);
}

public sealed record TaskWatchOptions(bool Downloads = true, bool FileCopies = true, bool Installers = true);

/// <summary>Lo que la isla muestra de las tareas (resumen ya filtrado por relevancia).</summary>
/// <param name="Primary">La tarea principal (la más avanzada o la más reciente).</param>
/// <param name="RunningCount">Cuántas tareas relevantes hay en curso.</param>
public sealed record TaskSummary(BackgroundTask Primary, int RunningCount, IReadOnlyList<BackgroundTask> Running);

/// <summary>
/// Sistema de relevancia: decide qué tareas merecen aparecer en la isla.
///  - Una tarea en curso aparece recién si dura más que <see cref="MinVisibleDuration"/> (las descargas
///    chiquitas no molestan).
///  - Al terminar, se informa una vez (para el aviso breve) solo si llegó a ser visible o si falló.
/// Lógica pura: testeable.
/// </summary>
public sealed class TaskRelevance
{
    public static readonly TimeSpan MinVisibleDuration = TimeSpan.FromSeconds(3);

    private readonly Dictionary<string, BackgroundTask> _running = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _shown = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Procesa un cambio. Devuelve la tarea terminada a avisar (o null).</summary>
    public BackgroundTask? Apply(BackgroundTask task)
    {
        if (task.State == TaskState.Running)
        {
            _running[task.Id] = task;
            return null;
        }

        _running.Remove(task.Id);
        var wasShown = _shown.Remove(task.Id);
        // Las canceladas no se avisan; las fallidas siempre (aunque hayan sido cortas: es importante).
        if (task.State == TaskState.Cancelled) return null;
        return wasShown || task.State == TaskState.Failed || task.Kind == TaskKind.Install ? task : null;
    }

    /// <summary>Resumen de lo visible ahora (null si no hay nada relevante en curso).</summary>
    public TaskSummary? Summary(DateTimeOffset now)
    {
        var visible = _running.Values
            .Where(t => now - t.StartedAt >= MinVisibleDuration || _shown.Contains(t.Id))
            .OrderByDescending(t => t.Progress ?? -1)
            .ThenByDescending(t => t.StartedAt)
            .ToList();
        foreach (var t in visible) _shown.Add(t.Id);
        return visible.Count == 0 ? null : new TaskSummary(visible[0], visible.Count, visible);
    }

    public int RunningCount => _running.Count;
}

/// <summary>
/// Archivos temporales de descarga de los navegadores:
///  - Chrome / Edge: "archivo.ext.crdownload" (al principio "Unconfirmed 123456.crdownload").
///  - Firefox: "archivo.ext.part".
///  - Edge antiguo: "archivo.ext.partial".
/// </summary>
public static class DownloadFiles
{
    private static readonly (string Extension, string Browser)[] TempExtensions =
    [
        (".crdownload", "Chrome / Edge"),
        (".part", "Firefox"),
        (".partial", "Edge"),
    ];

    /// <summary>Si es un temporal de descarga, devuelve el nombre final y el navegador.</summary>
    public static bool TryParseTemp(string path, out string finalName, out string browser)
    {
        var name = Path.GetFileName(path);
        foreach (var (ext, b) in TempExtensions)
        {
            if (!name.EndsWith(ext, StringComparison.OrdinalIgnoreCase) || name.Length <= ext.Length) continue;
            finalName = name[..^ext.Length];
            browser = b;
            // "Unconfirmed 123456" (Chrome aún no sabe el nombre): se muestra genérico.
            if (finalName.StartsWith("Unconfirmed ", StringComparison.OrdinalIgnoreCase)) finalName = "Descarga";
            return true;
        }
        finalName = browser = "";
        return false;
    }

    public static bool IsTemp(string path) => TryParseTemp(path, out _, out _);
}

/// <summary>Lectura del porcentaje en el título de la ventana de progreso del Explorador ("37 % completado").</summary>
public static class CopyProgressTitle
{
    private static readonly System.Text.RegularExpressions.Regex Percent =
        new(@"(\d{1,3})\s?%", System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    public static double? Parse(string? title)
    {
        if (string.IsNullOrWhiteSpace(title)) return null;
        var m = Percent.Match(title);
        if (!m.Success || !int.TryParse(m.Groups[1].Value, out var p) || p > 100) return null;
        return p / 100.0;
    }
}

/// <summary>
/// Heurística de "instalador" por nombre de proceso. Aproximada: los servicios de fondo con "install" en el
/// nombre (por ejemplo "OmenInstallMonitor" de HP) se descartan por palabras típicas de servicio.
/// </summary>
public static class InstallerProcess
{
    private static readonly string[] Excluded = ["msiexec", "nexusisland", "trustedinstaller", "tiworker", "setuphost", "installagent"];

    /// <summary>Palabras de procesos que corren siempre en segundo plano (no son una instalación en curso).</summary>
    private static readonly string[] ServiceWords = ["monitor", "service", "svc", "agent", "helper", "updater", "update", "broker", "host", "daemon", "tray"];

    public static bool Looks(string processName)
    {
        var n = processName.ToLowerInvariant();
        if (Excluded.Any(e => n == e) || ServiceWords.Any(n.Contains)) return false;
        return n.Contains("setup") || n.Contains("install") || (n.StartsWith("unins") && n.Length <= 8);
    }

    /// <summary>
    /// Un proceso que ya corría cuando empezó la vigilancia y no tiene ventana es algo de fondo,
    /// no una instalación que el usuario acaba de abrir.
    /// </summary>
    public static bool IsRelevant(DateTimeOffset processStart, DateTimeOffset watchStart, bool hasWindow) =>
        hasWindow || processStart >= watchStart;
}
