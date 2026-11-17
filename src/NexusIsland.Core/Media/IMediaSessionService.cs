namespace NexusIsland.Core.Media;

/// <summary>
/// Fuente de información y control multimedia del sistema.
/// La implementación real usa los controles multimedia de Windows (GSMTC) en Services.Windows.
/// </summary>
public interface IMediaSessionService : IDisposable
{
    /// <summary>Estado actual, o null si no hay ninguna app reproduciendo/publicando.</summary>
    MediaSnapshot? Current { get; }

    /// <summary>
    /// Se dispara cuando cambia el estado (o null si ya no hay sesión).
    /// IMPORTANTE: puede llegar desde un hilo secundario.
    /// </summary>
    event Action<MediaSnapshot?>? Changed;

    /// <summary>Apps multimedia vistas desde que arrancó la isla (id → nombre), para la configuración.</summary>
    IReadOnlyDictionary<string, string> SeenApps { get; }

    Task InitializeAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Vuelve a conectarse con Windows desde cero (por ejemplo al volver de suspensión,
    /// cuando los avisos multimedia de Windows a veces dejan de llegar).
    /// </summary>
    Task ReconnectAsync();

    Task<bool> TogglePlayPauseAsync();
    Task<bool> NextAsync();
    Task<bool> PreviousAsync();

    /// <summary>Salta a una posición. Solo funciona si la app lo permite (MediaCapabilities.Seek).</summary>
    Task<bool> SeekAsync(TimeSpan position);
}
