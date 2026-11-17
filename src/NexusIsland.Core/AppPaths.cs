namespace NexusIsland.Core;

/// <summary>
/// Carpetas donde la app guarda sus datos. Se crean si no existen.
/// </summary>
public static class AppPaths
{
    private const string AppFolderName = "NexusIsland";

    /// <summary>%AppData%\NexusIsland — configuración del usuario (viaja con perfiles móviles).</summary>
    public static string RoamingData { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), AppFolderName));

    /// <summary>%LocalAppData%\NexusIsland — datos locales de esta PC (caché, logs).</summary>
    public static string LocalData { get; } = Ensure(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), AppFolderName));

    /// <summary>%LocalAppData%\NexusIsland\logs</summary>
    public static string Logs { get; } = Ensure(Path.Combine(LocalData, "logs"));

    private static string Ensure(string path)
    {
        Directory.CreateDirectory(path);
        return path;
    }
}
