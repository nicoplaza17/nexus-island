using NexusIsland.Core.Geometry;

namespace NexusIsland.Core.Tests;

public class PlacementCalculatorTests
{
    // Monitor 1920x1080 con barra de tareas abajo de 48 px.
    private static readonly PixelRect Work = new(0, 0, 1920, 1032);
    // Monitor secundario a la izquierda, con coordenadas negativas.
    private static readonly PixelRect LeftWork = new(-2560, 0, 0, 1392);

    [Fact]
    public void Docks_top_center()
    {
        Assert.Equal(new PixelPoint(730, 0), PlacementCalculator.DockTopCenter(Work, 460));
        Assert.Equal(new PixelPoint(-1510, 0), PlacementCalculator.DockTopCenter(LeftWork, 460));
    }

    [Fact]
    public void Clamp_keeps_window_inside_work_area()
    {
        var offRight = PixelRect.FromSize(1800, 1000, 460, 220);
        Assert.Equal(new PixelPoint(1460, 812), PlacementCalculator.ClampInto(offRight, Work));

        var offTopLeft = PixelRect.FromSize(-50, -30, 460, 220);
        Assert.Equal(new PixelPoint(0, 0), PlacementCalculator.ClampInto(offTopLeft, Work));
    }

    [Fact]
    public void Clamp_leaves_valid_position_untouched()
    {
        var inside = PixelRect.FromSize(300, 400, 460, 220);
        Assert.Equal(new PixelPoint(300, 400), PlacementCalculator.ClampInto(inside, Work));
    }

    [Fact]
    public void Snaps_only_when_close_to_top_center()
    {
        Assert.True(PlacementCalculator.ShouldSnapToTopCenter(PixelRect.FromSize(760, 40, 460, 220), Work, 70));
        Assert.False(PlacementCalculator.ShouldSnapToTopCenter(PixelRect.FromSize(900, 40, 460, 220), Work, 70));
        Assert.False(PlacementCalculator.ShouldSnapToTopCenter(PixelRect.FromSize(730, 300, 460, 220), Work, 70));
    }
}
