namespace NexusIsland.Core.Media;

/// <summary>
/// Traduce el identificador técnico de una app (AppUserModelId) a un nombre legible.
/// Windows no da un nombre "lindo" para apps de escritorio, por eso hay una tabla de apps conocidas.
/// </summary>
public static class SourceAppNames
{
    // Se busca "contiene" sin distinguir mayúsculas, en este orden.
    private static readonly (string Fragment, string Name)[] Known =
    [
        ("spotify", "Spotify"),
        ("msedge", "Microsoft Edge"),
        ("microsoftedge", "Microsoft Edge"),
        ("chrome", "Google Chrome"),
        ("308046B0AF4A39CB", "Firefox"), // identificador de Firefox en su instalación por defecto
        ("firefox", "Firefox"),
        ("brave", "Brave"),
        ("opera", "Opera"),
        ("vivaldi", "Vivaldi"),
        ("applemusic", "Apple Music"),
        ("AppleInc.iTunes", "iTunes"),
        ("ZuneMusic", "Reproductor multimedia"),
        ("ZuneVideo", "Películas y TV"),
        ("deezer", "Deezer"),
        ("tidal", "TIDAL"),
        ("amazonmusic", "Amazon Music"),
        ("youtubemusic", "YouTube Music"),
        ("foobar2000", "foobar2000"),
        ("musicbee", "MusicBee"),
        ("aimp", "AIMP"),
        ("vlc", "VLC"),
        ("discord", "Discord"),
    ];

    public static bool TryGetKnown(string appUserModelId, out string name)
    {
        // Apps web instaladas desde un navegador (PWA): "Chrome._crx_<id>", "MSEdge._crx_<id>".
        // Windows no expone su nombre por esta vía; se indica de dónde vienen.
        if (appUserModelId.Contains("_crx_", StringComparison.OrdinalIgnoreCase))
        {
            name = appUserModelId.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase)
                ? "App web (Edge)" : "App web (Chrome)";
            return true;
        }

        foreach (var (fragment, known) in Known)
        {
            if (appUserModelId.Contains(fragment, StringComparison.OrdinalIgnoreCase))
            {
                name = known;
                return true;
            }
        }
        name = "";
        return false;
    }

    /// <summary>Último recurso: limpia el identificador ("MiApp.exe" → "MiApp", "Paquete_xyz!App" → "App").</summary>
    public static string Prettify(string appUserModelId)
    {
        if (string.IsNullOrWhiteSpace(appUserModelId)) return "Multimedia";
        var name = appUserModelId;
        var bang = name.LastIndexOf('!');
        if (bang >= 0 && bang < name.Length - 1) name = name[(bang + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var slash = name.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) name = name[(slash + 1)..];
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : "Multimedia";
    }
}
