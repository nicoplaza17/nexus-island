using NexusIsland.Services.Windows.Display;
using NexusIsland.Services.Windows.Interop;

namespace NexusIsland.Services.Windows.Shell;

/// <summary>
/// Detecta si la app en primer plano está en pantalla completa en un monitor dado
/// (juegos, videos, presentaciones, navegador con F11), para que la isla se aparte.
///
/// Método: la ventana activa cubre el monitor entero y no es una ventana maximizada común
/// (una maximizada con la barra de tareas en "ocultar automáticamente" también cubre la pantalla,
/// pero no es pantalla completa). Limitación: los juegos en pantalla completa "exclusiva"
/// tapan cualquier ventana igual; esta detección es para el resto.
/// </summary>
public static class FullscreenDetector
{
    // Escritorio y barra de tareas: nunca cuentan como "app en pantalla completa".
    private static readonly HashSet<string> ShellClasses = new(StringComparer.Ordinal)
    {
        "Progman", "WorkerW", "Shell_TrayWnd", "Shell_SecondaryTrayWnd",
    };

    public static bool IsFullscreenOnMonitor(IntPtr monitor, IntPtr ownWindow)
    {
        var foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero || foreground == ownWindow) return false;
        if (NativeMethods.MonitorFromWindow(foreground, NativeMethods.MONITOR_DEFAULTTONEAREST) != monitor) return false;
        if (NativeMethods.IsZoomed(foreground)) return false;
        if (ShellClasses.Contains(NativeMethods.GetClassName(foreground))) return false;

        var window = DisplayService.GetWindowRect(foreground);
        var bounds = DisplayService.GetMonitor(monitor).Bounds;
        return window.Left <= bounds.Left && window.Top <= bounds.Top
            && window.Right >= bounds.Right && window.Bottom >= bounds.Bottom;
    }

    public static IntPtr GetForegroundWindow() => NativeMethods.GetForegroundWindow();
}
