using NexusIsland.Core.Island;

namespace NexusIsland.Core.Activities;

/// <summary>
/// Algo que un módulo quiere mostrar en la isla (una canción sonando, un temporizador, una descarga...).
/// </summary>
/// <param name="Key">Identificador único de la actividad (por ejemplo "media.session").</param>
/// <param name="ModuleId">Módulo dueño. Define qué plantillas visuales se usan: "{ModuleId}.Compact", ".Peek", ".Expanded".</param>
/// <param name="Priority">Mayor = más importante. Si hay varias, se muestra la de mayor prioridad.</param>
/// <param name="ViewModel">Datos que muestran las plantillas.</param>
public sealed record IslandActivity(string Key, string ModuleId, int Priority, object ViewModel);

/// <summary>Prioridades de referencia para que los módulos sean consistentes entre sí.</summary>
public static class ActivityPriority
{
    public const int Idle = 0;        // reloj (siempre presente, último recurso)
    public const int Ambient = 50;    // multimedia
    public const int Active = 100;    // temporizador corriendo, descarga en curso
    public const int Urgent = 200;    // alertas que requieren atención
}

/// <summary>Lo que un módulo puede hacer con la isla. Llamar siempre desde el hilo de la interfaz.</summary>
public interface IActivityHost
{
    /// <summary>Agrega o reemplaza una actividad.</summary>
    void Publish(IslandActivity activity);

    void Remove(string key);

    /// <summary>
    /// Pide mostrarse brevemente (notificación temporal), por ejemplo al cambiar de canción.
    /// Solo tiene efecto si la actividad es la que se está mostrando.
    /// </summary>
    void RequestAttention(string key, TimeSpan duration);
}

/// <summary>
/// Opcional para ViewModels: recibir el estado de la isla para trabajar solo cuando hace falta
/// (por ejemplo, actualizar una barra de progreso solo si la isla está expandida).
/// </summary>
public interface IIslandAware
{
    /// <param name="isShown">False si la actividad dejó de ser la visible.</param>
    void OnIslandStateChanged(IslandState state, bool isShown);
}

/// <summary>Contrato de un módulo de la isla.</summary>
public interface IIslandModule : IDisposable
{
    string Id { get; }

    string DisplayName { get; }

    /// <summary>
    /// Diccionario de recursos (WPF) con las plantillas "{Id}.Compact", "{Id}.Peek" y "{Id}.Expanded".
    /// Es un Uri para que el Core no dependa de WPF.
    /// </summary>
    Uri? TemplatesUri { get; }

    /// <summary>Se llama una vez, en el hilo de la interfaz, después de crear la isla.</summary>
    Task StartAsync(IActivityHost host, CancellationToken cancellationToken);
}
