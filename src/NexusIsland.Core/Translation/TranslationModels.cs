namespace NexusIsland.Core.Translation;

/// <summary>Sentido de la traducción. Solo inglés ↔ español.</summary>
public enum TranslationDirection
{
    EnglishToSpanish,
    SpanishToEnglish,
}

public static class TranslationDirections
{
    public static TranslationDirection Reverse(this TranslationDirection d) =>
        d == TranslationDirection.EnglishToSpanish ? TranslationDirection.SpanishToEnglish : TranslationDirection.EnglishToSpanish;

    /// <summary>"en-es" / "es-en" (nombre de la carpeta del modelo).</summary>
    public static string Code(this TranslationDirection d) =>
        d == TranslationDirection.EnglishToSpanish ? "en-es" : "es-en";
}

/// <summary>Un archivo del modelo, con su tamaño y huella SHA-256 esperados (se verifican al bajarlo).</summary>
public sealed record ModelFile(string Name, long Size, string Sha256);

/// <summary>
/// Modelo de traducción Opus-MT (Helsinki-NLP, licencia CC BY 4.0) convertido a ONNX por Xenova,
/// en versión comprimida (int8). Se baja de Hugging Face fijando la revisión: si alguien cambiara
/// los archivos en el repositorio de origen, la huella no coincidiría y la descarga se rechaza.
/// </summary>
public sealed record TranslationModel(TranslationDirection Direction, string Repository, string Revision, IReadOnlyList<ModelFile> Files)
{
    public long TotalBytes => Files.Sum(f => f.Size);

    public Uri UrlOf(ModelFile file) => new($"https://huggingface.co/{Repository}/resolve/{Revision}/{FilePathInRepo(file.Name)}");

    private static string FilePathInRepo(string name) => name.EndsWith(".onnx", StringComparison.Ordinal) ? "onnx/" + name : name;
}

public static class TranslationModels
{
    public const string EncoderFile = "encoder_model_quantized.onnx";
    public const string DecoderFile = "decoder_model_merged_quantized.onnx";

    public static readonly TranslationModel EnglishToSpanish = new(
        TranslationDirection.EnglishToSpanish, "Xenova/opus-mt-en-es", "4b002a4c7edd54a7ced58877258b87f7efd3f892",
        [
            new("config.json", 1468, "499eae1f97eeba63172fc884f92fd727ae803b7ceb79c0f653aa4b011152af2f"),
            new("vocab.json", 1720044, "b074b4cca0036ade5a39ea97faabd534e1015482c480fc2cb02c6481983eb163"),
            new("source.spm", 801636, "4dd547c24816a335e7b0b2e63376a8f1b3cbfc671eda5ab808dd44fdadaa8791"),
            new(EncoderFile, 52899742, "03434edd1147e6c49e22066e9dc5888235e99cc35a6711d1c4ea5b06455d5da9"),
            new(DecoderFile, 60212804, "58ee5ddd6e22d1693d8722b90c1486afb93700a4376cf28fd74175052ad530ae"),
        ]);

    public static readonly TranslationModel SpanishToEnglish = new(
        TranslationDirection.SpanishToEnglish, "Xenova/opus-mt-es-en", "eadfd7c658a9d8929ac3b8e996b68a68e2c7d480",
        [
            new("config.json", 1433, "fab3a7f93185bc5aa7b419f6a1e6e74d98c8f2a506c94493d3019bf46da3478d"),
            new("vocab.json", 1720044, "b074b4cca0036ade5a39ea97faabd534e1015482c480fc2cb02c6481983eb163"),
            new("source.spm", 825924, "e236ee6d866b635c0142114f8647f39831f9d92534aa2aad75c942f6a78ad0e3"),
            new(EncoderFile, 52899742, "c01e70f8455efc350831aa2af7ed187b16829241f0018ba07d1ba643a391bc18"),
            new(DecoderFile, 60212804, "4cd91ab30240d295ce907b5100826031838c672005e57e524d711122d75605fa"),
        ]);

    public static TranslationModel For(TranslationDirection d) =>
        d == TranslationDirection.EnglishToSpanish ? EnglishToSpanish : SpanishToEnglish;
}
