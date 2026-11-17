using System.Globalization;

namespace NexusIsland.Core.Settings;

/// <summary>
/// Idioma de la interfaz. Los textos existen en español e inglés; cualquier otro idioma de Windows
/// cae en inglés (el idioma de respaldo de los recursos).
/// </summary>
public static class AppLanguage
{
    public const string Auto = "auto";
    public const string Spanish = "es";
    public const string English = "en";

    /// <summary>Valor válido para guardar: "auto", "es" o "en" (cualquier otra cosa → "auto").</summary>
    public static string Normalize(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        Spanish => Spanish,
        English => English,
        _ => Auto,
    };

    /// <summary>
    /// Cultura a forzar para la interfaz, o null para dejar la de Windows. Si Windows ya está en el idioma
    /// elegido (p. ej. es-AR y se eligió "es"), devuelve null para conservar la variante regional.
    /// </summary>
    public static CultureInfo? Resolve(string? setting, CultureInfo windowsUiCulture)
    {
        var lang = Normalize(setting);
        if (lang == Auto) return null;
        if (string.Equals(windowsUiCulture.TwoLetterISOLanguageName, lang, StringComparison.OrdinalIgnoreCase)) return null;
        return CultureInfo.GetCultureInfo(lang);
    }
}
