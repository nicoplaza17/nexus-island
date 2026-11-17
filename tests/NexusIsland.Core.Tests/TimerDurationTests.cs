using NexusIsland.Core.Tools;

namespace NexusIsland.Core.Tests;

public class TimerDurationTests
{
    [Theory]
    [InlineData(1, 2)]
    [InlineData(9, 10)]
    [InlineData(10, 15)]
    [InlineData(12, 15)]  // redondea al múltiplo siguiente
    [InlineData(55, 60)]
    [InlineData(60, 75)]
    [InlineData(170, 180)]
    [InlineData(180, 180)] // tope
    public void More(int from, int expected) => Assert.Equal(expected, TimerDuration.Step(from, +1));

    [Theory]
    [InlineData(1, 1)]     // piso
    [InlineData(5, 4)]
    [InlineData(10, 9)]
    [InlineData(12, 10)]
    [InlineData(15, 10)]
    [InlineData(60, 55)]
    [InlineData(65, 60)]
    [InlineData(75, 60)]
    public void Less(int from, int expected) => Assert.Equal(expected, TimerDuration.Step(from, -1));

    [Fact]
    public void GoingUpAndDown_IsSymmetric_OnTheGrid()
    {
        // Recorre todos los valores que se alcanzan con "+" desde 1: subir y bajar vuelve al mismo número.
        var visited = new List<int>();
        for (var m = TimerDuration.Min; m < TimerDuration.Max; m = TimerDuration.Step(m, +1)) visited.Add(m);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 15, 20, 25, 30, 35, 40, 45, 50, 55, 60, 75, 90, 105, 120, 135, 150, 165], visited);
        foreach (var m in visited) Assert.Equal(m, TimerDuration.Step(TimerDuration.Step(m, +1), -1));
    }

    [Fact]
    public void Presets_AreValid() =>
        Assert.All(TimerDuration.Presets, p => Assert.Equal(p, TimerDuration.Clamp(p)));
}
