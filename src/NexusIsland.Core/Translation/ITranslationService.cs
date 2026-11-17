namespace NexusIsland.Core.Translation;

/// <summary>Traducción local inglés ↔ español (sin Internet una vez bajado el modelo).</summary>
public interface ITranslationService
{
    bool IsModelInstalled(TranslationDirection direction);

    /// <summary>Lo que pesa bajar el modelo de esa dirección.</summary>
    long DownloadSize(TranslationDirection direction);

    /// <summary>Baja y verifica el modelo. <paramref name="progress"/> va de 0 a 1 (puede llegar en otro hilo).</summary>
    Task DownloadModelAsync(TranslationDirection direction, IProgress<double>? progress, CancellationToken cancellationToken);

    /// <summary>Traduce. El texto nunca sale de la PC ni se escribe en el log.</summary>
    Task<string> TranslateAsync(string text, TranslationDirection direction, CancellationToken cancellationToken);

    /// <summary>Espacio que ocupan los modelos bajados.</summary>
    long InstalledBytes();

    /// <summary>Borra los modelos bajados (se vuelven a pedir al traducir).</summary>
    void DeleteModels();
}

/// <summary>Mensajes entre la isla y el proceso traductor (una línea JSON por mensaje).</summary>
public sealed record TranslatorRequest(int Id, string ModelDirectory, string[] Sentences);

public sealed record TranslatorResponse(int Id, string[]? Translations, string? Error);
