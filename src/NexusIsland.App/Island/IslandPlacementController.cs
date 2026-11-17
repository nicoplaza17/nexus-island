using Microsoft.Extensions.Logging;
using NexusIsland.Core.Geometry;
using NexusIsland.Core.Settings;
using NexusIsland.Services.Windows.Display;

namespace NexusIsland.App.Island;

/// <summary>
/// Ubicación de la ventana de la isla: anclaje arriba al centro, arrastre con "imán",
/// reubicación cuando cambian los monitores, y persistencia en la configuración.
/// Todo en píxeles físicos para funcionar bien con monitores de distinta escala.
/// </summary>
public sealed class IslandPlacementController
{
    private const double SnapDistanceDip = 56;

    private readonly IntPtr _hwnd;
    private readonly ILogger _logger;
    private readonly SettingsStore _settings;
    private PixelPoint _dragOffset;

    public IslandPlacementController(IntPtr hwnd, ILogger logger, SettingsStore settings)
    {
        _hwnd = hwnd;
        _logger = logger;
        _settings = settings;
    }

    public IslandPlacementMode Mode { get; private set; } = IslandPlacementMode.DockedTopCenter;

    public bool IsDragging { get; private set; }

    /// <summary>Ubicación al arrancar: la guardada, si los monitores lo permiten.</summary>
    public void PlaceInitial()
    {
        var monitors = DisplayService.GetMonitors().Select(m => m.ToDescriptor()).ToList();
        var target = PlacementResolver.Resolve(_settings.Current.Island, monitors, DisplayService.GetWindowRect(_hwnd));
        Mode = target.Mode;
        DisplayService.MoveWindow(_hwnd, target.Position);
        _logger.LogInformation("Ubicación inicial: {Mode} en {Monitor} ({X}, {Y})",
            target.Mode == IslandPlacementMode.Floating ? "suelta" : "anclada", target.Monitor.DeviceName,
            target.Position.X, target.Position.Y);
    }

    /// <summary>Tras un cambio de pantallas o de tamaño: re-ancla, o asegura que la isla suelta siga visible.</summary>
    public void Revalidate()
    {
        if (IsDragging) return;
        var monitor = DisplayService.GetMonitorForWindow(_hwnd);
        if (Mode == IslandPlacementMode.DockedTopCenter)
        {
            DockOn(monitor, save: false);
        }
        else
        {
            var rect = DisplayService.GetWindowRect(_hwnd);
            var position = PlacementCalculator.ClampInto(rect, monitor.WorkArea);
            DisplayService.MoveWindow(_hwnd, position);
            SaveFloating(position, monitor);
        }
    }

    /// <summary>Ancla en un monitor elegido desde la configuración (null = principal).</summary>
    public void DockOnMonitor(string? deviceName)
    {
        var monitors = DisplayService.GetMonitors();
        var monitor = monitors.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));
        DockOn(monitor.Handle != IntPtr.Zero ? monitor : DisplayService.GetPrimaryMonitor(), save: true);
    }

    public void BeginDrag(PixelPoint cursorAtPress)
    {
        var rect = DisplayService.GetWindowRect(_hwnd);
        _dragOffset = new PixelPoint(cursorAtPress.X - rect.Left, cursorAtPress.Y - rect.Top);
        IsDragging = true;
    }

    public void DragTo(PixelPoint cursor)
    {
        if (!IsDragging) return;
        DisplayService.MoveWindow(_hwnd, new PixelPoint(cursor.X - _dragOffset.X, cursor.Y - _dragOffset.Y));
    }

    /// <summary>Al soltar: si quedó cerca de arriba al centro, se ancla; si no, queda suelta dentro de la pantalla.</summary>
    public void EndDrag()
    {
        if (!IsDragging) return;
        IsDragging = false;

        var rect = DisplayService.GetWindowRect(_hwnd);
        var monitor = DisplayService.GetMonitorForPoint(DisplayService.GetCursorPosition());
        var snap = (int)Math.Round(SnapDistanceDip * DisplayService.GetScaleForWindow(_hwnd));

        if (PlacementCalculator.ShouldSnapToTopCenter(rect, monitor.WorkArea, snap))
        {
            DockOn(monitor, save: true);
        }
        else
        {
            Mode = IslandPlacementMode.Floating;
            var position = PlacementCalculator.ClampInto(rect, monitor.WorkArea);
            DisplayService.MoveWindow(_hwnd, position);
            SaveFloating(position, monitor);
            _logger.LogInformation("Isla suelta en ({X}, {Y})", position.X, position.Y);
        }
    }

    private void DockOn(MonitorInfo monitor, bool save)
    {
        Mode = IslandPlacementMode.DockedTopCenter;
        var rect = DisplayService.GetWindowRect(_hwnd);
        var position = PlacementCalculator.DockTopCenter(monitor.WorkArea, rect.Width);
        DisplayService.MoveWindow(_hwnd, position);
        _logger.LogInformation("Isla anclada arriba al centro de {Monitor} ({W}x{H}{Primary}) en ({X}, {Y})",
            monitor.DeviceName, monitor.Bounds.Width, monitor.Bounds.Height, monitor.IsPrimary ? ", principal" : "",
            position.X, position.Y);

        if (save)
        {
            _settings.Update(s =>
            {
                s.Island.Placement = IslandPlacementMode.DockedTopCenter;
                s.Island.MonitorDevice = monitor.DeviceName;
                s.Island.FloatingX = s.Island.FloatingY = null;
            });
        }
    }

    private void SaveFloating(PixelPoint position, MonitorInfo monitor)
    {
        var island = _settings.Current.Island;
        if (island.Placement == IslandPlacementMode.Floating && island.FloatingX == position.X
            && island.FloatingY == position.Y && island.MonitorDevice == monitor.DeviceName)
            return;

        _settings.Update(s =>
        {
            s.Island.Placement = IslandPlacementMode.Floating;
            s.Island.MonitorDevice = monitor.DeviceName;
            s.Island.FloatingX = position.X;
            s.Island.FloatingY = position.Y;
        });
    }
}
