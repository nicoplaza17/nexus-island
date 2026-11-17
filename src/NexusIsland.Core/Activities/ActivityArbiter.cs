namespace NexusIsland.Core.Activities;

/// <summary>
/// Decide qué actividad se muestra en la isla: la de mayor prioridad y, si empatan, la más reciente.
/// Es el sistema de relevancia que evita que todos los módulos peleen por el espacio.
/// No es thread-safe: usar desde el hilo de la interfaz.
/// </summary>
public sealed class ActivityArbiter : IActivityHost
{
    private readonly Dictionary<string, (IslandActivity Activity, long Sequence)> _activities = new(StringComparer.Ordinal);
    private long _sequence;

    public IslandActivity? Current { get; private set; }

    public IReadOnlyCollection<IslandActivity> All => _activities.Values.Select(v => v.Activity).ToArray();

    /// <summary>Todas las actividades en el orden en que compiten: prioridad y después la más reciente.</summary>
    public IReadOnlyList<IslandActivity> Ranked => _activities.Values
        .OrderByDescending(v => v.Activity.Priority)
        .ThenByDescending(v => v.Sequence)
        .Select(v => v.Activity)
        .ToList();

    /// <summary>Se agregó, quitó o reemplazó alguna actividad (visible o no).</summary>
    public event Action? ActivitiesChanged;

    public bool Contains(string key) => _activities.ContainsKey(key);

    /// <summary>Cambió la actividad visible (o su ViewModel).</summary>
    public event Action<IslandActivity?>? CurrentChanged;

    /// <summary>La actividad visible pidió mostrarse brevemente.</summary>
    public event Action<IslandActivity, TimeSpan>? AttentionRequested;

    public void Publish(IslandActivity activity)
    {
        ArgumentNullException.ThrowIfNull(activity);
        // Re-publicar no la vuelve "más nueva": conserva su lugar en el desempate.
        var sequence = _activities.TryGetValue(activity.Key, out var existing) ? existing.Sequence : ++_sequence;
        _activities[activity.Key] = (activity, sequence);
        Recompute();
        ActivitiesChanged?.Invoke();
    }

    public void Remove(string key)
    {
        if (!_activities.Remove(key)) return;
        Recompute();
        ActivitiesChanged?.Invoke();
    }

    public void RequestAttention(string key, TimeSpan duration)
    {
        if (Current is not null && Current.Key == key && duration > TimeSpan.Zero)
            AttentionRequested?.Invoke(Current, duration);
    }

    private void Recompute()
    {
        IslandActivity? best = null;
        long bestSequence = -1;
        foreach (var (activity, sequence) in _activities.Values)
        {
            if (best is null || activity.Priority > best.Priority
                || (activity.Priority == best.Priority && sequence > bestSequence))
            {
                best = activity;
                bestSequence = sequence;
            }
        }

        if (ReferenceEquals(best, Current)) return;
        Current = best;
        CurrentChanged?.Invoke(best);
    }
}
