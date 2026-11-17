namespace NexusIsland.Core.Storage;

/// <summary>Escritura segura de archivos: un corte a mitad de camino nunca deja el archivo a medias.</summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temp = path + ".tmp";
        File.WriteAllText(temp, contents);
        File.Move(temp, path, overwrite: true); // reemplazo atómico en el mismo disco
    }
}
