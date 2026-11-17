using NexusIsland.Core.Island;

namespace NexusIsland.App.Island;

/// <summary>Tamaño y redondeo de la isla en cada estado (unidades WPF, se escalan solas con el DPI).</summary>
public readonly record struct IslandShapeSpec(double Width, double Height, double CornerRadius);

public static class IslandLayout
{
    public static readonly IslandShapeSpec Collapsed = new(150, 34, 17);
    public static readonly IslandShapeSpec Peek = new(250, 42, 21);
    public static readonly IslandShapeSpec Expanded = new(400, 184, 34);

    public static IslandShapeSpec For(IslandState state) => state switch
    {
        IslandState.Peek => Peek,
        IslandState.Expanded => Expanded,
        _ => Collapsed,
    };
}
