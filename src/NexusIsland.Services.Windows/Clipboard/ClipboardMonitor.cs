using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Tools;

namespace NexusIsland.Services.Windows.Clipboard;

/// <summary>
/// Escucha el portapapeles con AddClipboardFormatListener (API oficial de Windows) usando una
/// ventana invisible "solo mensajes". Lee únicamente texto (CF_UNICODETEXT).
///
/// Privacidad: respeta los formatos con los que las apps piden NO ser registradas
/// (los mismos que respeta el historial de Windows, Win+V):
///  - "ExcludeClipboardContentFromMonitorProcessing" (gestores de contraseñas como KeePass, 1Password, Bitwarden).
///  - "CanIncludeInClipboardHistory" con valor 0.
/// </summary>
public sealed class ClipboardMonitor : IClipboardMonitor
{
    private const int WM_CLIPBOARDUPDATE = 0x031D;
    private const uint CF_UNICODETEXT = 13;
    private const uint GMEM_MOVEABLE = 0x0002;
    private static readonly IntPtr HWND_MESSAGE = new(-3);

    private readonly ILogger<ClipboardMonitor> _logger;
    private readonly uint _excludeFormat = RegisterClipboardFormat("ExcludeClipboardContentFromMonitorProcessing");
    private readonly uint _historyFormat = RegisterClipboardFormat("CanIncludeInClipboardHistory");
    private WndProc? _wndProc; // se guarda la referencia: si el GC la junta, Windows llamaría a memoria inválida
    private IntPtr _hwnd;
    private string? _lastText;
    private string? _ownText; // lo que pusimos nosotros (se acepta igual, pero no se loguea como externo)

    public ClipboardMonitor(ILogger<ClipboardMonitor> logger) => _logger = logger;

    public event Action<string>? TextCopied;

    public void Start()
    {
        if (_hwnd != IntPtr.Zero) return;
        _wndProc = WindowProcedure;
        var className = "NexusIsland.ClipboardListener." + Environment.ProcessId;
        var wc = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProc),
            hInstance = GetModuleHandle(null),
            lpszClassName = className,
        };
        if (RegisterClassEx(ref wc) == 0) throw new Win32Exception(Marshal.GetLastWin32Error());

        _hwnd = CreateWindowEx(0, className, "", 0, 0, 0, 0, 0, HWND_MESSAGE, IntPtr.Zero, wc.hInstance, IntPtr.Zero);
        if (_hwnd == IntPtr.Zero) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (!AddClipboardFormatListener(_hwnd)) throw new Win32Exception(Marshal.GetLastWin32Error());
        _logger.LogInformation("Escuchando el portapapeles (solo texto, historial solo en memoria)");
    }

    public void Stop()
    {
        if (_hwnd == IntPtr.Zero) return;
        RemoveClipboardFormatListener(_hwnd);
        DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    public bool TrySetText(string text)
    {
        if (!TryOpenClipboard()) return false;
        try
        {
            EmptyClipboard();
            var bytes = (text.Length + 1) * 2;
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE, (UIntPtr)bytes);
            if (hGlobal == IntPtr.Zero) return false;
            var target = GlobalLock(hGlobal);
            Marshal.Copy(text.ToCharArray(), 0, target, text.Length);
            Marshal.WriteInt16(target, text.Length * 2, 0);
            GlobalUnlock(hGlobal);
            if (SetClipboardData(CF_UNICODETEXT, hGlobal) == IntPtr.Zero)
            {
                GlobalFree(hGlobal);
                return false;
            }
            _ownText = text; // a partir de acá, el portapapeles es dueño de la memoria
            return true;
        }
        finally
        {
            CloseClipboard();
        }
    }

    private IntPtr WindowProcedure(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        if (msg == WM_CLIPBOARDUPDATE)
        {
            // No se lee en el acto: la app que copió puede seguir escribiendo (varios formatos, o dos copias
            // seguidas) y si abrimos el portapapeles justo ahí, a ella le falla. Se espera un instante.
            SetTimer(hwnd, ReadTimerId, ReadDelayMs, IntPtr.Zero);
            return IntPtr.Zero;
        }
        if (msg == WM_TIMER && wParam == ReadTimerId)
        {
            KillTimer(hwnd, ReadTimerId);
            try { OnClipboardUpdate(); }
            catch (Exception ex) { _logger.LogDebug(ex, "No se pudo leer el portapapeles"); }
            return IntPtr.Zero;
        }
        return DefWindowProc(hwnd, msg, wParam, lParam);
    }

    private const uint WM_TIMER = 0x0113;
    private static readonly IntPtr ReadTimerId = new(1);
    private const uint ReadDelayMs = 60;

    [DllImport("user32.dll")] private static extern UIntPtr SetTimer(IntPtr hwnd, IntPtr id, uint ms, IntPtr callback);
    [DllImport("user32.dll")] private static extern bool KillTimer(IntPtr hwnd, IntPtr id);

    private void OnClipboardUpdate()
    {
        if (!IsClipboardFormatAvailable(CF_UNICODETEXT)) return;
        if (_excludeFormat != 0 && IsClipboardFormatAvailable(_excludeFormat))
        {
            _logger.LogDebug("Contenido marcado como privado (gestor de contraseñas): no se registra");
            return;
        }
        if (!TryOpenClipboard()) return;

        string? text;
        try
        {
            if (_historyFormat != 0 && IsClipboardFormatAvailable(_historyFormat) && ReadDword(_historyFormat) == 0)
            {
                _logger.LogDebug("Contenido excluido del historial por la app de origen: no se registra");
                return;
            }
            text = ReadUnicodeText();
        }
        finally
        {
            CloseClipboard();
        }

        if (string.IsNullOrEmpty(text) || text == _lastText) return; // algunas apps avisan dos veces
        _lastText = text;
        if (text != _ownText) _logger.LogDebug("Texto copiado ({Length} caracteres)", text.Length); // nunca se loguea el contenido
        TextCopied?.Invoke(text);
    }

    /// <summary>El portapapeles puede estar bloqueado un instante por otra app: se reintenta.</summary>
    private bool TryOpenClipboard()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if (OpenClipboard(_hwnd)) return true;
            Thread.Sleep(15);
        }
        _logger.LogDebug("Portapapeles ocupado por otra app");
        return false;
    }

    private static string? ReadUnicodeText()
    {
        var handle = GetClipboardData(CF_UNICODETEXT);
        if (handle == IntPtr.Zero) return null;
        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero) return null;
        try
        {
            var maxChars = (int)Math.Min((long)GlobalSize(handle) / 2, ClipboardHistory.MaxTextLength + 1);
            var text = Marshal.PtrToStringUni(pointer, maxChars);
            var nul = text.IndexOf('\0');
            return nul >= 0 ? text[..nul] : text;
        }
        finally
        {
            GlobalUnlock(handle);
        }
    }

    private static int ReadDword(uint format)
    {
        var handle = GetClipboardData(format);
        if (handle == IntPtr.Zero) return -1;
        var pointer = GlobalLock(handle);
        if (pointer == IntPtr.Zero) return -1;
        try { return (long)GlobalSize(handle) >= 4 ? Marshal.ReadInt32(pointer) : -1; }
        finally { GlobalUnlock(handle); }
    }

    public void Dispose() => Stop();

    // ------------------------------------------------------------------ Win32

    private delegate IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize, style;
        public IntPtr lpfnWndProc;
        public int cbClsExtra, cbWndExtra;
        public IntPtr hInstance, hIcon, hCursor, hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public IntPtr hIconSm;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX wc);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(uint exStyle, string className, string windowName, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr DefWindowProc(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr GetModuleHandle(string? name);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool AddClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterClipboardFormat(string name);
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool OpenClipboard(IntPtr owner);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format, IntPtr data);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags, UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr mem);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr mem);
}
