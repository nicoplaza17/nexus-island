using NexusIsland.Core.Geometry;
using NexusIsland.Services.Windows.Interop;

namespace NexusIsland.Services.Windows.Display;

/// <summary>Datos de un monitor en píxeles físicos.</summary>
public readonly record struct MonitorInfo(IntPtr Handle, string DeviceName, PixelRect Bounds, PixelRect WorkArea,
    bool IsPrimary, double Scale)
{
    public MonitorDescriptor ToDescriptor() => new(DeviceName, Bounds, WorkArea, IsPrimary, Scale);
}

/// <summary>
/// Monitores, posición de ventanas y cursor, siempre en píxeles físicos. Trabajar en píxeles físicos
/// (y no en las unidades de WPF) evita errores cuando los monitores tienen distinta escala.
/// </summary>
public static class DisplayService
{
    private const uint MONITORINFOF_PRIMARY = 1;

    public static MonitorInfo GetPrimaryMonitor() =>
        GetMonitor(NativeMethods.MonitorFromPoint(default, NativeMethods.MONITOR_DEFAULTTOPRIMARY));

    public static MonitorInfo GetMonitorForWindow(IntPtr hwnd) =>
        GetMonitor(NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST));

    public static MonitorInfo GetMonitorForPoint(PixelPoint point) =>
        GetMonitor(NativeMethods.MonitorFromPoint(new NativeMethods.POINT { X = point.X, Y = point.Y },
            NativeMethods.MONITOR_DEFAULTTONEAREST));

    public static unsafe MonitorInfo GetMonitor(IntPtr hMonitor)
    {
        var info = new NativeMethods.MONITORINFOEX { cbSize = sizeof(NativeMethods.MONITORINFOEX) };
        if (hMonitor == IntPtr.Zero || !NativeMethods.GetMonitorInfoEx(hMonitor, ref info))
            throw new InvalidOperationException("No se pudo obtener la información del monitor.");
        var device = new string(info.szDevice);
        var scale = NativeMethods.GetDpiForMonitor(hMonitor, 0, out var dpiX, out _) == 0 && dpiX > 0 ? dpiX / 96.0 : 1.0;
        return new MonitorInfo(hMonitor, device, ToRect(info.rcMonitor), ToRect(info.rcWork),
            (info.dwFlags & MONITORINFOF_PRIMARY) != 0, scale);
    }

    /// <summary>Todos los monitores conectados, de izquierda a derecha.</summary>
    public static IReadOnlyList<MonitorInfo> GetMonitors()
    {
        var handles = new List<IntPtr>();
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (h, _, _, _) => { handles.Add(h); return true; }, IntPtr.Zero);
        return handles.Select(GetMonitor).OrderBy(m => m.Bounds.Left).ThenBy(m => m.Bounds.Top).ToList();
    }

    public static PixelRect GetWindowRect(IntPtr hwnd) =>
        NativeMethods.GetWindowRect(hwnd, out var r) ? ToRect(r) : default;

    /// <summary>Mueve la ventana (sin cambiar tamaño, orden ni foco).</summary>
    public static void MoveWindow(IntPtr hwnd, PixelPoint position) =>
        NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, position.X, position.Y, 0, 0,
            NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

    public static PixelPoint GetCursorPosition() =>
        NativeMethods.GetCursorPos(out var p) ? new PixelPoint(p.X, p.Y) : default;

    /// <summary>Escala del monitor donde está la ventana (1,0 = 100 %, 1,25 = 125 %...).</summary>
    public static double GetScaleForWindow(IntPtr hwnd)
    {
        var dpi = NativeMethods.GetDpiForWindow(hwnd);
        return dpi == 0 ? 1.0 : dpi / 96.0;
    }

    private static PixelRect ToRect(NativeMethods.RECT r) => new(r.Left, r.Top, r.Right, r.Bottom);
}
