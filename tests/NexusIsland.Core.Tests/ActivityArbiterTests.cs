using NexusIsland.Core.Activities;

namespace NexusIsland.Core.Tests;

public class ActivityArbiterTests
{
    private static IslandActivity A(string key, int priority) => new(key, key, priority, new object());

    [Fact]
    public void Shows_highest_priority()
    {
        var arbiter = new ActivityArbiter();
        arbiter.Publish(A("clock", ActivityPriority.Idle));
        arbiter.Publish(A("media", ActivityPriority.Ambient));
        Assert.Equal("media", arbiter.Current?.Key);
    }

    [Fact]
    public void Removing_falls_back_to_next_best()
    {
        var arbiter = new ActivityArbiter();
        arbiter.Publish(A("clock", ActivityPriority.Idle));
        arbiter.Publish(A("media", ActivityPriority.Ambient));
        arbiter.Remove("media");
        Assert.Equal("clock", arbiter.Current?.Key);
    }

    [Fact]
    public void Ties_go_to_the_newest_and_republishing_does_not_steal()
    {
        var arbiter = new ActivityArbiter();
        arbiter.Publish(A("first", 10));
        arbiter.Publish(A("second", 10));
        Assert.Equal("second", arbiter.Current?.Key);

        arbiter.Publish(A("first", 10)); // actualización: conserva su antigüedad
        Assert.Equal("second", arbiter.Current?.Key);
    }

    [Fact]
    public void Raises_change_only_when_visible_activity_changes()
    {
        var arbiter = new ActivityArbiter();
        var changes = 0;
        arbiter.CurrentChanged += _ => changes++;
        var media = A("media", 50);
        arbiter.Publish(media);
        arbiter.Publish(A("clock", 0)); // de menor prioridad: no cambia lo visible
        arbiter.Publish(media);         // misma instancia: no cambia
        Assert.Equal(1, changes);
    }

    [Fact]
    public void Attention_only_for_the_visible_activity()
    {
        var arbiter = new ActivityArbiter();
        var requests = new List<string>();
        arbiter.AttentionRequested += (a, _) => requests.Add(a.Key);
        arbiter.Publish(A("clock", 0));
        arbiter.Publish(A("alert", 200));
        arbiter.RequestAttention("clock", TimeSpan.FromSeconds(3)); // tapado por "alert": se ignora
        arbiter.RequestAttention("alert", TimeSpan.FromSeconds(3));
        arbiter.RequestAttention("missing", TimeSpan.FromSeconds(3));
        Assert.Equal(["alert"], requests);
    }
}
