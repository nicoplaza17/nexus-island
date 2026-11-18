namespace NexusIsland.Core.Media;

/// <summary>Nivel de sonido real de la salida de audio (pico desde la última lectura, 0 a 1 por canal).</summary>
public interface IAudioLevelMeter
{
    /// <summary>False si no hay dispositivo de salida o Windows no lo pudo informar.</summary>
    bool TryRead(out float left, out float right);
}

/// <summary>
/// Convierte el nivel de sonido en la altura de las tres barritas del indicador "sonando".
/// Todo sale del volumen real: izquierda (rápida), el máximo de los dos canales (lenta, como un
/// promedio) y derecha (intermedia). Cada barra sube rápido y baja suave, como un vúmetro.
/// </summary>
public sealed class LevelBars
{
    // Por barra: cuánto del salto hacia arriba se toma en un cuadro, y cuánto se conserva al bajar.
    private static readonly double[] Attack = [0.85, 0.45, 0.65];
    private static readonly double[] Decay = [0.55, 0.80, 0.68];

    private readonly double[] _levels = new double[3];

    /// <summary>Altura relativa de cada barra (0 a 1).</summary>
    public IReadOnlyList<double> Levels => _levels;

    public void Update(float left, float right)
    {
        // Raíz cuadrada: el oído es logarítmico; sin esto, la música a volumen normal casi no movería las barras.
        double l = Math.Sqrt(Math.Clamp(left, 0f, 1f));
        double r = Math.Sqrt(Math.Clamp(right, 0f, 1f));
        double[] target = [l, Math.Max(l, r), r];
        for (var i = 0; i < 3; i++)
        {
            var h = _levels[i];
            _levels[i] = target[i] > h
                ? h + (target[i] - h) * Attack[i]
                : h * Decay[i] + target[i] * (1 - Decay[i]);
            if (_levels[i] < 0.001) _levels[i] = 0;
        }
    }

    public void Reset() => Array.Clear(_levels);

    /// <summary>
    /// Altura en píxeles entre <paramref name="min"/> y <paramref name="max"/>, redondeada a medio píxel:
    /// cambios imperceptibles no provocan un redibujado de la isla.
    /// </summary>
    public static double ToPixels(double level, double min, double max) =>
        Math.Round((min + Math.Clamp(level, 0, 1) * (max - min)) * 2) / 2;
}
