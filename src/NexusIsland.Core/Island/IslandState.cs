namespace NexusIsland.Core.Island;

/// <summary>Estados visuales de la isla.</summary>
public enum IslandState
{
    /// <summary>Cápsula pequeña y discreta (estado de reposo).</summary>
    Collapsed,

    /// <summary>"Vistazo": un poco más grande al pasar el mouse.</summary>
    Peek,

    /// <summary>Expandida: información detallada y controles.</summary>
    Expanded,
}

/// <summary>Temporizadores que la máquina de estados le pide a la interfaz.</summary>
public enum IslandTimer
{
    None,

    /// <summary>Espera breve antes del vistazo, para que pasar el mouse "de paso" no dispare nada.</summary>
    HoverIntent,

    /// <summary>Margen al salir del vistazo antes de contraerse (evita parpadeos en el borde).</summary>
    LeaveGrace,

    /// <summary>Contracción automática de la isla expandida cuando el mouse se fue.</summary>
    AutoCollapse,

    /// <summary>Fin de una notificación temporal (por ejemplo "nueva canción").</summary>
    TransientEnd,
}

/// <summary>Tiempos del comportamiento. Valores por defecto ajustados a mano; configurables en la Etapa 3.</summary>
public sealed record IslandTiming
{
    public TimeSpan HoverIntent { get; init; } = TimeSpan.FromMilliseconds(140);
    public TimeSpan LeaveGrace { get; init; } = TimeSpan.FromMilliseconds(260);
    public TimeSpan AutoCollapse { get; init; } = TimeSpan.FromMilliseconds(2200);
}
