using System.Text.Json;
using NexusIsland.Core.Translation;

namespace NexusIsland.Translation;

/// <summary>
/// El proceso traductor: lee pedidos (una línea JSON) por la entrada estándar y responde por la salida
/// estándar. Mantiene cargado un solo modelo (cambiar de dirección libera el anterior). Termina cuando la
/// isla cierra la entrada (por inactividad o al salir). Nunca escribe textos en ningún lado más que la respuesta.
/// </summary>
public static class TranslatorHost
{
    public static int Run(TextReader input, TextWriter output)
    {
        MarianTranslator? model = null;
        string? loadedDirectory = null;
        try
        {
            string? line;
            while ((line = input.ReadLine()) is not null)
            {
                if (line.Length == 0) continue;
                TranslatorResponse response;
                TranslatorRequest? request = null;
                try
                {
                    request = JsonSerializer.Deserialize<TranslatorRequest>(line) ?? throw new InvalidDataException("pedido vacío");
                    if (!string.Equals(loadedDirectory, request.ModelDirectory, StringComparison.OrdinalIgnoreCase))
                    {
                        model?.Dispose();
                        model = null;
                        GC.Collect();
                        model = new MarianTranslator(request.ModelDirectory);
                        loadedDirectory = request.ModelDirectory;
                    }
                    var translations = request.Sentences.Select(s => s.Length == 0 ? "" : model!.Translate(s)).ToArray();
                    response = new TranslatorResponse(request.Id, translations, null);
                }
                catch (Exception ex)
                {
                    // Solo el tipo de error: el mensaje podría incluir partes del texto.
                    response = new TranslatorResponse(request?.Id ?? -1, null, ex.GetType().Name);
                }
                output.WriteLine(JsonSerializer.Serialize(response));
                output.Flush();
            }
            return 0;
        }
        finally
        {
            model?.Dispose();
        }
    }
}
