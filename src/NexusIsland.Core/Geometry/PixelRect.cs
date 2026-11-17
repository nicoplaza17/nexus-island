namespace NexusIsland.Core.Geometry;

/// <summary>Rectángulo en píxeles físicos de pantalla (como los usa Win32).</summary>
public readonly record struct PixelRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public int CenterX => Left + Width / 2;

    public static PixelRect FromSize(int left, int top, int width, int height) =>
        new(left, top, left + width, top + height);

    /// <summary>True si <paramref name="other"/> queda completamente adentro (o es igual).</summary>
    public bool Contains(PixelRect other) =>
        other.Left >= Left && other.Top >= Top && other.Right <= Right && other.Bottom <= Bottom;
}

/// <summary>Punto en píxeles físicos.</summary>
public readonly record struct PixelPoint(int X, int Y);
