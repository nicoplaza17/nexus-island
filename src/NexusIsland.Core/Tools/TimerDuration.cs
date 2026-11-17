namespace NexusIsland.Core.Tools;

/// <summary>Elegir los minutos del temporizador: de 1 a 180, con pasos cómodos.</summary>
public static class TimerDuration
{
    public const int Min = 1;
    public const int Max = 180;
    public const int Default = 10;

    /// <summary>Accesos rápidos.</summary>
    public static readonly int[] Presets = [5, 10, 15, 25];

    public static int Clamp(int minutes) => Math.Clamp(minutes, Min, Max);

    /// <summary>
    /// Un paso con − / +: de a 1 minuto hasta 10, de a 5 hasta 60, de a 15 después
    /// (así llegar a una hora son pocos toques, sin perder precisión en tiempos cortos).
    /// </summary>
    public static int Step(int minutes, int direction)
    {
        minutes = Clamp(minutes);
        if (direction > 0)
        {
            var step = minutes < 10 ? 1 : minutes < 60 ? 5 : 15;
            return Clamp((minutes / step + 1) * step); // redondea al múltiplo siguiente (12 → 15)
        }
        else
        {
            var step = minutes <= 10 ? 1 : minutes <= 60 ? 5 : 15;
            var down = minutes % step == 0 ? minutes - step : minutes / step * step; // 12 → 10
            return Clamp(down);
        }
    }
}
