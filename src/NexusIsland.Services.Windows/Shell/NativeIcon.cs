using NexusIsland.Services.Windows.Interop;

namespace NexusIsland.Services.Windows.Shell;

/// <summary>Ayuda para liberar íconos nativos (HICON) creados con Bitmap.GetHicon().</summary>
public static class NativeIcon
{
    public static void Destroy(IntPtr hIcon)
    {
        if (hIcon != IntPtr.Zero) NativeMethods.DestroyIcon(hIcon);
    }
}
