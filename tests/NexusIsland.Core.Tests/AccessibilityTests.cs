using NexusIsland.Core.Island;

namespace NexusIsland.Core.Tests;

public class AccessibilityTests
{
    [Fact]
    public void Hold_ExpandsAndIgnoresMouse()
    {
        var sm = new IslandStateMachine();
        sm.Hold();
        Assert.Equal(IslandState.Expanded, sm.State);

        sm.PointerEntered();
        sm.PointerExited();
        Assert.Equal(IslandTimer.None, sm.PendingTimer); // no programa la contracción automática
        sm.Clicked();
        Assert.Equal(IslandState.Expanded, sm.State); // un clic tampoco la contrae
    }

    [Fact]
    public void Release_Collapses()
    {
        var sm = new IslandStateMachine();
        sm.Hold();
        sm.Release();
        Assert.Equal(IslandState.Collapsed, sm.State);
        Assert.False(sm.IsHeld);
    }

    [Fact]
    public void ForceCollapse_EndsHold()
    {
        var sm = new IslandStateMachine();
        sm.Hold();
        sm.ForceCollapse(); // por ejemplo, empezó un juego en pantalla completa
        Assert.False(sm.IsHeld);
        sm.Clicked();
        Assert.Equal(IslandState.Expanded, sm.State);
    }

    [Theory]
    [InlineData(1.0, 1.0, 1.0)]
    [InlineData(1.0, 1.25, 1.25)]
    [InlineData(1.15, 2.25, 1.725)] // el texto al 225 % se limita a 150 %
    [InlineData(0.85, 0.5, 0.85)]   // nunca achica
    [InlineData(1.0, double.NaN, 1.0)]
    public void EffectiveScale_CombinesSizeAndTextScale(double size, double text, double expected) =>
        Assert.Equal(expected, IslandScale.Effective(size, text), 3);
}
