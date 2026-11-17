using NexusIsland.Services.Windows.Interop;

namespace NexusIsland.Services.Windows.Input;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = NativeMethods.MOD_ALT,
    Control = NativeMethods.MOD_CONTROL,
    Shift = NativeMethods.MOD_SHIFT,
    Win = NativeMethods.MOD_WIN,
}

/// <summary>
/// Atajo de teclado global (funciona aunque otra app tenga el foco) con RegisterHotKey.
/// Limitación de Windows: si otra app ya registró la misma combinación, el registro falla
/// (Register devuelve false) y hay que elegir otra.
/// </summary>
public sealed class GlobalHotkey : IDisposable
{
    public const int WM_HOTKEY = NativeMethods.WM_HOTKEY;

    private readonly IntPtr _hwnd;
    private bool _registered;

    public GlobalHotkey(IntPtr hwnd, int id)
    {
        _hwnd = hwnd;
        Id = id;
    }

    public int Id { get; }

    /// <param name="virtualKey">Código de tecla virtual (por ejemplo 0x4E = N).</param>
    public bool Register(HotkeyModifiers modifiers, uint virtualKey)
    {
        Unregister();
        _registered = NativeMethods.RegisterHotKey(_hwnd, Id, (uint)modifiers | NativeMethods.MOD_NOREPEAT, virtualKey);
        return _registered;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_hwnd, Id);
        _registered = false;
    }

    /// <summary>True si el mensaje de ventana corresponde a este atajo.</summary>
    public bool Matches(int msg, IntPtr wParam) => msg == WM_HOTKEY && wParam.ToInt32() == Id;

    public void Dispose() => Unregister();
}
