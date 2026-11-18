using NexusIsland.Core.Properties;

namespace NexusIsland.Core.Media;

/// <summary>
/// Traduce el identificador técnico de una app (AppUserModelId) a un nombre legible.
/// Windows no da un nombre "lindo" para apps de escritorio, por eso hay una tabla de apps conocidas.
/// </summary>
public static class SourceAppNames
{
    // Se busca "contiene" sin distinguir mayúsculas, en este orden.
    // Se arma en cada consulta (no una sola vez) para que los nombres sigan el idioma actual.
    private static (string Fragment, string Name)[] Known =>
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
        ("ZuneMusic", Strings.App_MediaPlayer),
        ("ZuneVideo", Strings.App_MoviesTv),
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

    /// <summary>App web instalada desde un navegador (PWA): "Chrome._crx_&lt;id&gt;", "MSEdge._crx_&lt;id&gt;".</summary>
    public static bool IsBrowserWebApp(string appUserModelId) =>
        appUserModelId.Contains("_crx_", StringComparison.OrdinalIgnoreCase);

    public static bool TryGetKnown(string appUserModelId, out string name)
    {
        // Apps web (PWA) cuyo nombre Windows no pudo dar (ver AppDisplayNames): se indica de dónde vienen.
        if (IsBrowserWebApp(appUserModelId))
        {
            name = appUserModelId.StartsWith("MSEdge", StringComparison.OrdinalIgnoreCase)
                ? Strings.App_WebEdge : Strings.App_WebChrome;
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
        if (string.IsNullOrWhiteSpace(appUserModelId)) return Strings.Media_Generic;
        var name = appUserModelId;
        var bang = name.LastIndexOf('!');
        if (bang >= 0 && bang < name.Length - 1) name = name[(bang + 1)..];
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        var slash = name.LastIndexOfAny(['\\', '/']);
        if (slash >= 0) name = name[(slash + 1)..];
        return name.Length > 0 ? char.ToUpperInvariant(name[0]) + name[1..] : Strings.Media_Generic;
    }
}
