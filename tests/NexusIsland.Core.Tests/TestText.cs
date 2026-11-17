using NexusIsland.Core.Properties;

namespace NexusIsland.Core.Tests;

/// <summary>
/// En los tests, "@Clave" significa "el texto de la clave en el idioma actual": así los tests
/// pasan igual con Windows en español o en inglés.
/// </summary>
internal static class TestText
{
    public static string R(string expected) =>
        expected.StartsWith('@') ? Strings.ResourceManager.GetString(expected[1..], Strings.Culture)! : expected;
}
