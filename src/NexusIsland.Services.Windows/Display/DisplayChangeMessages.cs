using NexusIsland.Services.Windows.Interop;

namespace NexusIsland.Services.Windows.Display;

/// <summary>Reconoce los mensajes de Windows que indican que cambió la disposición de pantallas.</summary>
public static class DisplayChangeMessages
{
    /// <summary>
    /// True para: cambio de resolución o monitores (WM_DISPLAYCHANGE), cambio de escala (WM_DPICHANGED)
    /// y cambio del área de trabajo, por ejemplo si se mueve la barra de tareas (WM_SETTINGCHANGE + SPI_SETWORKAREA).
    /// </summary>
    public static bool IsDisplayChange(int msg, IntPtr wParam) =>
        msg == NativeMethods.WM_DISPLAYCHANGE
        || msg == NativeMethods.WM_DPICHANGED
        || (msg == NativeMethods.WM_SETTINGCHANGE && wParam.ToInt64() == NativeMethods.SPI_SETWORKAREA);
}
