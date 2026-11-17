namespace NexusIsland.Core.Island;

/// <summary>
/// Reglas de comportamiento de la isla, sin nada de interfaz ni de Windows (por eso es testeable).
/// La interfaz le avisa lo que pasa (mouse entra/sale, clic, temporizador cumplido) y la máquina
/// decide el estado y qué temporizador hace falta.
///
///   Contraída --(mouse quieto encima 140 ms)--> Vistazo --(mouse sale 260 ms)--> Contraída
///   Contraída/Vistazo --(clic)--> Expandida --(clic)--> Vistazo/Contraída
///   Expandida --(mouse afuera 2,2 s)--> Contraída
///   Contraída --(notificación temporal)--> Vistazo --(fin, si el mouse no está encima)--> Contraída
/// </summary>
public sealed class IslandStateMachine
{
    private readonly IslandTiming _timing;

    public IslandStateMachine(IslandTiming? timing = null)
    {
        _timing = timing ?? new IslandTiming();
    }

    public IslandState State { get; private set; } = IslandState.Collapsed;

    public bool IsPointerInside { get; private set; }

    /// <summary>Temporizador pendiente (o None). La interfaz debe programarlo cuando cambie.</summary>
    public IslandTimer PendingTimer { get; private set; } = IslandTimer.None;

    /// <summary>Se dispara con (anterior, nuevo) cada vez que cambia el estado.</summary>
    public event Action<IslandState, IslandState>? StateChanged;

    /// <summary>Se dispara cuando hay que programar (delay &gt; 0) o cancelar (None) un temporizador.</summary>
    public event Action<IslandTimer, TimeSpan>? TimerRequested;

    public void PointerEntered()
    {
        IsPointerInside = true;
        if (State == IslandState.Collapsed)
            Schedule(IslandTimer.HoverIntent);
        else
            Schedule(IslandTimer.None); // volvió a entrar: se cancela cualquier contracción pendiente
    }

    public void PointerExited()
    {
        IsPointerInside = false;
        switch (State)
        {
            case IslandState.Collapsed: Schedule(IslandTimer.None); break;
            case IslandState.Peek: Schedule(IslandTimer.LeaveGrace); break;
            case IslandState.Expanded: Schedule(IslandTimer.AutoCollapse); break;
        }
    }

    public void Clicked()
    {
        // Un clic implica que el puntero está encima, aunque no haya llegado el aviso de "entró"
        // (por ejemplo con pantalla táctil). Así la contracción automática funciona igual al salir.
        IsPointerInside = true;
        Schedule(IslandTimer.None);
        if (State == IslandState.Expanded)
            SetState(IsPointerInside ? IslandState.Peek : IslandState.Collapsed);
        else
            SetState(IslandState.Expanded);
    }

    public void TimerElapsed(IslandTimer timer)
    {
        // Ignora temporizadores viejos que ya fueron reemplazados o cancelados.
        if (timer == IslandTimer.None || timer != PendingTimer) return;
        PendingTimer = IslandTimer.None;

        switch (timer)
        {
            case IslandTimer.HoverIntent when IsPointerInside && State == IslandState.Collapsed:
                SetState(IslandState.Peek);
                break;
            case IslandTimer.LeaveGrace when !IsPointerInside && State == IslandState.Peek:
                SetState(IslandState.Collapsed);
                break;
            case IslandTimer.AutoCollapse when !IsPointerInside && State == IslandState.Expanded:
                SetState(IslandState.Collapsed);
                break;
            case IslandTimer.TransientEnd when !IsPointerInside && State == IslandState.Peek:
                SetState(IslandState.Collapsed);
                break;
        }
    }

    /// <summary>
    /// Notificación temporal: muestra el vistazo durante <paramref name="duration"/> y vuelve a contraída.
    /// No interrumpe al usuario: si la isla está expandida o el mouse está encima, no hace nada.
    /// </summary>
    public void ShowTransient(TimeSpan duration)
    {
        if (IsPointerInside || duration <= TimeSpan.Zero) return;
        if (State == IslandState.Collapsed)
        {
            SetState(IslandState.Peek);
            Schedule(IslandTimer.TransientEnd, duration);
        }
        else if (State == IslandState.Peek && PendingTimer == IslandTimer.TransientEnd)
        {
            Schedule(IslandTimer.TransientEnd, duration); // otra notificación: se extiende
        }
    }

    /// <summary>Vuelve a contraída de inmediato (por ejemplo al empezar a arrastrar u ocultar).</summary>
    public void ForceCollapse()
    {
        Schedule(IslandTimer.None);
        SetState(IslandState.Collapsed);
    }

    private void Schedule(IslandTimer timer, TimeSpan? customDelay = null)
    {
        if (timer == PendingTimer && customDelay is null) return;
        PendingTimer = timer;
        var delay = customDelay ?? timer switch
        {
            IslandTimer.HoverIntent => _timing.HoverIntent,
            IslandTimer.LeaveGrace => _timing.LeaveGrace,
            IslandTimer.AutoCollapse => _timing.AutoCollapse,
            _ => TimeSpan.Zero,
        };
        TimerRequested?.Invoke(timer, delay);
    }

    private void SetState(IslandState next)
    {
        if (next == State) return;
        var previous = State;
        State = next;
        StateChanged?.Invoke(previous, next);
    }
}
