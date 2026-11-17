using Microsoft.Extensions.Logging;
using NexusIsland.Core.Tasks;

namespace NexusIsland.Services.Windows.Tasks;

/// <summary>
/// Vigila la carpeta Descargas. Mientras un navegador descarga, escribe un temporal (".crdownload", ".part");
/// al terminar lo renombra al nombre final (o lo borra y deja el final). El tamaño total NO queda anotado
/// en ningún lado, así que se informa lo descargado y la velocidad, sin porcentaje.
/// </summary>
internal sealed class DownloadWatcher : IDisposable
{
    private readonly string _folder;
    private readonly Action<BackgroundTask> _raise;
    private readonly ILogger _logger;
    private readonly FileSystemWatcher _watcher;
    private readonly Timer _poll;
    private readonly object _sync = new();

    /// <summary>Descargas en curso, por ruta del temporal actual.</summary>
    private readonly Dictionary<string, Active> _active = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="Id">Fijo durante toda la descarga, aunque el navegador renombre el temporal.</param>
    private sealed record Active(string Id, string FinalName, string Browser, DateTimeOffset Started,
        long LastSize, DateTimeOffset LastAt, double? Speed);

    public DownloadWatcher(string folder, Action<BackgroundTask> raise, ILogger logger)
    {
        _folder = folder;
        _raise = raise;
        _logger = logger;

        // Descargas que ya estaban en curso al abrir la isla
        foreach (var file in Directory.EnumerateFiles(folder).Where(DownloadFiles.IsTemp)) Track(file);

        _watcher = new FileSystemWatcher(folder)
        {
            IncludeSubdirectories = false,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.Size,
            InternalBufferSize = 32 * 1024,
        };
        _watcher.Created += (_, e) => { if (DownloadFiles.IsTemp(e.FullPath)) Track(e.FullPath); };
        _watcher.Renamed += (_, e) => OnRenamed(e.OldFullPath, e.FullPath);
        _watcher.Deleted += (_, e) => OnDeleted(e.FullPath);
        _watcher.Error += (_, e) => _logger.LogDebug(e.GetException(), "El vigilante de Descargas perdió eventos");
        _watcher.EnableRaisingEvents = true;

        _poll = new Timer(_ => Poll(), null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
    }

    private void Track(string tempPath)
    {
        if (!DownloadFiles.TryParseTemp(tempPath, out var finalName, out var browser)) return;
        var now = DateTimeOffset.Now;
        Active a;
        lock (_sync)
        {
            if (_active.ContainsKey(tempPath)) return;
            a = new Active("download:" + tempPath, finalName, browser, now, SizeOf(tempPath), now, null);
            _active[tempPath] = a;
        }
        _raise(Running(a));
    }

    private void OnRenamed(string oldPath, string newPath)
    {
        Active? prev;
        lock (_sync) _active.Remove(oldPath, out prev);

        if (DownloadFiles.TryParseTemp(newPath, out var name, out _))
        {
            if (prev is null) { Track(newPath); return; }
            // Chrome: "Unconfirmed 123.crdownload" → "archivo.zip.crdownload": es la misma descarga.
            var renamed = prev with { FinalName = name };
            lock (_sync) _active[newPath] = renamed;
            _raise(Running(renamed));
            return;
        }

        // Temporal → nombre final: descarga completa.
        if (prev is not null)
            _raise(Done(prev, Path.GetFileName(newPath), newPath, completed: true));
    }

    private void OnDeleted(string path)
    {
        Active? prev;
        lock (_sync) _active.Remove(path, out prev);
        if (prev is null) return;
        // Firefox borra el ".part" y deja el archivo final; si el final no está, se canceló.
        Thread.Sleep(300); // el navegador puede tardar un instante en dejar el final
        var finalPath = Path.Combine(_folder, prev.FinalName);
        var completed = prev.FinalName != DownloadFiles.UnnamedName && File.Exists(finalPath);
        _raise(Done(prev, prev.FinalName, completed ? finalPath : _folder, completed));
    }

    /// <summary>Cada segundo: tamaño y velocidad de las descargas activas.</summary>
    private void Poll()
    {
        List<string> paths;
        lock (_sync) paths = _active.Keys.ToList();
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue; // lo resuelven Renamed / Deleted
            var now = DateTimeOffset.Now;
            var size = SizeOf(path);
            Active updated;
            lock (_sync)
            {
                if (!_active.TryGetValue(path, out var a)) continue;
                var seconds = (now - a.LastAt).TotalSeconds;
                double? speed = seconds > 0 && size >= a.LastSize ? (size - a.LastSize) / seconds : a.Speed;
                // Promedio móvil: que el número no salte tanto de un segundo a otro
                if (a.Speed is { } s && speed is { } v) speed = s * 0.6 + v * 0.4;
                updated = a with { LastSize = size, LastAt = now, Speed = speed };
                _active[path] = updated;
            }
            _raise(Running(updated));
        }
    }

    private BackgroundTask Running(Active a) =>
        new(a.Id, TaskKind.Download, a.FinalName, TaskState.Running, a.Started, DateTimeOffset.Now,
            null, a.LastSize, a.Speed, a.Browser, _folder);

    private static BackgroundTask Done(Active a, string name, string location, bool completed) =>
        new(a.Id, TaskKind.Download, name, completed ? TaskState.Completed : TaskState.Cancelled, a.Started,
            DateTimeOffset.Now, completed ? 1 : null, completed ? SizeOf(location) : a.LastSize, null, a.Browser, location);

    private static long SizeOf(string path)
    {
        try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
        catch { return 0; }
    }

    public void Dispose()
    {
        _watcher.Dispose();
        _poll.Dispose();
    }
}
