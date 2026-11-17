using System.IO;
using System.Text;
using NexusIsland.Translation;

namespace NexusIsland.App;

/// <summary>
/// Punto de entrada. Normalmente arranca la isla; con "--translator" corre el motor de traducción
/// (sin ventanas ni WPF), conectado a la isla por la entrada y salida estándar.
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args is ["--translator", ..])
        {
            using var input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));
            using var output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false));
            return TranslatorHost.Run(input, output);
        }

        var app = new App();
        app.InitializeComponent();
        return app.Run();
    }
}
