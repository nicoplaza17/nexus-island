using NexusIsland.Core.Notifications;
using NexusIsland.Core.Settings;

namespace NexusIsland.Core.Tests;

public class NotificationTrackerTests
{
    [Fact]
    public void Existing_notifications_are_not_reported_as_new()
    {
        var tracker = new NotificationTracker();
        tracker.Seed([1, 2, 3]);
        Assert.Empty(tracker.Update([1, 2, 3]));
        Assert.Equal([4u], tracker.Update([1, 2, 3, 4]));
        Assert.Empty(tracker.Update([1, 2, 3, 4])); // no se repite
    }

    [Fact]
    public void Forgets_removed_ids_so_memory_does_not_grow()
    {
        var tracker = new NotificationTracker();
        tracker.Seed([1, 2]);
        Assert.Empty(tracker.Update([2])); // el usuario borró la 1 del centro de notificaciones
        Assert.Equal([1u], tracker.Update([1, 2])); // si Windows reutiliza el id, cuenta como nueva
    }
}

public class NotificationHistoryTests
{
    private static IslandNotification N(uint id) => new(id, "app", "App", $"t{id}", "", DateTimeOffset.UnixEpoch);

    [Fact]
    public void Newest_first_capped_without_duplicates()
    {
        var h = new NotificationHistory(capacity: 2);
        h.Add(N(1)); h.Add(N(2)); h.Add(N(1)); h.Add(N(3));
        Assert.Equal([3u, 1u], h.Items.Select(n => n.Id));
        h.Clear();
        Assert.Empty(h.Items);
    }
}

public sealed class NotificationSettingsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexus-ntf-" + Guid.NewGuid().ToString("N"));
    public NotificationSettingsTests() => Directory.CreateDirectory(_dir);
    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Muted_apps_persist_case_insensitive_without_duplicates()
    {
        var path = Path.Combine(_dir, "settings.json");
        var store = new SettingsStore(path);
        Assert.True(store.Current.Notifications.ShowContent); // por defecto se muestra el texto
        store.Update(s =>
        {
            s.Notifications.ShowContent = false;
            s.Notifications.MutedApps.Add(new IgnoredApp("WhatsApp!App", "WhatsApp"));
            s.Notifications.MutedApps.Add(new IgnoredApp("whatsapp!app", "WhatsApp (dup)"));
        });
        var reloaded = new SettingsStore(path).Current.Notifications;
        Assert.False(reloaded.ShowContent);
        Assert.Single(reloaded.MutedApps);
        Assert.True(reloaded.IsMuted("WHATSAPP!APP"));
    }
}
