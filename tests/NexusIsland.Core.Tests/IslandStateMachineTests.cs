using NexusIsland.Core.Island;

namespace NexusIsland.Core.Tests;

public class IslandStateMachineTests
{
    private static (IslandStateMachine Sm, List<IslandTimer> Timers) Create()
    {
        var sm = new IslandStateMachine();
        var timers = new List<IslandTimer>();
        sm.TimerRequested += (t, _) => timers.Add(t);
        return (sm, timers);
    }

    [Fact]
    public void Starts_collapsed_with_no_timer()
    {
        var (sm, _) = Create();
        Assert.Equal(IslandState.Collapsed, sm.State);
        Assert.Equal(IslandTimer.None, sm.PendingTimer);
    }

    [Fact]
    public void Hover_requires_intent_delay_before_peek()
    {
        var (sm, timers) = Create();
        sm.PointerEntered();
        Assert.Equal(IslandState.Collapsed, sm.State);
        Assert.Equal([IslandTimer.HoverIntent], timers);

        sm.TimerElapsed(IslandTimer.HoverIntent);
        Assert.Equal(IslandState.Peek, sm.State);
    }

    [Fact]
    public void Passing_through_quickly_does_not_peek()
    {
        var (sm, _) = Create();
        sm.PointerEntered();
        sm.PointerExited();
        sm.TimerElapsed(IslandTimer.HoverIntent); // llega tarde: ya fue cancelado
        Assert.Equal(IslandState.Collapsed, sm.State);
    }

    [Fact]
    public void Leaving_peek_collapses_after_grace_and_reentering_cancels_it()
    {
        var (sm, _) = Create();
        sm.PointerEntered();
        sm.TimerElapsed(IslandTimer.HoverIntent);

        sm.PointerExited();
        Assert.Equal(IslandTimer.LeaveGrace, sm.PendingTimer);
        sm.PointerEntered();
        Assert.Equal(IslandTimer.None, sm.PendingTimer);
        sm.TimerElapsed(IslandTimer.LeaveGrace);
        Assert.Equal(IslandState.Peek, sm.State);

        sm.PointerExited();
        sm.TimerElapsed(IslandTimer.LeaveGrace);
        Assert.Equal(IslandState.Collapsed, sm.State);
    }

    [Fact]
    public void Click_expands_and_click_again_returns_to_peek_when_pointer_inside()
    {
        var (sm, _) = Create();
        sm.PointerEntered();
        sm.Clicked();
        Assert.Equal(IslandState.Expanded, sm.State);
        sm.Clicked();
        Assert.Equal(IslandState.Peek, sm.State);
    }

    [Fact]
    public void Expanded_auto_collapses_only_after_pointer_leaves()
    {
        var (sm, _) = Create();
        sm.PointerEntered();
        sm.Clicked();
        Assert.Equal(IslandTimer.None, sm.PendingTimer);

        sm.PointerExited();
        Assert.Equal(IslandTimer.AutoCollapse, sm.PendingTimer);
        sm.TimerElapsed(IslandTimer.AutoCollapse);
        Assert.Equal(IslandState.Collapsed, sm.State);
    }

    [Fact]
    public void Returning_to_expanded_island_cancels_auto_collapse()
    {
        var (sm, _) = Create();
        sm.PointerEntered();
        sm.Clicked();
        sm.PointerExited();
        sm.PointerEntered();
        sm.TimerElapsed(IslandTimer.AutoCollapse);
        Assert.Equal(IslandState.Expanded, sm.State);
    }

    [Fact]
    public void Click_without_prior_enter_still_auto_collapses_after_leaving()
    {
        var (sm, _) = Create();
        sm.Clicked(); // p. ej. toque en pantalla táctil, sin aviso previo de "entró"
        Assert.Equal(IslandState.Expanded, sm.State);
        sm.PointerExited();
        Assert.Equal(IslandTimer.AutoCollapse, sm.PendingTimer);
    }

    [Fact]
    public void Transient_shows_peek_then_collapses()
    {
        var (sm, timers) = Create();
        sm.ShowTransient(TimeSpan.FromSeconds(3));
        Assert.Equal(IslandState.Peek, sm.State);
        Assert.Equal(IslandTimer.TransientEnd, sm.PendingTimer);
        sm.TimerElapsed(IslandTimer.TransientEnd);
        Assert.Equal(IslandState.Collapsed, sm.State);
    }

    [Fact]
    public void Transient_never_interrupts_the_user()
    {
        var (sm, _) = Create();
        sm.PointerEntered();
        sm.ShowTransient(TimeSpan.FromSeconds(3)); // el mouse está encima
        Assert.Equal(IslandState.Collapsed, sm.State);

        var (sm2, _) = Create();
        sm2.Clicked();
        sm2.PointerExited();
        sm2.ShowTransient(TimeSpan.FromSeconds(3)); // está expandida
        Assert.Equal(IslandState.Expanded, sm2.State);
        Assert.Equal(IslandTimer.AutoCollapse, sm2.PendingTimer);
    }

    [Fact]
    public void Hovering_during_transient_keeps_it_open_until_leaving()
    {
        var (sm, _) = Create();
        sm.ShowTransient(TimeSpan.FromSeconds(3));
        sm.PointerEntered();
        sm.TimerElapsed(IslandTimer.TransientEnd); // ya cancelado
        Assert.Equal(IslandState.Peek, sm.State);
        sm.PointerExited();
        sm.TimerElapsed(IslandTimer.LeaveGrace);
        Assert.Equal(IslandState.Collapsed, sm.State);
    }

    [Fact]
    public void Second_transient_extends_the_first()
    {
        var (sm, timers) = Create();
        sm.ShowTransient(TimeSpan.FromSeconds(3));
        sm.ShowTransient(TimeSpan.FromSeconds(3));
        Assert.Equal([IslandTimer.TransientEnd, IslandTimer.TransientEnd], timers);
        Assert.Equal(IslandState.Peek, sm.State);
    }

    [Fact]
    public void Force_collapse_cancels_timers_and_reports_change()
    {
        var (sm, _) = Create();
        var changes = new List<(IslandState, IslandState)>();
        sm.StateChanged += (a, b) => changes.Add((a, b));
        sm.Clicked();
        sm.ForceCollapse();
        Assert.Equal(IslandState.Collapsed, sm.State);
        Assert.Equal(IslandTimer.None, sm.PendingTimer);
        Assert.Equal([(IslandState.Collapsed, IslandState.Expanded), (IslandState.Expanded, IslandState.Collapsed)], changes);
    }
}
