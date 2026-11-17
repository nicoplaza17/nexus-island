using System.Text.Json.Serialization;

namespace NexusIsland.Core.Settings;

/// <summary>
/// Toda la configuración del usuario. Se guarda como JSON en %AppData%\NexusIsland\settings.json.
/// Las propiedades tienen valores por defecto: un archivo viejo o incompleto se completa solo.
/// </summary>
public sealed class AppSettings
{
    public const int CurrentVersion = 1;

    public int Version { get; set; } = CurrentVersion;
    public GeneralSettings General { get; set; } = new();
    public IslandSettings Island { get; set; } = new();
    public HotkeySettings Hotkey { get; set; } = new();
    public MediaSettings Media { get; set; } = new();
    public MonitorSettings Monitor { get; set; } = new();

    /// <summary>Módulos activados por id. Un módulo que no figura se considera activado.</summary>
    public Dictionary<string, bool> Modules { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public bool IsModuleEnabled(string id) => !Modules.TryGetValue(id, out var enabled) || enabled;

    /// <summary>Corrige valores fuera de rango (por ejemplo si alguien editó el archivo a mano).</summary>
    public void Normalize()
    {
        General ??= new();
        Island ??= new();
        Hotkey ??= new();
        Media ??= new();
        Monitor ??= new();
        Modules =new Dictionary<string, bool>(Modules ?? new(), StringComparer.OrdinalIgnoreCase);
        Media.IgnoredApps ??= [];
        Media.IgnoredApps = Media.IgnoredApps
            .Where(a => !string.IsNullOrWhiteSpace(a.Id))
            .GroupBy(a => a.Id, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
        if (!Enum.IsDefined(Island.Size)) Island.Size = IslandSize.Normal;
        if (!Enum.IsDefined(Island.Placement)) Island.Placement = IslandPlacementMode.DockedTopCenter;
        if (string.IsNullOrWhiteSpace(Hotkey.Key)) Hotkey.Key = HotkeySettings.DefaultKey;
        if (!Hotkey.Ctrl && !Hotkey.Alt && !Hotkey.Shift && !Hotkey.Win) // un atajo sin modificadores sería una tecla común
        {
            Hotkey.Ctrl = true;
            Hotkey.Alt = true;
        }
    }
}

public sealed class GeneralSettings
{
    public bool StartWithWindows { get; set; }
    public bool HideInFullscreen { get; set; } = true;
}

[JsonConverter(typeof(JsonStringEnumConverter<IslandPlacementMode>))]
public enum IslandPlacementMode
{
    /// <summary>Anclada arriba al centro de un monitor.</summary>
    DockedTopCenter,

    /// <summary>Suelta, donde el usuario la dejó.</summary>
    Floating,
}

[JsonConverter(typeof(JsonStringEnumConverter<IslandSize>))]
public enum IslandSize
{
    Small,
    Normal,
    Large,
}

public sealed class IslandSettings
{
    public IslandPlacementMode Placement { get; set; } = IslandPlacementMode.DockedTopCenter;

    /// <summary>Monitor elegido (nombre de dispositivo de Windows, p. ej. "\\.\DISPLAY2"). Null = principal.</summary>
    public string? MonitorDevice { get; set; }

    /// <summary>Posición de la ventana cuando está suelta, en píxeles físicos.</summary>
    public int? FloatingX { get; set; }
    public int? FloatingY { get; set; }

    public IslandSize Size { get; set; } = IslandSize.Normal;

    [JsonIgnore]
    public double Scale => Size switch
    {
        IslandSize.Small => 0.85,
        IslandSize.Large => 1.15,
        _ => 1.0,
    };
}

public sealed class HotkeySettings
{
    public const string DefaultKey = "N";

    public bool Ctrl { get; set; } = true;
    public bool Alt { get; set; } = true;
    public bool Shift { get; set; }
    public bool Win { get; set; }

    /// <summary>Nombre de la tecla (como System.Windows.Input.Key: "N", "F9", "Space"...).</summary>
    public string Key { get; set; } = DefaultKey;

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(Key);
        return string.Join("+", parts);
    }

    public HotkeySettings Clone() => (HotkeySettings)MemberwiseClone();
}

public sealed class MediaSettings
{
    /// <summary>Mostrar el vistazo unos segundos al cambiar de canción.</summary>
    public bool NotifyOnTrackChange { get; set; } = true;

    /// <summary>Apps cuya reproducción la isla no muestra (p. ej. WhatsApp, Discord).</summary>
    public List<IgnoredApp> IgnoredApps { get; set; } = [];

    public bool IsIgnored(string appId) =>
        IgnoredApps.Any(a => string.Equals(a.Id, appId, StringComparison.OrdinalIgnoreCase));
}

/// <summary>Alertas del monitoreo del sistema (el panel "Sistema" siempre está disponible).</summary>
public sealed class MonitorSettings
{
    public bool AlertCpu { get; set; } = true;
    public bool AlertMemory { get; set; } = true;
    public bool AlertBattery { get; set; } = true;
    public bool AlertNetwork { get; set; } = true;
}

/// <param name="Id">Identificador técnico (AppUserModelId).</param>
/// <param name="Name">Nombre legible, para mostrarlo en la configuración.</param>
public sealed record IgnoredApp(string Id, string Name);
