namespace NexusIsland.Core.Notifications;

/// <summary>Una notificación de otra app, ya leída del centro de notificaciones de Windows.</summary>
/// <param name="Id">Identificador que asigna Windows (único mientras la notificación exista).</param>
/// <param name="AppId">Identificador técnico de la app de origen (AppUserModelId).</param>
public sealed record IslandNotification(uint Id, string AppId, string AppName, string Title, string Body, DateTimeOffset At);

/// <summary>Estado del acceso a las notificaciones de Windows.</summary>
public enum NotificationAccess
{
    /// <summary>La app no tiene identidad de paquete: Windows no deja usar la API.</summary>
    NoPackageIdentity,
    /// <summary>Todavía no se le preguntó al usuario.</summary>
    Unspecified,
    Allowed,
    /// <summary>El usuario lo negó (se cambia en Configuración de Windows → Privacidad → Notificaciones).</summary>
    Denied,
    /// <summary>Otro error (API no disponible, etc.).</summary>
    Unavailable,
}

/// <summary>Fuente de notificaciones (implementación real: UserNotificationListener en Services.Windows).</summary>
public interface INotificationSource : IDisposable
{
    NotificationAccess Access { get; }

    /// <summary>Pide permiso al usuario (Windows muestra su propio diálogo). Llamar desde el hilo de la interfaz.</summary>
    Task<NotificationAccess> RequestAccessAsync();

    /// <summary>Empieza a escuchar. Las que ya estaban en el centro de notificaciones no se informan como nuevas.</summary>
    Task StartAsync();

    /// <summary>Llegó una notificación nueva. Puede llegar desde un hilo secundario.</summary>
    event Action<IslandNotification>? Arrived;

    /// <summary>Abre la app de origen (no se pueden tocar los botones de la notificación: Windows no lo permite).</summary>
    bool TryOpenApp(string appId);
}

/// <summary>
/// Detecta cuáles notificaciones son nuevas comparando con las ya vistas (Windows informa la lista completa).
/// Lógica pura: testeable.
/// </summary>
public sealed class NotificationTracker
{
    private readonly HashSet<uint> _seen = [];

    /// <summary>Marca todas como vistas sin informarlas (al arrancar: no "explotar" con las viejas).</summary>
    public void Seed(IEnumerable<uint> ids)
    {
        _seen.Clear();
        _seen.UnionWith(ids);
    }

    /// <summary>Devuelve los ids nuevos y olvida los que ya no están (para que la memoria no crezca).</summary>
    public IReadOnlyList<uint> Update(IReadOnlyCollection<uint> current)
    {
        var fresh = current.Where(id => !_seen.Contains(id)).ToList();
        _seen.IntersectWith(current);
        _seen.UnionWith(fresh);
        return fresh;
    }
}

/// <summary>Historial reciente (solo en memoria; nunca se escribe en disco ni en el log).</summary>
public sealed class NotificationHistory(int capacity = 20)
{
    private readonly List<IslandNotification> _items = [];

    public IReadOnlyList<IslandNotification> Items => _items;

    public void Add(IslandNotification n)
    {
        _items.RemoveAll(x => x.Id == n.Id);
        _items.Insert(0, n);
        if (_items.Count > capacity) _items.RemoveRange(capacity, _items.Count - capacity);
    }

    public void Clear() => _items.Clear();
}
