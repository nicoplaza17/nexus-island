using System.Security.Cryptography;

namespace NexusIsland.Core.Translation;

/// <summary>
/// Dónde quedan los modelos bajados (%LocalAppData%\NexusIsland\models\opus-mt-en-es, ...) y su descarga.
/// La descarga es lo único de la isla que usa Internet, y solo se hace con permiso del usuario.
/// </summary>
public sealed class ModelStore(string root, HttpClient http)
{
    public string Root { get; } = root;

    public string DirectoryOf(TranslationDirection d) => Path.Combine(Root, "opus-mt-" + d.Code());

    /// <summary>True si están todos los archivos con el tamaño correcto (la huella se verificó al bajarlos).</summary>
    public bool IsInstalled(TranslationDirection d)
    {
        var dir = DirectoryOf(d);
        return TranslationModels.For(d).Files.All(f =>
        {
            var info = new FileInfo(Path.Combine(dir, f.Name));
            return info.Exists && info.Length == f.Size;
        });
    }

    /// <summary>Espacio que ocupan los modelos bajados.</summary>
    public long InstalledBytes() =>
        Directory.Exists(Root) ? new DirectoryInfo(Root).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length) : 0;

    public void DeleteAll()
    {
        if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
    }

    /// <summary>
    /// Baja el modelo archivo por archivo a ".part", verifica tamaño y SHA-256, y recién ahí lo deja con su nombre.
    /// Si algo falla (o se cancela), no queda ningún archivo a medias. <paramref name="progress"/> va de 0 a 1.
    /// </summary>
    public async Task DownloadAsync(TranslationDirection d, IProgress<double>? progress, CancellationToken ct)
    {
        var model = TranslationModels.For(d);
        var dir = DirectoryOf(d);
        Directory.CreateDirectory(dir);
        long done = 0;
        var buffer = new byte[81920];

        foreach (var file in model.Files)
        {
            var target = Path.Combine(dir, file.Name);
            if (new FileInfo(target) is { Exists: true } existing && existing.Length == file.Size)
            {
                done += file.Size;
                progress?.Report((double)done / model.TotalBytes);
                continue;
            }

            var part = target + ".part";
            try
            {
                using var response = await http.GetAsync(model.UrlOf(file), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                long written = 0;
                await using (var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
                await using (var output = new FileStream(part, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
                {
                    int read;
                    while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                    {
                        written += read;
                        if (written > file.Size) throw new InvalidDataException($"{file.Name}: más grande de lo esperado");
                        sha.AppendData(buffer, 0, read);
                        await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
                        progress?.Report((double)(done + written) / model.TotalBytes);
                    }
                }

                var hash = Convert.ToHexStringLower(sha.GetHashAndReset());
                if (written != file.Size || hash != file.Sha256)
                    throw new InvalidDataException($"{file.Name}: la huella no coincide (archivo dañado o modificado)");
                File.Move(part, target, overwrite: true);
                done += file.Size;
            }
            catch
            {
                TryDelete(part);
                throw;
            }
        }
        progress?.Report(1);
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}
