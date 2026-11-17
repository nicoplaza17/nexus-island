namespace NexusIsland.Core.Tools;

/// <summary>Cronómetro con vueltas. Igual que el temporizador, recibe la hora actual en cada llamada.</summary>
public sealed class StopwatchClock
{
    private TimeSpan _accumulated;
    private DateTimeOffset _runningSince;
    private readonly List<TimeSpan> _laps = [];

    public bool IsRunning { get; private set; }

    /// <summary>Tiempos de vuelta (cada uno medido desde la vuelta anterior), la más reciente primero.</summary>
    public IReadOnlyList<TimeSpan> Laps => _laps;

    public bool HasStarted => IsRunning || _accumulated > TimeSpan.Zero;

    public void Start(DateTimeOffset now)
    {
        if (IsRunning) return;
        _runningSince = now;
        IsRunning = true;
    }

    public void Pause(DateTimeOffset now)
    {
        if (!IsRunning) return;
        _accumulated += now - _runningSince;
        IsRunning = false;
    }

    public void Reset()
    {
        IsRunning = false;
        _accumulated = TimeSpan.Zero;
        _laps.Clear();
    }

    public TimeSpan Elapsed(DateTimeOffset now) => IsRunning ? _accumulated + (now - _runningSince) : _accumulated;

    /// <summary>Marca una vuelta: guarda el tiempo desde la vuelta anterior. Se conservan las últimas 50.</summary>
    public TimeSpan Lap(DateTimeOffset now)
    {
        var total = Elapsed(now);
        var previousTotal = TimeSpan.Zero;
        foreach (var lap in _laps) previousTotal += lap;
        var lapTime = total - previousTotal;
        _laps.Insert(0, lapTime);
        if (_laps.Count > 50) _laps.RemoveAt(_laps.Count - 1);
        return lapTime;
    }

    /// <summary>"1:23" o "1:02:03" (sin décimas: se actualiza una vez por segundo para no gastar CPU).</summary>
    public static string Format(TimeSpan time)
    {
        if (time < TimeSpan.Zero) time = TimeSpan.Zero;
        return time.TotalHours >= 1
            ? $"{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}"
            : $"{(int)time.TotalMinutes}:{time.Seconds:00}";
    }
}
