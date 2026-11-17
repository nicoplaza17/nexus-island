using Microsoft.Extensions.Logging;
using Windows.UI.ViewManagement;

namespace NexusIsland.Services.Windows.Accessibility;

/// <summary>
/// "Tamaño del texto" de Windows (Configuración → Accesibilidad → Tamaño del texto): 100 % a 225 %.
/// Es un ajuste distinto de la escala de pantalla (DPI), que WPF ya respeta solo. Lo expone la API
/// WinRT UISettings, que funciona en apps de escritorio.
/// </summary>
public sealed class TextScaleWatcher : IDisposable
{
    private readonly ILogger<TextScaleWatcher> _logger;
    private readonly UISettings? _ui;

    public TextScaleWatcher(ILogger<TextScaleWatcher> logger)
    {
        _logger = logger;
        try
        {
            _ui = new UISettings();
            _ui.TextScaleFactorChanged += OnChanged;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo leer el tamaño del texto de Windows: se usa 100 %");
        }
    }

    /// <summary>1.0 = 100 %. Si no se puede leer, 1.0.</summary>
    public double Factor
    {
        get
        {
            try { return _ui?.TextScaleFactor ?? 1.0; }
            catch { return 1.0; }
        }
    }

    /// <summary>Cambió el tamaño del texto. Se dispara en un hilo cualquiera (no el de la interfaz).</summary>
    public event Action? Changed;

    private void OnChanged(UISettings sender, object args)
    {
        _logger.LogInformation("Tamaño del texto de Windows cambiado: {Factor:P0}", Factor);
        Changed?.Invoke();
    }

    public void Dispose()
    {
        if (_ui is not null) _ui.TextScaleFactorChanged -= OnChanged;
    }
}
