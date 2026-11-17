using System.Runtime.InteropServices;
using System.Text;

namespace NexusIsland.Services.Windows.Notifications;

/// <summary>
/// ¿Corre la app con identidad de paquete? (necesaria para leer notificaciones de otras apps).
/// Con un "paquete con ubicación externa" registrado, el mismo .exe obtiene identidad al abrirse.
/// </summary>
public static class PackageIdentity
{
    private const int APPMODEL_ERROR_NO_PACKAGE = 15700;
    private const int ERROR_INSUFFICIENT_BUFFER = 122;

    public static bool HasIdentity => FullName is not null;

    /// <summary>Nombre completo del paquete (p. ej. "NexusIsland_0.7.0.0_x64__abc123"), o null.</summary>
    public static string? FullName
    {
        get
        {
            var length = 0;
            var result = GetCurrentPackageFullName(ref length, null);
            if (result == APPMODEL_ERROR_NO_PACKAGE || result != ERROR_INSUFFICIENT_BUFFER) return null;
            var buffer = new StringBuilder(length);
            return GetCurrentPackageFullName(ref length, buffer) == 0 ? buffer.ToString() : null;
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int length, StringBuilder? fullName);
}
