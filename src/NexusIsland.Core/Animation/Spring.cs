namespace NexusIsland.Core.Animation;

/// <summary>
/// Resorte físico de una dimensión (masa = 1). Es lo que da la sensación "elástica" de la isla:
/// la animación no tiene una duración fija sino que sigue la física, y si cambia el destino a mitad
/// de camino continúa suavemente desde donde está (con su velocidad), sin saltos.
///
/// - Stiffness (rigidez): más alta = más rápido.
/// - DampingRatio: 1 = llega sin pasarse; &lt; 1 = se pasa un poco y rebota (0,75–0,85 se siente natural).
/// </summary>
public sealed class Spring
{
    private const double SubStep = 1.0 / 240.0; // paso fijo: estable aunque baje la tasa de cuadros

    public Spring(double initialValue, double stiffness = 380, double dampingRatio = 0.8, double restThreshold = 0.05)
    {
        Value = Target = initialValue;
        Stiffness = stiffness;
        DampingRatio = dampingRatio;
        RestThreshold = restThreshold;
    }

    public double Value { get; private set; }
    public double Velocity { get; private set; }
    public double Target { get; set; }
    public double Stiffness { get; set; }
    public double DampingRatio { get; set; }

    /// <summary>Distancia (y velocidad/10) por debajo de la cual se considera quieto y se "pega" al destino.</summary>
    public double RestThreshold { get; set; }

    public bool IsAtRest => Value == Target && Velocity == 0;

    /// <summary>Avanza la simulación <paramref name="seconds"/> segundos. Devuelve true si sigue en movimiento.</summary>
    public bool Step(double seconds)
    {
        if (IsAtRest) return false;
        if (seconds <= 0) return true;

        var damping = 2 * DampingRatio * Math.Sqrt(Stiffness);
        var remaining = Math.Min(seconds, 0.1); // si la app se trabó, no "saltar" de golpe
        while (remaining > 0)
        {
            var h = Math.Min(SubStep, remaining);
            var acceleration = -Stiffness * (Value - Target) - damping * Velocity;
            Velocity += acceleration * h; // Euler semi-implícito: estable para resortes
            Value += Velocity * h;
            remaining -= h;
        }

        if (Math.Abs(Value - Target) < RestThreshold && Math.Abs(Velocity) < RestThreshold * 10)
        {
            Value = Target;
            Velocity = 0;
            return false;
        }
        return true;
    }

    /// <summary>Salta al valor sin animar (por ejemplo con "reducir movimiento" activado).</summary>
    public void SnapTo(double value)
    {
        Value = Target = value;
        Velocity = 0;
    }
}
