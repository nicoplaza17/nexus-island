namespace NexusIsland.Core.Media;

/// <summary>
/// Cálculo de la posición de reproducción "ahora".
/// Muchas apps (Spotify, por ejemplo) solo informan la posición al pausar o adelantar,
/// junto con la hora de ese dato. Si está sonando, la posición actual se estima sumando
/// el tiempo transcurrido desde entonces.
/// </summary>
public static class PlaybackPosition
{
    private static readonly DateTimeOffset MinimumValidTimestamp = new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static TimeSpan Estimate(MediaSnapshot snapshot, DateTimeOffset now)
    {
        if (snapshot.Timeline is not { } timeline) return TimeSpan.Zero;

        var position = timeline.Position;
        if (snapshot.IsPlaying)
        {
            // Algunas apps no informan la hora del dato (queda en 0001-01-01) o informan una hora futura:
            // en ese caso se usa el momento en que leímos la información.
            var reference = timeline.LastUpdated >= MinimumValidTimestamp && timeline.LastUpdated <= now.AddMinutes(1)
                ? timeline.LastUpdated
                : snapshot.ReceivedAt;
            var rate = snapshot.PlaybackRate > 0 ? snapshot.PlaybackRate : 1.0;
            var elapsed = now - reference;
            if (elapsed > TimeSpan.Zero) position += TimeSpan.FromTicks((long)(elapsed.Ticks * rate));
        }

        if (timeline.End > timeline.Start)
        {
            if (position > timeline.End) position = timeline.End;
            if (position < timeline.Start) position = timeline.Start;
        }
        else if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }
        return position;
    }

    /// <summary>Progreso entre 0 y 1 (0 si no hay duración conocida, por ejemplo en una transmisión en vivo).</summary>
    public static double Progress(MediaTimeline timeline, TimeSpan position)
    {
        var duration = timeline.Duration;
        if (duration <= TimeSpan.Zero) return 0;
        return Math.Clamp((position - timeline.Start).TotalSeconds / duration.TotalSeconds, 0, 1);
    }

    /// <summary>"3:07" o "1:02:45".</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
