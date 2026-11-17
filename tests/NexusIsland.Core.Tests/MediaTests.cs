using NexusIsland.Core.Media;

namespace NexusIsland.Core.Tests;

public class PlaybackPositionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 16, 12, 0, 0, TimeSpan.FromHours(-3));

    private static MediaSnapshot Snap(MediaPlaybackStatus status, TimeSpan position, DateTimeOffset lastUpdated,
        double rate = 1, TimeSpan? end = null) => new()
    {
        SourceAppId = "Spotify.exe",
        SourceAppName = "Spotify",
        Title = "Canción",
        Status = status,
        PlaybackRate = rate,
        Timeline = new MediaTimeline(TimeSpan.Zero, end ?? TimeSpan.FromMinutes(3), position, lastUpdated),
        ReceivedAt = T0,
    };

    [Fact]
    public void Playing_advances_from_last_update()
    {
        var s = Snap(MediaPlaybackStatus.Playing, TimeSpan.FromSeconds(30), T0);
        Assert.Equal(TimeSpan.FromSeconds(42), PlaybackPosition.Estimate(s, T0.AddSeconds(12)));
    }

    [Fact]
    public void Paused_does_not_advance()
    {
        var s = Snap(MediaPlaybackStatus.Paused, TimeSpan.FromSeconds(30), T0);
        Assert.Equal(TimeSpan.FromSeconds(30), PlaybackPosition.Estimate(s, T0.AddMinutes(5)));
    }

    [Fact]
    public void Respects_playback_rate()
    {
        var s = Snap(MediaPlaybackStatus.Playing, TimeSpan.Zero, T0, rate: 2);
        Assert.Equal(TimeSpan.FromSeconds(20), PlaybackPosition.Estimate(s, T0.AddSeconds(10)));
    }

    [Fact]
    public void Never_goes_past_the_end()
    {
        var s = Snap(MediaPlaybackStatus.Playing, TimeSpan.FromSeconds(170), T0);
        Assert.Equal(TimeSpan.FromMinutes(3), PlaybackPosition.Estimate(s, T0.AddMinutes(10)));
    }

    [Fact]
    public void Missing_timestamp_uses_time_received()
    {
        // Algunas apps no informan la hora del dato (queda en 0001-01-01).
        var s = Snap(MediaPlaybackStatus.Playing, TimeSpan.FromSeconds(10), default);
        Assert.Equal(TimeSpan.FromSeconds(15), PlaybackPosition.Estimate(s, T0.AddSeconds(5)));
    }

    [Fact]
    public void Future_timestamp_is_not_trusted()
    {
        var s = Snap(MediaPlaybackStatus.Playing, TimeSpan.FromSeconds(10), T0.AddHours(2));
        Assert.Equal(TimeSpan.FromSeconds(15), PlaybackPosition.Estimate(s, T0.AddSeconds(5)));
    }

    [Fact]
    public void Progress_and_format()
    {
        var t = new MediaTimeline(TimeSpan.Zero, TimeSpan.FromMinutes(4), TimeSpan.Zero, T0);
        Assert.Equal(0.25, PlaybackPosition.Progress(t, TimeSpan.FromMinutes(1)), 3);
        Assert.Equal(0, PlaybackPosition.Progress(new MediaTimeline(), TimeSpan.FromMinutes(1)));
        Assert.Equal("3:07", PlaybackPosition.Format(TimeSpan.FromSeconds(187)));
        Assert.Equal("0:05", PlaybackPosition.Format(TimeSpan.FromSeconds(5)));
        Assert.Equal("1:02:45", PlaybackPosition.Format(new TimeSpan(1, 2, 45)));
    }
}

public class SourceAppNamesTests
{
    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "Spotify")]
    [InlineData("MSEdge", "Microsoft Edge")]
    [InlineData("Chrome", "Google Chrome")]
    [InlineData("308046B0AF4A39CB", "Firefox")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "Reproductor multimedia")]
    [InlineData("Chrome._crx_pjibgcllelbfgfagdaldikeohf", "App web (Chrome)")]
    [InlineData("MSEdge._crx_abcdef", "App web (Edge)")]
    public void Known_apps(string aumid, string expected)
    {
        Assert.True(SourceAppNames.TryGetKnown(aumid, out var name));
        Assert.Equal(expected, name);
    }

    [Theory]
    [InlineData("miReproductor.exe", "MiReproductor")]
    [InlineData("Empresa.App_abc123!Player", "Player")]
    [InlineData(@"C:\Apps\radio.exe", "Radio")]
    [InlineData("", "Multimedia")]
    public void Unknown_apps_are_cleaned_up(string aumid, string expected)
    {
        Assert.False(SourceAppNames.TryGetKnown(aumid, out _));
        Assert.Equal(expected, SourceAppNames.Prettify(aumid));
    }
}
