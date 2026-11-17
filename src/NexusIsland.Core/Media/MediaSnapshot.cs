namespace NexusIsland.Core.Media;

/// <summary>Estado de reproducción (equivale al de los controles multimedia de Windows).</summary>
public enum MediaPlaybackStatus
{
    Unknown,
    Closed,
    Opened,
    Changing,
    Stopped,
    Playing,
    Paused,
}

/// <summary>Qué acciones permite la app de origen en este momento.</summary>
[Flags]
public enum MediaCapabilities
{
    None = 0,
    PlayPause = 1,
    Next = 2,
    Previous = 4,
    Seek = 8,
}

/// <summary>Línea de tiempo tal como la informó la app (puede estar desactualizada: ver PlaybackPosition).</summary>
public readonly record struct MediaTimeline(TimeSpan Start, TimeSpan End, TimeSpan Position, DateTimeOffset LastUpdated)
{
    public TimeSpan Duration => End > Start ? End - Start : TimeSpan.Zero;
}

/// <summary>
/// Foto del estado multimedia actual. Inmutable: cada cambio genera una nueva.
/// </summary>
public sealed record MediaSnapshot
{
    /// <summary>Identificador técnico de la app (AppUserModelId), por ejemplo "Spotify.exe".</summary>
    public required string SourceAppId { get; init; }

    /// <summary>Nombre legible de la app, por ejemplo "Spotify".</summary>
    public required string SourceAppName { get; init; }

    public string Title { get; init; } = "";
    public string Artist { get; init; } = "";
    public string AlbumTitle { get; init; } = "";
    public MediaPlaybackStatus Status { get; init; }
    public MediaCapabilities Capabilities { get; init; }
    public double PlaybackRate { get; init; } = 1.0;
    public MediaTimeline? Timeline { get; init; }

    /// <summary>Carátula como bytes de imagen (PNG/JPEG), o null si la app no la publica.</summary>
    public byte[]? Thumbnail { get; init; }

    /// <summary>Cuándo se leyó esta información (para estimar la posición si la app no informa la hora).</summary>
    public DateTimeOffset ReceivedAt { get; init; }

    /// <summary>Identifica la pista: cambia cuando cambia la canción (sirve para detectar "nueva canción").</summary>
    public string TrackKey => $"{SourceAppId}\u001f{Title}\u001f{Artist}\u001f{AlbumTitle}";

    public bool IsPlaying => Status == MediaPlaybackStatus.Playing;
}
