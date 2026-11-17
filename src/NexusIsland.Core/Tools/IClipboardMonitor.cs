namespace NexusIsland.Core.Tools;

/// <summary>
/// Avisa cuando se copia texto en cualquier app y permite volver a copiar un texto.
/// La implementación real (Services.Windows) respeta los formatos con los que los gestores de
/// contraseñas piden no ser registrados.
/// </summary>
public interface IClipboardMonitor : IDisposable
{
    /// <summary>Se copió texto. Llega en el hilo de la interfaz.</summary>
    event Action<string>? TextCopied;

    /// <summary>Empieza a escuchar. Llamar desde el hilo de la interfaz.</summary>
    void Start();

    void Stop();

    /// <summary>Pone un texto en el portapapeles. False si otra app lo tenía bloqueado.</summary>
    bool TrySetText(string text);

    /// <summary>
    /// Lee el texto que hay ahora en el portapapeles. Null si no hay texto, si está ocupado, o si la app de
    /// origen pidió no ser leída (gestores de contraseñas).
    /// </summary>
    string? TryGetText();
}
