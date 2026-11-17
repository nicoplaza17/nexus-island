using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace NexusIsland.Core.Settings;

/// <summary>
/// Carga y guarda la configuración.
///  - Guardado seguro: se escribe un archivo temporal y después se reemplaza el real de una sola vez,
///    así un corte a mitad de camino nunca deja un archivo a medias.
///  - Si el archivo está dañado, se guarda una copia (.bad) y se usan valores por defecto.
/// No es thread-safe: usar desde el hilo de la interfaz.
/// </summary>
public sealed class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly string _path;
    private readonly ILogger? _logger;

    public SettingsStore(string path, ILogger<SettingsStore>? logger = null)
    {
        _path = path;
        _logger = logger;
        Current = Load();
    }

    public string FilePath => _path;

    public AppSettings Current { get; private set; }

    /// <summary>Se dispara después de cada cambio guardado.</summary>
    public event Action<AppSettings>? Changed;

    /// <summary>Aplica un cambio, lo guarda y avisa a los interesados.</summary>
    public void Update(Action<AppSettings> change)
    {
        change(Current);
        Current.Normalize();
        Save();
        Changed?.Invoke(Current);
    }

    private AppSettings Load()
    {
        if (!File.Exists(_path))
        {
            _logger?.LogInformation("Sin configuración guardada: se usan valores por defecto");
            return Defaults();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_path), JsonOptions) ?? Defaults();
            settings.Normalize();
            _logger?.LogInformation("Configuración cargada de {Path}", _path);
            return settings;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            var backup = _path + ".bad";
            try { File.Copy(_path, backup, overwrite: true); } catch (IOException) { }
            _logger?.LogWarning(ex, "Configuración dañada: se respaldó en {Backup} y se usan valores por defecto", backup);
            return Defaults();
        }
    }

    private void Save()
    {
        try
        {
            Storage.AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(Current, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "No se pudo guardar la configuración en {Path}", _path);
        }
    }

    private static AppSettings Defaults()
    {
        var settings = new AppSettings();
        settings.Normalize();
        return settings;
    }
}
