using NexusIsland.Core.Animation;

namespace NexusIsland.Core.Tests;

public class SpringTests
{
    private static int RunUntilRest(Spring spring, double frame = 1.0 / 60, int maxFrames = 600)
    {
        var frames = 0;
        while (spring.Step(frame) && frames < maxFrames) frames++;
        return frames;
    }

    [Fact]
    public void Starts_at_rest()
    {
        var s = new Spring(10);
        Assert.True(s.IsAtRest);
        Assert.False(s.Step(1.0 / 60));
    }

    [Fact]
    public void Reaches_target_exactly_and_stops()
    {
        var s = new Spring(34) { Target = 170 };
        var frames = RunUntilRest(s);
        Assert.Equal(170, s.Value);
        Assert.True(s.IsAtRest);
        Assert.InRange(frames, 10, 90); // entre ~0,15 s y 1,5 s a 60 fps
    }

    [Fact]
    public void Underdamped_spring_overshoots_a_little()
    {
        var s = new Spring(0, stiffness: 420, dampingRatio: 0.74) { Target = 100 };
        double max = 0;
        while (s.Step(1.0 / 60)) max = Math.Max(max, s.Value);
        Assert.InRange(max, 100.5, 115); // rebote visible pero contenido
    }

    [Fact]
    public void Critically_damped_spring_never_overshoots()
    {
        var s = new Spring(0, stiffness: 900, dampingRatio: 1.0, restThreshold: 0.002) { Target = 1 };
        double max = 0;
        while (s.Step(1.0 / 60)) max = Math.Max(max, s.Value);
        Assert.True(max <= 1.0 + 1e-9);
    }

    [Fact]
    public void Retargeting_midway_keeps_value_continuous()
    {
        var s = new Spring(0) { Target = 100 };
        for (var i = 0; i < 5; i++) s.Step(1.0 / 60);
        var before = s.Value;
        var velocity = s.Velocity;
        s.Target = 0;
        s.Step(1.0 / 240);
        var delta = s.Value - before;
        // Sin saltos: en un sub-paso se mueve lo que indica su velocidad (no "teletransporta" al nuevo destino)
        // y conserva el impulso: todavía sigue un poco hacia el destino anterior.
        Assert.True(delta > 0, "Debe conservar el impulso hacia el destino anterior");
        Assert.True(delta <= velocity / 240 + 0.5, $"Salto inesperado: {delta:F2} px");
    }

    [Fact]
    public void A_long_frame_does_not_explode()
    {
        var s = new Spring(0) { Target = 100 };
        s.Step(5.0); // la app estuvo trabada 5 s
        Assert.InRange(s.Value, -10, 130);
    }

    [Fact]
    public void SnapTo_stops_immediately()
    {
        var s = new Spring(0) { Target = 100 };
        s.Step(1.0 / 60);
        s.SnapTo(42);
        Assert.Equal(42, s.Value);
        Assert.True(s.IsAtRest);
    }
}
