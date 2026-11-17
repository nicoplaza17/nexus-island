using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexusIsland.Core;
using NexusIsland.Core.Translation;

namespace NexusIsland.Services.Windows.Translation;

/// <summary>
/// Traducción local. El modelo corre en otro proceso ("NexusIsland.exe --translator"): carga ~260 MB
/// mientras se usa y se cierra solo tras <see cref="IdleTimeout"/> sin pedidos, así la isla vuelve a su
/// consumo normal. El texto viaja por una tubería entre los dos procesos; nunca va al disco, al log ni a Internet.
/// </summary>
public sealed class LocalTranslationService : ITranslationService, IDisposable
{
    public static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(90);

    private readonly ILogger<LocalTranslationService> _logger;
    private readonly ModelStore _store;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly Timer _idleTimer;
    private Process? _process;
    private int _nextId;

    public LocalTranslationService(ILogger<LocalTranslationService> logger)
    {
        _logger = logger;
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"NexusIsland/{typeof(LocalTranslationService).Assembly.GetName().Version?.ToString(3)}");
        _store = new ModelStore(Path.Combine(AppPaths.LocalData, "models"), http);
        _idleTimer = new Timer(_ => StopProcess("inactividad"));
    }

    public bool IsModelInstalled(TranslationDirection direction) => _store.IsInstalled(direction);

    public long DownloadSize(TranslationDirection direction) => TranslationModels.For(direction).TotalBytes;

    public long InstalledBytes() => _store.InstalledBytes();

    public async Task DownloadModelAsync(TranslationDirection direction, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Descargando el modelo de traducción {Direction} ({Mb:F0} MB)", direction.Code(), DownloadSize(direction) / 1048576.0);
        var sw = Stopwatch.StartNew();
        try
        {
            await _store.DownloadAsync(direction, progress, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Modelo {Direction} bajado y verificado en {Seconds:F0} s", direction.Code(), sw.Elapsed.TotalSeconds);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Descarga del modelo {Direction} cancelada", direction.Code());
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falló la descarga del modelo {Direction}", direction.Code());
            throw;
        }
    }

    public void DeleteModels()
    {
        StopProcess("borrado de modelos");
        _store.DeleteAll();
        _logger.LogInformation("Modelos de traducción borrados");
    }

    public async Task<string> TranslateAsync(string text, TranslationDirection direction, CancellationToken cancellationToken)
    {
        if (!_store.IsInstalled(direction)) throw new InvalidOperationException("El modelo no está bajado");
        var lines = TranslationText.Split(text);
        var sentences = lines.SelectMany(l => l).ToArray();
        if (sentences.Length == 0) return "";

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            _idleTimer.Change(Timeout.Infinite, Timeout.Infinite);
            var process = EnsureProcess();
            var request = new TranslatorRequest(++_nextId, _store.DirectoryOf(direction), sentences);
            var sw = Stopwatch.StartNew();
            await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(RequestTimeout);
            string? line;
            try
            {
                line = await process.StandardOutput.ReadLineAsync(timeout.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                StopProcess("sin respuesta"); // el estado del proceso es incierto: se empieza de cero la próxima vez
                throw;
            }
            if (line is null)
            {
                StopProcess("terminó inesperadamente");
                throw new InvalidOperationException("El traductor se cerró");
            }

            var response = JsonSerializer.Deserialize<TranslatorResponse>(line);
            if (response?.Translations is not { } translated || response.Id != request.Id)
                throw new InvalidOperationException("El traductor falló: " + (response?.Error ?? "respuesta inválida"));

            _logger.LogInformation("Traducción {Direction}: {Count} partes, {Chars} caracteres, {Ms} ms",
                direction.Code(), sentences.Length, text.Length, sw.ElapsedMilliseconds); // nunca el texto

            var i = 0;
            return TranslationText.Join(lines.Select(l => l.Select(_ => translated[i++]).ToList()));
        }
        finally
        {
            _idleTimer.Change(IdleTimeout, Timeout.InfiniteTimeSpan);
            _gate.Release();
        }
    }

    private Process EnsureProcess()
    {
        if (_process is { HasExited: false }) return _process;
        _process?.Dispose();
        var info = new ProcessStartInfo(Environment.ProcessPath ?? throw new InvalidOperationException("Sin ruta del ejecutable"), "--translator")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
        };
        _process = Process.Start(info) ?? throw new InvalidOperationException("No se pudo iniciar el traductor");
        try { _process.PriorityClass = ProcessPriorityClass.BelowNormal; } catch (Exception) { /* no es grave */ }
        _logger.LogInformation("Proceso traductor iniciado (PID {Pid})", _process.Id);
        return _process;
    }

    private void StopProcess(string reason)
    {
        var p = _process;
        if (p is null) return;
        _process = null;
        try
        {
            if (!p.HasExited)
            {
                p.StandardInput.Close(); // el traductor termina solo al ver la entrada cerrada
                if (!p.WaitForExit(3000)) p.Kill();
            }
            _logger.LogInformation("Proceso traductor cerrado ({Reason})", reason);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Al cerrar el traductor");
        }
        finally
        {
            p.Dispose();
        }
    }

    public void Dispose()
    {
        _idleTimer.Dispose();
        StopProcess("cierre de la isla");
    }
}
