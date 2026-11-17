namespace NexusIsland.Core.Tools;

public enum CountdownState
{
    Idle,
    Running,
    Paused,
    Finished,
}

/// <summary>
/// Temporizador de cuenta regresiva. No usa relojes internos: recibe la hora actual en cada llamada,
/// así es exacto (no acumula error) y se puede probar sin esperar.
/// </summary>
public sealed class CountdownTimer
{
    private DateTimeOffset _endsAt;
    private TimeSpan _remainingWhenPaused;

    public CountdownState State { get; private set; } = CountdownState.Idle;

    /// <summary>Duración total de la cuenta actual (incluye los minutos agregados).</summary>
    public TimeSpan Total { get; private set; }

    public void Start(TimeSpan duration, DateTimeOffset now)
    {
        if (duration <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(duration));
        Total = duration;
        _endsAt = now + duration;
        State = CountdownState.Running;
    }

    public void Pause(DateTimeOffset now)
    {
        if (State != CountdownState.Running) return;
        _remainingWhenPaused = Remaining(now);
        State = CountdownState.Paused;
    }

    public void Resume(DateTimeOffset now)
    {
        if (State != CountdownState.Paused) return;
        _endsAt = now + _remainingWhenPaused;
        State = CountdownState.Running;
    }

    /// <summary>Suma tiempo (también sirve para "+1 min" cuando ya terminó).</summary>
    public void Add(TimeSpan extra, DateTimeOffset now)
    {
        if (extra <= TimeSpan.Zero) return;
        switch (State)
        {
            case CountdownState.Running:
                _endsAt += extra;
                Total += extra;
                break;
            case CountdownState.Paused:
                _remainingWhenPaused += extra;
                Total += extra;
                break;
            case CountdownState.Finished:
                Start(extra, now);
                break;
        }
    }

    public void Cancel() => State = CountdownState.Idle;

    public TimeSpan Remaining(DateTimeOffset now) => State switch
    {
        CountdownState.Running => _endsAt > now ? _endsAt - now : TimeSpan.Zero,
        CountdownState.Paused => _remainingWhenPaused,
        _ => TimeSpan.Zero,
    };

    /// <summary>Fracción transcurrida (0 a 1), para la barra de progreso.</summary>
    public double Progress(DateTimeOffset now) =>
        Total <= TimeSpan.Zero ? 0 : Math.Clamp(1 - Remaining(now).TotalSeconds / Total.TotalSeconds, 0, 1);

    /// <summary>Si estaba corriendo y llegó a cero, pasa a Finished y devuelve true (una sola vez).</summary>
    public bool CheckFinished(DateTimeOffset now)
    {
        if (State != CountdownState.Running || now < _endsAt) return false;
        State = CountdownState.Finished;
        return true;
    }

    /// <summary>"4:59", "1:02:03". Redondea hacia arriba: "0:01" hasta que realmente llega a cero.</summary>
    public static string Format(TimeSpan time)
    {
        var seconds = (long)Math.Ceiling(Math.Max(0, time.TotalSeconds));
        var t = TimeSpan.FromSeconds(seconds);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{(int)t.TotalMinutes}:{t.Seconds:00}";
    }
}
