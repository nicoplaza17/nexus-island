using System.Runtime.InteropServices;

namespace NexusIsland.Services.Windows.Shell;

/// <summary>
/// Nombre visible de una app a partir de su AppUserModelId, preguntándole a Windows (la carpeta
/// "Aplicaciones" del Inicio: shell:AppsFolder). Sirve para las apps web instaladas desde Chrome o Edge
/// ("Chrome._crx_pjibgc…" → "Spotify"), que no son apps de la Store y no tienen otra forma de resolverse.
/// </summary>
public static class AppDisplayNames
{
    private const uint SIGDN_NORMALDISPLAY = 0;
    private static readonly Guid IID_IShellItem = new("43826d1e-e718-42ee-bc55-a1e261c37bfe");

    public static bool TryGet(string appUserModelId, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(appUserModelId)) return false;
        IShellItem? item = null;
        var buffer = IntPtr.Zero;
        try
        {
            var iid = IID_IShellItem;
            if (SHCreateItemFromParsingName(@"shell:AppsFolder\" + appUserModelId, IntPtr.Zero, ref iid, out item) != 0 || item is null)
                return false;
            if (item.GetDisplayName(SIGDN_NORMALDISPLAY, out buffer) != 0 || buffer == IntPtr.Zero) return false;
            var display = Marshal.PtrToStringUni(buffer);
            // Si Windows no la conoce, a veces devuelve el mismo identificador: eso no es un nombre.
            if (string.IsNullOrWhiteSpace(display) || display.Equals(appUserModelId, StringComparison.OrdinalIgnoreCase)) return false;
            name = display.Trim();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            if (buffer != IntPtr.Zero) Marshal.FreeCoTaskMem(buffer);
            if (item is not null) Marshal.ReleaseComObject(item);
        }
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHCreateItemFromParsingName(string path, IntPtr bindContext, ref Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItem? item);

    [ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        [PreserveSig] int BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
        [PreserveSig] int GetParent(out IShellItem ppsi);
        [PreserveSig] int GetDisplayName(uint sigdnName, out IntPtr ppszName);
        [PreserveSig] int GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
        [PreserveSig] int Compare(IShellItem psi, uint hint, out int piOrder);
    }
}
