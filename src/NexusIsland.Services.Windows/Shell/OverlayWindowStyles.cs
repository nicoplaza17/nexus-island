using NexusIsland.Services.Windows.Interop;

namespace NexusIsland.Services.Windows.Shell;

/// <summary>
/// Convierte una ventana común en una "superposición" tipo isla:
///  - no aparece en Alt+Tab ni en la barra de tareas (WS_EX_TOOLWINDOW),
///  - no le roba el foco a la app que estás usando al hacer clic (WS_EX_NOACTIVATE + WM_MOUSEACTIVATE),
///  - queda por encima de las ventanas normales (HWND_TOPMOST).
/// </summary>
public static class OverlayWindowStyles
{
    public static void Apply(IntPtr hwnd)
    {
        var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        ex |= NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE;
        ex &= ~NativeMethods.WS_EX_APPWINDOW;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
        BringToTopmost(hwnd);
    }

    /// <summary>
    /// Modo teclado: permite (o vuelve a impedir) que la isla se active y reciba el foco del teclado.
    /// Sin esto, Windows nunca le da el foco a una ventana WS_EX_NOACTIVATE.
    /// </summary>
    public static void SetActivatable(IntPtr hwnd, bool activatable)
    {
        var ex = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        ex = activatable ? ex & ~NativeMethods.WS_EX_NOACTIVATE : ex | NativeMethods.WS_EX_NOACTIVATE;
        NativeMethods.SetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE, ex);
    }

    /// <summary>
    /// Devuelve el foco a la ventana que lo tenía. Windows solo lo permite si esta app es la activa
    /// (lo es mientras dura el modo teclado); si la ventana ya no existe, no hace nada.
    /// </summary>
    public static void ReturnFocusTo(IntPtr hwnd)
    {
        if (hwnd != IntPtr.Zero && NativeMethods.IsWindow(hwnd)) NativeMethods.SetForegroundWindow(hwnd);
    }

    /// <summary>
    /// Re-afirma el "siempre encima" sin mover, redimensionar ni activar la ventana.
    /// Útil porque otras apps "topmost" pueden quedar por encima.
    /// </summary>
    public static void BringToTopmost(IntPtr hwnd) =>
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
            NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);

    /// <summary>
    /// Procesa WM_MOUSEACTIVATE para que el clic no active la ventana.
    /// Devuelve true si el mensaje fue manejado. Conectar en el hook de mensajes de la ventana.
    /// </summary>
    public static bool TryHandleMouseActivate(int msg, out IntPtr result)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            result = new IntPtr(NativeMethods.MA_NOACTIVATE);
            return true;
        }
        result = IntPtr.Zero;
        return false;
    }
}
