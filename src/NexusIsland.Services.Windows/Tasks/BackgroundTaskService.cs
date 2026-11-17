using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Tasks;

namespace NexusIsland.Services.Windows.Tasks;

/// <summary>
/// Detecta tareas en segundo plano. Windows NO tiene una API para "qué está descargando o instalando
/// cada app", así que se combinan tres fuentes, cada una con su nivel de certeza:
///  - Descargas: archivos temporales de los navegadores en la carpeta Descargas (confiable; sin %).
///  - Copias del Explorador: título de su ventana de progreso ("37 % completado") (confiable; con %).
///  - Instaladores: eventos de Windows Installer (MSI, confiable) + procesos "setup/install" (aproximado).
/// </summary>
public sealed class BackgroundTaskService : IBackgroundTaskSource
{
    private readonly ILogger<BackgroundTaskService> _logger;
    private readonly object _sync = new();
    private TaskWatchOptions _options = new();
    private DownloadWatcher? _downloads;
    private Timer? _copyTimer, _processTimer;
    private EventLogWatcher? _msiWatcher;
    private readonly Dictionary<IntPtr, BackgroundTask> _copies = [];
    private readonly Dictionary<int, BackgroundTask> _installers = [];
    private readonly DateTimeOffset _watchStarted = DateTimeOffset.Now;

    public BackgroundTaskService(ILogger<BackgroundTaskService> logger) => _logger = logger;

    public event Action<BackgroundTask>? Changed;

    public void Start(TaskWatchOptions options) => UpdateOptions(options);

    public void UpdateOptions(TaskWatchOptions options)
    {
        lock (_sync)
        {
            _options = options;
            // Descargas
            if (options.Downloads && _downloads is null)
            {
                try
                {
                    _downloads = new DownloadWatcher(KnownFolders.Downloads, Raise, _logger);
                    _logger.LogInformation("Vigilando descargas en {Folder}", KnownFolders.Downloads);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "No se pudo vigilar la carpeta Descargas"); }
            }
            else if (!options.Downloads) { _downloads?.Dispose(); _downloads = null; }

            // Copias del Explorador
            if (options.FileCopies) _copyTimer ??= new Timer(_ => CheckCopies(), null, TimeSpan.Zero, TimeSpan.FromSeconds(2));
            else { _copyTimer?.Dispose(); _copyTimer = null; }

            // Instaladores
            if (options.Installers)
            {
                _processTimer ??= new Timer(_ => CheckInstallerProcesses(), null, TimeSpan.Zero, TimeSpan.FromSeconds(4));
                if (_msiWatcher is null) StartMsiWatcher();
            }
            else
            {
                _processTimer?.Dispose(); _processTimer = null;
                _msiWatcher?.Dispose(); _msiWatcher = null;
            }
        }
    }

    private void Raise(BackgroundTask task)
    {
        try { Changed?.Invoke(task); }
        catch (Exception ex) { _logger.LogDebug(ex, "Error al avisar una tarea"); }
    }

    // ------------------------------------------------------------------ copias del Explorador

    private void CheckCopies()
    {
        try
        {
            var now = DateTimeOffset.Now;
            var seen = new HashSet<IntPtr>();
            foreach (var (hwnd, title) in OperationWindows())
            {
                seen.Add(hwnd);
                var progress = CopyProgressTitle.Parse(title);
                BackgroundTask task;
                lock (_sync)
                {
                    task = _copies.TryGetValue(hwnd, out var prev)
                        ? prev with { Progress = progress ?? prev.Progress, UpdatedAt = now, Detail = title }
                        : new BackgroundTask($"copy:{hwnd}", TaskKind.FileCopy, "Copiando archivos", TaskState.Running, now, now, progress, Detail: title);
                    _copies[hwnd] = task;
                }
                Raise(task);
            }

            List<BackgroundTask> ended;
            lock (_sync)
            {
                ended = _copies.Where(kv => !seen.Contains(kv.Key)).Select(kv => kv.Value).ToList();
                foreach (var e in ended) _copies.Remove(_copies.First(kv => kv.Value.Id == e.Id).Key);
            }
            // Si la ventana se cerró cerca del final, se considera completada; si no, se canceló.
            foreach (var e in ended)
                Raise(e with { State = e.Progress is >= 0.9 ? TaskState.Completed : TaskState.Cancelled, Progress = e.Progress is >= 0.9 ? 1 : e.Progress, UpdatedAt = now });
        }
        catch (Exception ex) { _logger.LogDebug(ex, "No se pudieron revisar las copias del Explorador"); }
    }

    /// <summary>Ventanas de progreso de operaciones de archivos del Explorador (clase "OperationStatusWindow").</summary>
    private static List<(IntPtr Hwnd, string Title)> OperationWindows()
    {
        var result = new List<(IntPtr, string)>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd)) return true;
            var cls = new StringBuilder(64);
            GetClassName(hwnd, cls, cls.Capacity);
            if (cls.ToString() == "OperationStatusWindow")
            {
                var title = new StringBuilder(256);
                GetWindowText(hwnd, title, title.Capacity);
                result.Add((hwnd, title.ToString()));
            }
            return true;
        }, IntPtr.Zero);
        return result;
    }

    // ------------------------------------------------------------------ instaladores

    private void CheckInstallerProcesses()
    {
        try
        {
            var now = DateTimeOffset.Now;
            var alive = new HashSet<int>();
            foreach (var p in Process.GetProcesses())
            {
                using (p)
                {
                    if (p.Id == Environment.ProcessId || !InstallerProcess.Looks(p.ProcessName)) continue;
                    alive.Add(p.Id);
                    bool isNew;
                    lock (_sync) isNew = !_installers.ContainsKey(p.Id);
                    if (!isNew) continue;

                    string title;
                    bool hasWindow;
                    try
                    {
                        hasWindow = p.MainWindowHandle != IntPtr.Zero;
                        title = string.IsNullOrWhiteSpace(p.MainWindowTitle) ? p.ProcessName : p.MainWindowTitle;
                    }
                    catch { title = p.ProcessName; hasWindow = false; }
                    DateTimeOffset started;
                    try { started = p.StartTime; } catch { started = now; }
                    if (!InstallerProcess.IsRelevant(started, _watchStarted, hasWindow))
                    {
                        alive.Remove(p.Id); // de fondo desde antes: se ignora
                        continue;
                    }
                    var task = new BackgroundTask($"setup:{p.Id}", TaskKind.Install, title, TaskState.Running, started, now,
                        Detail: "Instalador en curso");
                    lock (_sync) _installers[p.Id] = task;
                    Raise(task);
                }
            }

            List<BackgroundTask> ended;
            lock (_sync)
            {
                ended = _installers.Where(kv => !alive.Contains(kv.Key)).Select(kv => kv.Value).ToList();
                foreach (var key in _installers.Keys.Where(k => !alive.Contains(k)).ToList()) _installers.Remove(key);
            }
            // Un setup.exe que se cierra no dice si salió bien: se informa como "terminó".
            foreach (var e in ended) Raise(e with { State = TaskState.Ended, UpdatedAt = now, Detail = "El instalador se cerró" });
        }
        catch (Exception ex) { _logger.LogDebug(ex, "No se pudieron revisar los procesos"); }
    }

    /// <summary>
    /// Windows Installer registra cada instalación (1033) y desinstalación (1034) en el registro de eventos
    /// "Application", con el nombre del producto y el código de resultado. Se leen las propiedades del evento
    /// (no el texto, que viene traducido según el idioma de Windows).
    /// </summary>
    private void StartMsiWatcher()
    {
        try
        {
            var query = new EventLogQuery("Application", PathType.LogName,
                "*[System[Provider[@Name='MsiInstaller'] and (EventID=1033 or EventID=1034)]]");
            _msiWatcher = new EventLogWatcher(query);
            _msiWatcher.EventRecordWritten += (_, e) =>
            {
                if (e.EventRecord is not { } r) return;
                using (r)
                {
                    try
                    {
                        var props = r.Properties;
                        var product = props.Count > 0 ? props[0].Value?.ToString() ?? "Programa" : "Programa";
                        var status = props.Count > 3 && int.TryParse(props[3].Value?.ToString(), out var s) ? s : -1;
                        var removal = r.Id == 1034;
                        var ok = status is 0 or 3010; // 3010 = OK, requiere reiniciar
                        var detail = ok
                            ? (removal ? "Desinstalado" : "Instalado") + (status == 3010 ? " · requiere reiniciar" : "")
                            : $"{(removal ? "La desinstalación" : "La instalación")} falló (código {status})";
                        var at = r.TimeCreated is { } t ? new DateTimeOffset(t) : DateTimeOffset.Now;
                        Raise(new BackgroundTask($"msi:{r.RecordId}", TaskKind.Install, product,
                            ok ? TaskState.Completed : TaskState.Failed, at, at, ok ? 1 : null, Detail: detail));
                    }
                    catch (Exception ex) { _logger.LogDebug(ex, "Evento de Windows Installer con formato inesperado"); }
                }
            };
            _msiWatcher.Enabled = true;
            _logger.LogInformation("Vigilando instalaciones de Windows Installer (registro de eventos)");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo leer el registro de eventos de Windows Installer");
            _msiWatcher = null;
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _downloads?.Dispose();
            _copyTimer?.Dispose();
            _processTimer?.Dispose();
            _msiWatcher?.Dispose();
        }
    }

    // ------------------------------------------------------------------ Win32

    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
}

/// <summary>Carpetas conocidas de Windows (respeta si el usuario movió Descargas a otro disco).</summary>
internal static class KnownFolders
{
    private static readonly Guid DownloadsId = new("374DE290-123F-4565-9164-39C4925E467B");

    public static string Downloads
    {
        get
        {
            if (SHGetKnownFolderPath(DownloadsId, 0, IntPtr.Zero, out var ptr) == 0)
            {
                try { return Marshal.PtrToStringUni(ptr)!; }
                finally { Marshal.FreeCoTaskMem(ptr); }
            }
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }
    }

    [DllImport("shell32.dll")]
    private static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid id, uint flags, IntPtr token, out IntPtr path);
}
