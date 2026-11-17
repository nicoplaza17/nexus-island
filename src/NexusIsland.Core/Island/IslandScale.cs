namespace NexusIsland.Core.Island;

/// <summary>Escala final de la isla: tamaño elegido × "Tamaño del texto" de Windows.</summary>
public static class IslandScale
{
    /// <summary>Hasta 150 %: más grande la isla taparía demasiado la pantalla.</summary>
    public const double MaxTextScale = 1.5;

    public static double Effective(double sizeScale, double windowsTextScale) =>
        sizeScale * Math.Clamp(double.IsFinite(windowsTextScale) ? windowsTextScale : 1.0, 1.0, MaxTextScale);
}
