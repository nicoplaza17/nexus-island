using NexusIsland.Core.Geometry;
using NexusIsland.Core.Media;
using NexusIsland.Core.Settings;

namespace NexusIsland.Core.Tests;

public sealed class SettingsStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "nexus-tests-" + Guid.NewGuid().ToString("N"));
    private string FilePath => Path.Combine(_dir, "settings.json");

    public SettingsStoreTests() => Directory.CreateDirectory(_dir);

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Missing_file_gives_defaults()
    {
        var store = new SettingsStore(FilePath);
        Assert.Equal(IslandPlacementMode.DockedTopCenter, store.Current.Island.Placement);
        Assert.Equal("Ctrl+Alt+N", store.Current.Hotkey.ToString());
        Assert.True(store.Current.General.HideInFullscreen);
        Assert.True(store.Current.IsModuleEnabled("media"));
    }

    [Fact]
    public void Changes_survive_a_restart()
    {
        var store = new SettingsStore(FilePath);
        store.Update(s =>
        {
            s.Island.Placement = IslandPlacementMode.Floating;
            s.Island.FloatingX = 300;
            s.Island.FloatingY = 500;
            s.Island.Size = IslandSize.Large;
            s.Modules["media"] = false;
            s.Media.IgnoredApps.Add(new IgnoredApp("WhatsApp!App", "WhatsApp"));
        });

        var reloaded = new SettingsStore(FilePath).Current;
        Assert.Equal(IslandPlacementMode.Floating, reloaded.Island.Placement);
        Assert.Equal(300, reloaded.Island.FloatingX);
        Assert.Equal(IslandSize.Large, reloaded.Island.Size);
        Assert.False(reloaded.IsModuleEnabled("MEDIA")); // sin distinguir mayúsculas
        Assert.True(reloaded.Media.IsIgnored("whatsapp!app"));
        Assert.False(File.Exists(FilePath + ".tmp")); // el temporal no queda dando vueltas
    }

    [Fact]
    public void Corrupt_file_is_backed_up_and_defaults_are_used()
    {
        File.WriteAllText(FilePath, "{ esto no es JSON válido");
        var store = new SettingsStore(FilePath);
        Assert.Equal(IslandSize.Normal, store.Current.Island.Size);
        Assert.True(File.Exists(FilePath + ".bad"));
    }

    [Fact]
    public void Hand_edited_values_are_normalized()
    {
        File.WriteAllText(FilePath, """
            {
              // comentarios permitidos
              "Island": { "Size": "Large" },
              "Hotkey": { "Ctrl": false, "Alt": false, "Shift": false, "Win": false, "Key": "" },
              "Media": { "IgnoredApps": [ { "Id": "A", "Name": "a" }, { "Id": "a", "Name": "dup" }, { "Id": "", "Name": "x" } ] },
            }
            """);
        var s = new SettingsStore(FilePath).Current;
        Assert.Equal(IslandSize.Large, s.Island.Size);
        Assert.Equal("Ctrl+Alt+N", s.Hotkey.ToString()); // sin modificadores no se acepta
        Assert.Single(s.Media.IgnoredApps);              // duplicados y vacíos eliminados
        Assert.True(s.General.HideInFullscreen);          // lo que falta toma el valor por defecto
    }

    [Fact]
    public void Changed_event_fires_after_update()
    {
        var store = new SettingsStore(FilePath);
        AppSettings? received = null;
        store.Changed += s => received = s;
        store.Update(s => s.General.StartWithWindows = true);
        Assert.True(received?.General.StartWithWindows);
    }

    [Fact]
    public void Scale_by_size()
    {
        Assert.Equal(0.85, new IslandSettings { Size = IslandSize.Small }.Scale);
        Assert.Equal(1.0, new IslandSettings().Scale);
        Assert.Equal(1.15, new IslandSettings { Size = IslandSize.Large }.Scale);
    }
}

public class PlacementResolverTests
{
    private static readonly MonitorDescriptor Primary =
        new(@"\\.\DISPLAY1", new(0, 0, 1920, 1080), new(0, 0, 1920, 1032), true, 1.25);
    private static readonly MonitorDescriptor Second =
        new(@"\\.\DISPLAY2", new(1920, 0, 3840, 1080), new(1920, 0, 3840, 1040), false, 1.0);
    private static readonly PixelRect Window = PixelRect.FromSize(0, 0, 460, 220);

    [Fact]
    public void Default_is_top_center_of_primary()
    {
        var t = PlacementResolver.Resolve(new IslandSettings(), [Primary, Second], Window);
        Assert.Same(Primary, t.Monitor);
        Assert.Equal(new PixelPoint(730, 0), t.Position);
    }

    [Fact]
    public void Docked_on_saved_monitor()
    {
        var t = PlacementResolver.Resolve(new IslandSettings { MonitorDevice = @"\\.\display2" }, [Primary, Second], Window);
        Assert.Same(Second, t.Monitor);
        Assert.Equal(new PixelPoint(2650, 0), t.Position);
    }

    [Fact]
    public void Saved_monitor_disconnected_falls_back_to_primary()
    {
        var t = PlacementResolver.Resolve(new IslandSettings { MonitorDevice = @"\\.\DISPLAY2" }, [Primary], Window);
        Assert.Same(Primary, t.Monitor);
    }

    [Fact]
    public void Floating_position_is_restored_and_clamped()
    {
        var settings = new IslandSettings { Placement = IslandPlacementMode.Floating, FloatingX = 3700, FloatingY = 900 };
        var t = PlacementResolver.Resolve(settings, [Primary, Second], Window);
        Assert.Equal(IslandPlacementMode.Floating, t.Mode);
        Assert.Same(Second, t.Monitor);
        Assert.Equal(new PixelPoint(3380, 820), t.Position); // ajustada para entrar entera
    }

    [Fact]
    public void Floating_on_missing_monitor_docks_on_primary()
    {
        var settings = new IslandSettings { Placement = IslandPlacementMode.Floating, FloatingX = 2500, FloatingY = 300 };
        var t = PlacementResolver.Resolve(settings, [Primary], Window);
        Assert.Equal(IslandPlacementMode.DockedTopCenter, t.Mode);
        Assert.Same(Primary, t.Monitor);
    }
}

public class MediaSessionSelectorTests
{
    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase) { "WhatsApp!App" };

    [Fact]
    public void Uses_current_when_not_ignored()
    {
        SessionCandidate[] s = [new("Spotify.exe", MediaPlaybackStatus.Paused), new("Chrome", MediaPlaybackStatus.Playing)];
        Assert.Equal(0, MediaSessionSelector.Choose("Spotify.exe", s, Ignored.Contains));
    }

    [Fact]
    public void Ignored_current_falls_back_to_a_playing_session()
    {
        SessionCandidate[] s =
        [
            new("WhatsApp!App", MediaPlaybackStatus.Playing),
            new("Spotify.exe", MediaPlaybackStatus.Paused),
            new("Chrome", MediaPlaybackStatus.Playing),
        ];
        Assert.Equal(2, MediaSessionSelector.Choose("WhatsApp!App", s, Ignored.Contains));
    }

    [Fact]
    public void Falls_back_to_paused_if_nothing_plays()
    {
        SessionCandidate[] s = [new("WhatsApp!App", MediaPlaybackStatus.Playing), new("Spotify.exe", MediaPlaybackStatus.Paused)];
        Assert.Equal(1, MediaSessionSelector.Choose("WhatsApp!App", s, Ignored.Contains));
    }

    [Fact]
    public void Nothing_when_all_ignored_or_empty()
    {
        Assert.Equal(-1, MediaSessionSelector.Choose("WhatsApp!App", [new("WhatsApp!App", MediaPlaybackStatus.Playing)], Ignored.Contains));
        Assert.Equal(-1, MediaSessionSelector.Choose(null, [], Ignored.Contains));
    }
}
