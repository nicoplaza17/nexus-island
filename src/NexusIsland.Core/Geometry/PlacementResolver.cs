using NexusIsland.Core.Settings;

namespace NexusIsland.Core.Geometry;

/// <summary>Un monitor, en píxeles físicos.</summary>
/// <param name="DeviceName">Nombre de dispositivo de Windows ("\\.\DISPLAY1").</param>
public sealed record MonitorDescriptor(string DeviceName, PixelRect Bounds, PixelRect WorkArea, bool IsPrimary, double Scale = 1.0);

/// <summary>Dónde ubicar la isla.</summary>
public sealed record PlacementTarget(MonitorDescriptor Monitor, IslandPlacementMode Mode, PixelPoint Position);

/// <summary>
/// Decide la ubicación inicial a partir de la configuración guardada y los monitores conectados ahora
/// (que pueden no ser los mismos: notebook desenchufada del monitor externo, etc.).
/// </summary>
public static class PlacementResolver
{
    public static PlacementTarget Resolve(IslandSettings settings, IReadOnlyList<MonitorDescriptor> monitors, PixelRect windowSize)
    {
        if (monitors.Count == 0) throw new ArgumentException("No hay monitores.", nameof(monitors));
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];

        // Suelta: se respeta la posición si sigue cayendo en algún monitor; se ajusta para que entre entera.
        if (settings.Placement == IslandPlacementMode.Floating && settings.FloatingX is { } x && settings.FloatingY is { } y)
        {
            // Monitor con el que más se superpone (el tamaño pudo cambiar desde que se guardó).
            var rect = PixelRect.FromSize(x, y, windowSize.Width, windowSize.Height);
            var host = monitors.MaxBy(m => OverlapArea(m.Bounds, rect));
            if (host is not null && OverlapArea(host.Bounds, rect) > 0)
                return new PlacementTarget(host, IslandPlacementMode.Floating, PlacementCalculator.ClampInto(rect, host.WorkArea));
            // El monitor donde estaba ya no existe: se ancla en el principal.
        }

        var monitor = FindMonitor(monitors, settings.MonitorDevice) ?? primary;
        return new PlacementTarget(monitor, IslandPlacementMode.DockedTopCenter,
            PlacementCalculator.DockTopCenter(monitor.WorkArea, windowSize.Width));
    }

    public static MonitorDescriptor? FindMonitor(IReadOnlyList<MonitorDescriptor> monitors, string? deviceName) =>
        string.IsNullOrEmpty(deviceName)
            ? null
            : monitors.FirstOrDefault(m => string.Equals(m.DeviceName, deviceName, StringComparison.OrdinalIgnoreCase));

    private static long OverlapArea(PixelRect a, PixelRect b)
    {
        long w = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        long h = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return w > 0 && h > 0 ? w * h : 0;
    }
}
