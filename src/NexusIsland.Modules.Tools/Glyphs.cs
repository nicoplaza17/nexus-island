namespace NexusIsland.Modules.Tools;

/// <summary>Íconos de Segoe Fluent Icons / Segoe MDL2 Assets usados por las herramientas.</summary>
internal static class Glyphs
{
    public const string Timer = "\uE823";     // reloj
    public const string Stopwatch = "\uE916"; // cronómetro
    public const string Bell = "\uEA8F";      // campana (E7ED es la campana tachada: "silencio")
    public const string Clipboard = "\uE77F"; // pegar
    public const string Play = "\uE768";
    public const string Pause = "\uE769";

    /// <summary>Plantillas de las cuatro herramientas (un solo diccionario compartido).</summary>
    public static readonly Uri TemplatesUri =
        new("pack://application:,,,/NexusIsland.Modules.Tools;component/ToolsTemplates.xaml", UriKind.Absolute);
}