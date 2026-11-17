using System.Text.RegularExpressions;

namespace NexusIsland.Core.Translation;

/// <summary>Detección de idioma (inglés o español) y división del texto en partes que el modelo traduce bien.</summary>
public static partial class TranslationText
{
    /// <summary>Máximo de caracteres que se traducen (un par de párrafos).</summary>
    public const int MaxLength = 2000;

    /// <summary>Largo máximo de una parte: el modelo trabaja hasta 512 piezas y rinde mejor con frases cortas.</summary>
    public const int MaxChunk = 400;

    private static readonly HashSet<string> SpanishWords =
    [
        "el", "la", "los", "las", "de", "que", "y", "en", "un", "una", "es", "por", "con", "para", "no", "se", "del", "al",
        "lo", "como", "pero", "más", "me", "te", "si", "ya", "muy", "hay", "está", "son", "su", "le", "mi", "tu", "qué",
        "cuando", "porque", "también", "este", "esta", "eso", "hola", "gracias", "bien", "vos", "che",
    ];

    private static readonly HashSet<string> EnglishWords =
    [
        "the", "and", "of", "to", "a", "in", "is", "it", "you", "that", "for", "on", "with", "as", "are", "be", "this",
        "have", "at", "or", "by", "i", "i'm", "we", "your", "can", "will", "if", "please", "was", "what", "my", "not",
        "do", "don't", "it's", "hello", "thanks", "from", "they", "there", "about",
    ];

    /// <summary>
    /// Adivina si el texto está en inglés (→ se traduce al español) o en español (→ al inglés), con palabras
    /// frecuentes y letras propias del español. Ante la duda, inglés (lo más común al copiar de la web).
    /// </summary>
    public static TranslationDirection Detect(string text)
    {
        var lower = text.ToLowerInvariant();
        double es = 0, en = 0;
        foreach (Match m in Word().Matches(lower))
        {
            if (SpanishWords.Contains(m.Value)) es++;
            if (EnglishWords.Contains(m.Value)) en++;
        }
        es += SpanishLetters().Count(lower) * 0.7;
        return es > en ? TranslationDirection.SpanishToEnglish : TranslationDirection.EnglishToSpanish;
    }

    /// <summary>Texto listo para traducir: sin espacios sobrantes y recortado a <see cref="MaxLength"/>.</summary>
    public static string Prepare(string text, out bool truncated)
    {
        var t = text.Replace("\r\n", "\n").Trim();
        truncated = t.Length > MaxLength;
        return truncated ? t[..MaxLength] : t;
    }

    /// <summary>
    /// Divide en líneas y, dentro de cada línea, en oraciones; las oraciones muy largas se cortan en un espacio.
    /// Las líneas vacías se conservan (como "") para respetar los párrafos al volver a armar el texto.
    /// </summary>
    public static IReadOnlyList<IReadOnlyList<string>> Split(string text)
    {
        var lines = new List<IReadOnlyList<string>>();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            var parts = new List<string>();
            foreach (var sentence in SentenceEnd().Split(line.Trim()))
            {
                var s = sentence.Trim();
                while (s.Length > MaxChunk)
                {
                    var cut = s.LastIndexOf(' ', MaxChunk);
                    if (cut < MaxChunk / 2) cut = MaxChunk;
                    parts.Add(s[..cut].Trim());
                    s = s[cut..].Trim();
                }
                if (s.Length > 0) parts.Add(s);
            }
            lines.Add(parts);
        }
        return lines;
    }

    /// <summary>Vuelve a armar el texto: oraciones separadas por espacio, líneas por salto de línea.</summary>
    public static string Join(IEnumerable<IEnumerable<string>> lines) =>
        string.Join("\n", lines.Select(l => string.Join(" ", l))).Trim();

    [GeneratedRegex(@"[\p{L}']+")]
    private static partial Regex Word();

    [GeneratedRegex("[áéíóúñ¿¡]")]
    private static partial Regex SpanishLetters();

    [GeneratedRegex(@"(?<=[.!?…])\s+")]
    private static partial Regex SentenceEnd();
}
