using NexusIsland.Core.Media;

namespace NexusIsland.Core.Tests;

public class LevelBarsTests
{
    [Fact]
    public void Silence_KeepsBarsDown()
    {
        var bars = new LevelBars();
        for (var i = 0; i < 10; i++) bars.Update(0, 0);
        Assert.All(bars.Levels, l => Assert.Equal(0, l));
    }

    [Fact]
    public void Sound_RaisesBars_AndTheyFallSoftlyAfter()
    {
        var bars = new LevelBars();
        bars.Update(0.5f, 0.5f);
        Assert.All(bars.Levels, l => Assert.True(l > 0.3)); // sube enseguida (raíz de 0,5 ≈ 0,71)
        var peak = bars.Levels.ToArray();
        bars.Update(0, 0);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(bars.Levels[i] < peak[i]);       // baja…
            Assert.True(bars.Levels[i] > peak[i] * 0.4); // …pero suave, no de golpe
        }
        for (var i = 0; i < 60; i++) bars.Update(0, 0);
        Assert.All(bars.Levels, l => Assert.Equal(0, l)); // y termina en cero (sin redibujar para siempre)
    }

    [Fact]
    public void Bars_MoveDifferently()
    {
        var bars = new LevelBars();
        bars.Update(0.8f, 0.2f); // más a la izquierda
        Assert.True(bars.Levels[0] > bars.Levels[2]);
        Assert.NotEqual(bars.Levels[0], bars.Levels[1]); // la del medio tiene otra velocidad
    }

    [Theory]
    [InlineData(0, 3)]
    [InlineData(1, 15)]
    [InlineData(0.5, 9)]
    [InlineData(0.52, 9)]   // 9,24 → 9
    [InlineData(0.55, 9.5)] // 9,6 → medio píxel
    [InlineData(2, 15)]     // fuera de rango
    public void ToPixels_RoundsToHalfPixel(double level, double expected) =>
        Assert.Equal(expected, LevelBars.ToPixels(level, 3, 15));
}
