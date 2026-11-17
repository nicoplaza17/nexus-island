namespace NexusIsland.Core.Geometry;

/// <summary>
/// Cálculos de ubicación de la ventana de la isla (matemática pura, en píxeles físicos).
/// </summary>
public static class PlacementCalculator
{
    /// <summary>Posición para anclar la ventana arriba al centro del área de trabajo de un monitor.</summary>
    public static PixelPoint DockTopCenter(PixelRect workArea, int windowWidth) =>
        new(workArea.Left + (workArea.Width - windowWidth) / 2, workArea.Top);

    /// <summary>
    /// Ajusta la posición para que la ventana entera quede dentro del área de trabajo
    /// (así la isla expandida nunca queda cortada por un borde de pantalla).
    /// </summary>
    public static PixelPoint ClampInto(PixelRect window, PixelRect workArea)
    {
        var x = Math.Clamp(window.Left, workArea.Left, Math.Max(workArea.Left, workArea.Right - window.Width));
        var y = Math.Clamp(window.Top, workArea.Top, Math.Max(workArea.Top, workArea.Bottom - window.Height));
        return new PixelPoint(x, y);
    }

    /// <summary>
    /// True si, al soltar, la ventana quedó cerca de la posición anclada arriba al centro
    /// (a menos de <paramref name="snapDistance"/> píxeles en horizontal y en vertical).
    /// </summary>
    public static bool ShouldSnapToTopCenter(PixelRect window, PixelRect workArea, int snapDistance)
    {
        var docked = DockTopCenter(workArea, window.Width);
        return Math.Abs(window.Left - docked.X) <= snapDistance
            && Math.Abs(window.Top - docked.Y) <= snapDistance;
    }
}
