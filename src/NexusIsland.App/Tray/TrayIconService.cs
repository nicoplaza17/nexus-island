using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using Microsoft.Extensions.Logging;
using NexusIsland.App.Island;
using NexusIsland.App.Properties;
using NexusIsland.Core;
using NexusIsland.Services.Windows.Shell;
using Forms = System.Windows.Forms;

namespace NexusIsland.App.Tray;

/// <summary>
/// Ícono en la bandeja del sistema (junto al reloj) con menú: mostrar/ocultar, logs, salir.
/// WPF no trae ícono de bandeja propio, por eso se usa el NotifyIcon de WinForms.
/// </summary>
public sealed class TrayIconService : IDisposable
{
    private readonly IslandWindow _island;
    private readonly ILogger<TrayIconService> _logger;
    private Forms.NotifyIcon? _notifyIcon;
    private Icon? _icon;
    private Forms.ToolStripMenuItem? _toggleItem;

    public TrayIconService(IslandWindow island, ILogger<TrayIconService> logger)
    {
        _island = island;
        _logger = logger;
    }

    public void Initialize()
    {
        _icon = LoadAppIcon(Forms.SystemInformation.SmallIconSize.Width);

        _toggleItem = new Forms.ToolStripMenuItem(ToggleText(), null, (_, _) => _island.ToggleUserVisibility());
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add(_toggleItem);
        menu.Items.Add(new Forms.ToolStripSeparator());
        var settingsItem = new Forms.ToolStripMenuItem(Strings.Tray_Settings, null, (_, _) => SettingsRequested?.Invoke());
        settingsItem.Font = new Font(settingsItem.Font, FontStyle.Bold); // acción principal (también con doble clic)
        menu.Items.Add(settingsItem);
        menu.Items.Add(new Forms.ToolStripMenuItem(Strings.Tray_Logs, null, (_, _) => OpenLogsFolder()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(new Forms.ToolStripMenuItem(Strings.Tray_Exit, null, (_, _) => System.Windows.Application.Current.Shutdown()));
        menu.Opening += (_, _) => _toggleItem.Text = ToggleText();

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = _icon,
            Text = "Nexus Island",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) _island.ToggleUserVisibility();
        };
        _notifyIcon.MouseDoubleClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) SettingsRequested?.Invoke();
        };

        _logger.LogInformation("Ícono de bandeja inicializado");
    }

    /// <summary>El usuario pidió abrir la configuración.</summary>
    public event Action? SettingsRequested;

    private string ToggleText() =>
        $"{(_island.IsUserVisible ? Strings.Tray_Hide : Strings.Tray_Show)}\t{_island.HotkeyText}";

    private void OpenLogsFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.Logs}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo abrir la carpeta de logs");
        }
    }

    /// <summary>Ícono de la app (embebido), en el tamaño justo para la bandeja según la escala de pantalla.</summary>
    private Icon LoadAppIcon(int size)
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/NexusIsland.ico"));
            if (resource is not null)
            {
                using var stream = resource.Stream;
                return new Icon(stream, size, size);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo cargar el ícono de la app; se usa uno dibujado");
        }
        return CreateCapsuleIcon(size);
    }

    /// <summary>Respaldo: ícono dibujado por código (una cápsula) si el embebido no se pudiera cargar.</summary>
    private static Icon CreateCapsuleIcon(int size)
    {
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            float w = size * 0.92f, h = size * 0.46f;
            float x = (size - w) / 2f, y = (size - h) / 2f, r = h;
            using var path = new GraphicsPath();
            path.AddArc(x, y, r, r, 90, 180);
            path.AddArc(x + w - r, y, r, r, 270, 180);
            path.CloseFigure();

            using var fill = new SolidBrush(Color.FromArgb(255, 18, 18, 22));
            using var stroke = new Pen(Color.FromArgb(255, 200, 200, 210), Math.Max(1f, size / 16f));
            g.FillPath(fill, path);
            g.DrawPath(stroke, path);

            float d = h * 0.38f;
            using var accent = new SolidBrush(Color.FromArgb(255, 52, 211, 153));
            g.FillEllipse(accent, x + h * 0.45f, (size - d) / 2f, d, d);
        }

        var hIcon = bmp.GetHicon();
        try
        {
            // Clone() copia el ícono, así podemos liberar el handle nativo enseguida.
            using var temp = Icon.FromHandle(hIcon);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeIcon.Destroy(hIcon);
        }
    }

    public void Dispose()
    {
        if (_notifyIcon is not null)
        {
            _notifyIcon.Visible = false; // evita el "ícono fantasma" en la bandeja al salir
            _notifyIcon.ContextMenuStrip?.Dispose();
            _notifyIcon.Dispose();
            _notifyIcon = null;
        }
        _icon?.Dispose();
    }
}
