using Microsoft.Win32;

namespace NexusIsland.Services.Windows.Startup;

/// <summary>Estado del inicio automático con Windows.</summary>
public enum StartupState
{
    Disabled,
    Enabled,

    /// <summary>Registrado, pero el usuario lo desactivó desde el Administrador de tareas / Configuración.</summary>
    DisabledByUser,
}

/// <summary>
/// Inicio con Windows mediante la clave "Run" del usuario actual
/// (HKCU\Software\Microsoft\Windows\CurrentVersion\Run). No requiere permisos de administrador.
///
/// Windows guarda aparte si el usuario desactivó una app de inicio desde el Administrador de tareas
/// (clave StartupApproved). Esa decisión es del usuario: se detecta y se informa, pero no se pisa.
/// (Si la app se distribuye empaquetada como MSIX, esto se reemplaza por StartupTask.)
/// </summary>
public static class StartupRegistration
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ValueName = "NexusIsland";

    public static string CurrentExecutable => Environment.ProcessPath
        ?? throw new InvalidOperationException("No se pudo determinar la ruta del ejecutable.");

    public static StartupState GetState()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        if (run?.GetValue(ValueName) is not string) return StartupState.Disabled;

        // StartupApproved: primer byte par (02) = habilitado, impar (03) = deshabilitado por el usuario.
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedKey);
        if (approved?.GetValue(ValueName) is byte[] { Length: > 0 } flags && (flags[0] & 1) == 1)
            return StartupState.DisabledByUser;

        return StartupState.Enabled;
    }

    /// <summary>True si está registrado pero apuntando a otro ejecutable (por ejemplo, se movió la carpeta).</summary>
    public static bool PointsElsewhere()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey);
        return run?.GetValue(ValueName) is string command
            && !string.Equals(command, Quote(CurrentExecutable), StringComparison.OrdinalIgnoreCase);
    }

    public static void Enable()
    {
        using var run = Registry.CurrentUser.CreateSubKey(RunKey, writable: true);
        run.SetValue(ValueName, Quote(CurrentExecutable), RegistryValueKind.String);
    }

    public static void Disable()
    {
        using var run = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
        run?.DeleteValue(ValueName, throwOnMissingValue: false);
    }

    private static string Quote(string path) => $"\"{path}\"";
}
