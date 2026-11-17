using NexusIsland.Core.Activities;
using NexusIsland.Core.Tools;

namespace NexusIsland.Core.Tests;

public class CountdownTimerTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(-3));

    [Fact]
    public void Counts_down_and_finishes_once()
    {
        var t = new CountdownTimer();
        t.Start(TimeSpan.FromMinutes(5), T0);
        Assert.Equal(TimeSpan.FromMinutes(4), t.Remaining(T0.AddMinutes(1)));
        Assert.Equal(0.2, t.Progress(T0.AddMinutes(1)), 3);
        Assert.False(t.CheckFinished(T0.AddMinutes(4)));
        Assert.True(t.CheckFinished(T0.AddMinutes(5)));
        Assert.False(t.CheckFinished(T0.AddMinutes(6))); // avisa una sola vez
        Assert.Equal(CountdownState.Finished, t.State);
    }

    [Fact]
    public void Pause_freezes_and_resume_continues()
    {
        var t = new CountdownTimer();
        t.Start(TimeSpan.FromMinutes(5), T0);
        t.Pause(T0.AddMinutes(1));
        Assert.Equal(TimeSpan.FromMinutes(4), t.Remaining(T0.AddMinutes(30)));
        t.Resume(T0.AddMinutes(30));
        Assert.Equal(TimeSpan.FromMinutes(3), t.Remaining(T0.AddMinutes(31)));
        Assert.True(t.CheckFinished(T0.AddMinutes(34)));
    }

    [Fact]
    public void Add_minute_extends_or_restarts_after_finishing()
    {
        var t = new CountdownTimer();
        t.Start(TimeSpan.FromMinutes(1), T0);
        t.Add(TimeSpan.FromMinutes(1), T0);
        Assert.Equal(TimeSpan.FromMinutes(2), t.Remaining(T0));
        Assert.Equal(TimeSpan.FromMinutes(2), t.Total);

        Assert.True(t.CheckFinished(T0.AddMinutes(2)));
        t.Add(TimeSpan.FromMinutes(1), T0.AddMinutes(3)); // "+1 min" en la alerta
        Assert.Equal(CountdownState.Running, t.State);
        Assert.Equal(TimeSpan.FromSeconds(30), t.Remaining(T0.AddMinutes(3.5)));
    }

    [Fact]
    public void Format_rounds_up_so_zero_means_done()
    {
        Assert.Equal("5:00", CountdownTimer.Format(TimeSpan.FromMinutes(5)));
        Assert.Equal("0:01", CountdownTimer.Format(TimeSpan.FromMilliseconds(200)));
        Assert.Equal("0:00", CountdownTimer.Format(TimeSpan.Zero));
        Assert.Equal("1:00:00", CountdownTimer.Format(TimeSpan.FromHours(1)));
    }
}

public class StopwatchClockTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Accumulates_across_pauses()
    {
        var s = new StopwatchClock();
        s.Start(T0);
        s.Pause(T0.AddSeconds(10));
        s.Start(T0.AddSeconds(100));
        Assert.Equal(TimeSpan.FromSeconds(15), s.Elapsed(T0.AddSeconds(105)));
    }

    [Fact]
    public void Laps_are_split_times_newest_first()
    {
        var s = new StopwatchClock();
        s.Start(T0);
        s.Lap(T0.AddSeconds(30));
        s.Lap(T0.AddSeconds(75));
        Assert.Equal([TimeSpan.FromSeconds(45), TimeSpan.FromSeconds(30)], s.Laps);
        s.Reset();
        Assert.Empty(s.Laps);
        Assert.False(s.HasStarted);
    }
}

public class ClipboardHistoryTests
{
    private static readonly DateTimeOffset T0 = DateTimeOffset.UnixEpoch;

    [Fact]
    public void Newest_first_without_duplicates_and_capped()
    {
        var h = new ClipboardHistory(capacity: 3);
        h.Add("a", T0); h.Add("b", T0); h.Add("c", T0);
        h.Add("a", T0); // ya estaba: sube al principio
        Assert.Equal(["a", "c", "b"], h.Entries.Select(e => e.Text));
        h.Add("d", T0);
        Assert.Equal(["d", "a", "c"], h.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Ignores_empty_and_huge_texts()
    {
        var h = new ClipboardHistory();
        Assert.False(h.Add("", T0));
        Assert.False(h.Add("   \r\n", T0));
        Assert.False(h.Add(new string('x', ClipboardHistory.MaxTextLength + 1), T0));
        Assert.Empty(h.Entries);
    }

    [Fact]
    public void Preview_is_one_short_line()
    {
        var e = new ClipboardEntry("línea 1\r\n\tlínea 2   ", T0);
        Assert.Equal("línea 1 línea 2", e.Preview);
        Assert.Equal(118, new ClipboardEntry(new string('x', 500), T0).Preview.Length);
    }
}

public sealed class ReminderStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexus-rem-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 10, 0, 0, TimeSpan.FromHours(-3));
    private string FilePath => Path.Combine(_dir, "reminders.json");

    public ReminderStoreTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Persists_sorted_and_reports_due()
    {
        var store = new ReminderStore(FilePath);
        store.Add("después", T0.AddHours(2));
        var soon = store.Add("  pronto  ", T0.AddMinutes(10));
        Assert.Equal("pronto", soon.Text);

        var reloaded = new ReminderStore(FilePath); // sobrevive a un reinicio
        Assert.Equal(["pronto", "después"], reloaded.Pending.Select(r => r.Text));
        Assert.Empty(reloaded.Due(T0));
        Assert.Single(reloaded.Due(T0.AddMinutes(10)));
        Assert.Equal(2, reloaded.Due(T0.AddDays(1)).Count); // la PC estuvo apagada: avisa todos al volver
    }

    [Fact]
    public void Snooze_and_remove()
    {
        var store = new ReminderStore(FilePath);
        var r = store.Add("llamar", T0);
        store.Snooze(r.Id, T0.AddMinutes(10));
        Assert.Empty(store.Due(T0.AddMinutes(5)));
        Assert.True(store.Remove(r.Id));
        Assert.Empty(new ReminderStore(FilePath).Pending);
    }

    [Fact]
    public void Corrupt_file_is_backed_up()
    {
        File.WriteAllText(FilePath, "[ roto");
        Assert.Empty(new ReminderStore(FilePath).Pending);
        Assert.True(File.Exists(FilePath + ".bad"));
    }
}

public class ArbiterRankingTests
{
    [Fact]
    public void Ranked_orders_by_priority_and_reports_changes()
    {
        var arbiter = new ActivityArbiter();
        var changes = 0;
        arbiter.ActivitiesChanged += () => changes++;
        arbiter.Publish(new IslandActivity("clock", "clock", ActivityPriority.Idle, new object()));
        arbiter.Publish(new IslandActivity("media", "media", ActivityPriority.Ambient, new object()));
        arbiter.Publish(new IslandActivity("timer", "timer", ActivityPriority.Active, new object()));
        Assert.Equal(["timer", "media", "clock"], arbiter.Ranked.Select(a => a.Key));
        arbiter.Remove("timer");
        arbiter.Remove("missing"); // no cambia nada: no avisa
        Assert.Equal(4, changes);
        Assert.True(arbiter.Contains("media"));
    }
}
