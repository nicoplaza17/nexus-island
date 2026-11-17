using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Media;
using NexusIsland.Core.Settings;
using Windows.ApplicationModel;
using Windows.Media.Control;
using GsmtcManager = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionManager;
using GsmtcSession = Windows.Media.Control.GlobalSystemMediaTransportControlsSession;
using GsmtcStatus = Windows.Media.Control.GlobalSystemMediaTransportControlsSessionPlaybackStatus;

namespace NexusIsland.Services.Windows.Media;

/// <summary>
/// Información y control multimedia usando los controles multimedia globales de Windows
/// (Global System Media Transport Controls, GSMTC: Windows.Media.Control).
///
/// Limitaciones reales de la API:
///  - Solo aparecen apps que publican en los controles multimedia de Windows
///    (Spotify, navegadores, Apple Music, Reproductor multimedia...). VLC 3.x de escritorio no publica.
///  - Se usa la sesión que Windows considera "actual" (la misma que controlan las teclas multimedia),
///    salvo que esa app esté ignorada en la configuración: ahí se busca otra (MediaSessionSelector).
///  - Muchas apps informan la posición solo al pausar/adelantar: la posición se estima (PlaybackPosition).
///  - Carátula, siguiente/anterior y adelantar dependen de cada app.
///  - Los eventos llegan en hilos secundarios.
/// </summary>
public sealed class GsmtcMediaSessionService : IMediaSessionService
{
    private const int MaxThumbnailBytes = 4 * 1024 * 1024;
    private static readonly TimeSpan RefreshDebounce = TimeSpan.FromMilliseconds(120);

    private readonly ILogger<GsmtcMediaSessionService> _logger;
    private readonly object _sessionLock = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly Timer _debounceTimer;
    private readonly ConcurrentDictionary<string, string> _appNameCache = new(StringComparer.OrdinalIgnoreCase);
    private volatile HashSet<string> _ignored = new(StringComparer.OrdinalIgnoreCase);

    private GsmtcManager? _manager;
    private GsmtcSession? _session;
    private string? _thumbnailTrackKey;
    private byte[]? _thumbnail;
    private bool _disposed;

    public GsmtcMediaSessionService(ILogger<GsmtcMediaSessionService> logger, SettingsStore settings)
    {
        _logger = logger;
        _debounceTimer = new Timer(_ => _ = RefreshSafeAsync(), null, Timeout.Infinite, Timeout.Infinite);
        UpdateIgnored(settings.Current);
        settings.Changed += s =>
        {
            // Copia propia de la lista: los eventos de Windows llegan en otros hilos.
            if (UpdateIgnored(s))
            {
                AttachCurrentSession();
                ScheduleRefresh();
            }
        };
    }

    public IReadOnlyDictionary<string, string> SeenApps => _appNameCache;

    private bool UpdateIgnored(AppSettings settings)
    {
        var next = new HashSet<string>(settings.Media.IgnoredApps.Select(a => a.Id), StringComparer.OrdinalIgnoreCase);
        if (next.SetEquals(_ignored)) return false;
        _ignored = next;
        if (next.Count > 0) _logger.LogInformation("Apps multimedia ignoradas: {Apps}", string.Join(", ", next));
        return true;
    }

    public MediaSnapshot? Current { get; private set; }

    public event Action<MediaSnapshot?>? Changed;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            _manager = await GsmtcManager.RequestAsync().AsTask(cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Puede fallar en ediciones de Windows sin el componente multimedia (Windows N sin Media Feature Pack).
            _logger.LogError(ex, "No se pudo acceder a los controles multimedia de Windows (GSMTC)");
            return;
        }

        _manager.CurrentSessionChanged += (_, _) =>
        {
            AttachCurrentSession();
            ScheduleRefresh();
        };
        // Algunas apps abren/cierran sesiones sin que cambie la "actual"; además, si la actual está
        // ignorada, la elegida puede cambiar cuando aparece o desaparece otra.
        _manager.SessionsChanged += (_, _) =>
        {
            AttachCurrentSession();
            ScheduleRefresh();
        };

        AttachCurrentSession();
        await RefreshSafeAsync();
        _logger.LogInformation("Controles multimedia de Windows conectados ({Count} sesiones abiertas)",
            _manager.GetSessions().Count);
    }

    public Task<bool> TogglePlayPauseAsync() => RunCommandAsync("play/pausa", s => s.TryTogglePlayPauseAsync().AsTask());
    public Task<bool> NextAsync() => RunCommandAsync("siguiente", s => s.TrySkipNextAsync().AsTask());
    public Task<bool> PreviousAsync() => RunCommandAsync("anterior", s => s.TrySkipPreviousAsync().AsTask());

    public Task<bool> SeekAsync(TimeSpan position) =>
        RunCommandAsync($"ir a {position:m\\:ss}", s => s.TryChangePlaybackPositionAsync(position.Ticks).AsTask());

    private async Task<bool> RunCommandAsync(string name, Func<GsmtcSession, Task<bool>> command)
    {
        GsmtcSession? session;
        lock (_sessionLock) session = _session;
        if (session is null) return false;

        try
        {
            var ok = await command(session);
            if (!ok) _logger.LogInformation("La app rechazó el comando \"{Command}\"", name);
            ScheduleRefresh();
            return ok;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Falló el comando multimedia \"{Command}\"", name);
            return false;
        }
    }

    // ---------------------------------------------------------------- sesión y eventos

    private void AttachCurrentSession()
    {
        if (_manager is null) return;
        GsmtcSession? next = null;
        try
        {
            var sessions = _manager.GetSessions();
            var candidates = new List<SessionCandidate>(sessions.Count);
            foreach (var session in sessions)
            {
                var appId = session.SourceAppUserModelId ?? "";
                ResolveAppName(appId); // para que figure en la configuración (apps ignoradas)
                MediaPlaybackStatus status;
                try { status = MapStatus(session.GetPlaybackInfo().PlaybackStatus); }
                catch { status = MediaPlaybackStatus.Unknown; }
                candidates.Add(new SessionCandidate(appId, status));
            }

            var current = _manager.GetCurrentSession()?.SourceAppUserModelId;
            var ignored = _ignored;
            var index = MediaSessionSelector.Choose(current, candidates, ignored.Contains);
            next = index >= 0 ? sessions[index] : null;
            if (current is not null && ignored.Contains(current))
                _logger.LogDebug("La sesión actual ({App}) está ignorada; elegida: {Chosen}", current,
                    next?.SourceAppUserModelId ?? "(ninguna)");
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudieron leer las sesiones multimedia");
        }

        lock (_sessionLock)
        {
            // Misma app que antes: se conserva la suscripción (evita perder avisos al re-suscribir).
            if (_session is not null && next is not null
                && string.Equals(_session.SourceAppUserModelId, next.SourceAppUserModelId, StringComparison.OrdinalIgnoreCase))
                return;

            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnSessionChanged;
                _session.PlaybackInfoChanged -= OnSessionChanged;
                _session.TimelinePropertiesChanged -= OnSessionChanged;
            }
            _session = next;
            if (_session is not null)
            {
                _session.MediaPropertiesChanged += OnSessionChanged;
                _session.PlaybackInfoChanged += OnSessionChanged;
                _session.TimelinePropertiesChanged += OnSessionChanged;
            }
        }
        _logger.LogDebug("Sesión elegida: {App}", next?.SourceAppUserModelId ?? "(ninguna)");
    }

    private void OnSessionChanged(GsmtcSession sender, object args) => ScheduleRefresh();

    /// <summary>
    /// Las apps suelen mandar varios avisos seguidos (título, después carátula, después estado):
    /// se agrupan y se lee todo una sola vez.
    /// </summary>
    private void ScheduleRefresh()
    {
        if (!_disposed) _debounceTimer.Change(RefreshDebounce, Timeout.InfiniteTimeSpan);
    }

    private async Task RefreshSafeAsync()
    {
        if (_disposed) return;
        await _refreshGate.WaitAsync();
        try
        {
            var snapshot = await ReadSnapshotAsync();
            Current = snapshot;
            Changed?.Invoke(snapshot);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error leyendo el estado multimedia");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<MediaSnapshot?> ReadSnapshotAsync()
    {
        GsmtcSession? session;
        lock (_sessionLock) session = _session;
        if (session is null) return null;

        GlobalSystemMediaTransportControlsSessionMediaProperties properties;
        GlobalSystemMediaTransportControlsSessionPlaybackInfo playback;
        GlobalSystemMediaTransportControlsSessionTimelineProperties timeline;
        string appId;
        try
        {
            appId = session.SourceAppUserModelId ?? "";
            properties = await session.TryGetMediaPropertiesAsync().AsTask();
            playback = session.GetPlaybackInfo();
            timeline = session.GetTimelineProperties();
        }
        catch (Exception ex)
        {
            // La app puede haberse cerrado justo mientras leíamos.
            _logger.LogDebug(ex, "La sesión multimedia dejó de responder");
            return null;
        }

        var status = MapStatus(playback.PlaybackStatus);
        var title = properties.Title ?? "";
        var artist = string.IsNullOrWhiteSpace(properties.Artist) ? properties.AlbumArtist ?? "" : properties.Artist;
        if (status == MediaPlaybackStatus.Closed || (title.Length == 0 && artist.Length == 0))
            return null;

        var snapshot = new MediaSnapshot
        {
            SourceAppId = appId,
            SourceAppName = ResolveAppName(appId),
            Title = title,
            Artist = artist,
            AlbumTitle = properties.AlbumTitle ?? "",
            Status = status,
            Capabilities = MapCapabilities(playback.Controls),
            PlaybackRate = playback.PlaybackRate ?? 1.0,
            Timeline = timeline.EndTime > timeline.StartTime
                ? new MediaTimeline(timeline.StartTime, timeline.EndTime, timeline.Position, timeline.LastUpdatedTime)
                : null,
            ReceivedAt = DateTimeOffset.Now,
        };

        return snapshot with { Thumbnail = await GetThumbnailAsync(snapshot.TrackKey, properties) };
    }

    /// <summary>Lee la carátula solo cuando cambia la canción (o si todavía no había llegado).</summary>
    private async Task<byte[]?> GetThumbnailAsync(string trackKey, GlobalSystemMediaTransportControlsSessionMediaProperties properties)
    {
        if (trackKey == _thumbnailTrackKey && _thumbnail is not null) return _thumbnail;
        if (trackKey != _thumbnailTrackKey)
        {
            _thumbnailTrackKey = trackKey;
            _thumbnail = null;
        }
        if (properties.Thumbnail is null) return null;

        try
        {
            using var winrtStream = await properties.Thumbnail.OpenReadAsync().AsTask();
            if (winrtStream.Size == 0 || winrtStream.Size > MaxThumbnailBytes) return null;
            using var stream = winrtStream.AsStreamForRead();
            using var memory = new MemoryStream((int)winrtStream.Size);
            await stream.CopyToAsync(memory);
            _thumbnail = memory.ToArray();
            _logger.LogDebug("Carátula leída: {Bytes} bytes ({Type})", _thumbnail.Length, winrtStream.ContentType);
            return _thumbnail;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo leer la carátula");
            return null;
        }
    }

    private string ResolveAppName(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return "Multimedia";
        if (_appNameCache.TryGetValue(appId, out var cached)) return cached;

        if (!SourceAppNames.TryGetKnown(appId, out var name))
        {
            name = SourceAppNames.Prettify(appId);
            // Apps empaquetadas (Microsoft Store): Windows sabe su nombre real.
            if (appId.Contains('!'))
            {
                try
                {
                    var display = AppInfo.GetFromAppUserModelId(appId)?.DisplayInfo?.DisplayName;
                    if (!string.IsNullOrWhiteSpace(display)) name = display;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "No se pudo resolver el nombre de {AppId}", appId);
                }
            }
        }

        _appNameCache[appId] = name;
        _logger.LogInformation("App multimedia {AppId} → \"{Name}\"", appId, name);
        return name;
    }

    private static MediaPlaybackStatus MapStatus(GsmtcStatus status) => status switch
    {
        GsmtcStatus.Closed => MediaPlaybackStatus.Closed,
        GsmtcStatus.Opened => MediaPlaybackStatus.Opened,
        GsmtcStatus.Changing => MediaPlaybackStatus.Changing,
        GsmtcStatus.Stopped => MediaPlaybackStatus.Stopped,
        GsmtcStatus.Playing => MediaPlaybackStatus.Playing,
        GsmtcStatus.Paused => MediaPlaybackStatus.Paused,
        _ => MediaPlaybackStatus.Unknown,
    };

    private static MediaCapabilities MapCapabilities(GlobalSystemMediaTransportControlsSessionPlaybackControls c)
    {
        var caps = MediaCapabilities.None;
        if (c.IsPlayPauseToggleEnabled || c.IsPlayEnabled || c.IsPauseEnabled) caps |= MediaCapabilities.PlayPause;
        if (c.IsNextEnabled) caps |= MediaCapabilities.Next;
        if (c.IsPreviousEnabled) caps |= MediaCapabilities.Previous;
        if (c.IsPlaybackPositionEnabled) caps |= MediaCapabilities.Seek;
        return caps;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _debounceTimer.Dispose();
        lock (_sessionLock)
        {
            if (_session is not null)
            {
                _session.MediaPropertiesChanged -= OnSessionChanged;
                _session.PlaybackInfoChanged -= OnSessionChanged;
                _session.TimelinePropertiesChanged -= OnSessionChanged;
                _session = null;
            }
        }
    }
}
