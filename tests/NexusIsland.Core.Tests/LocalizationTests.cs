using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using NexusIsland.Core.Properties;
using NexusIsland.Core.Settings;
using NexusIsland.Core.Tasks;

namespace NexusIsland.Core.Tests;

public class LocalizationTests
{
    [Theory]
    [InlineData(null, "auto")]
    [InlineData("", "auto")]
    [InlineData("ES", "es")]
    [InlineData(" en ", "en")]
    [InlineData("fr", "auto")]
    public void Normalize_KeepsOnlySupportedValues(string? input, string expected) =>
        Assert.Equal(expected, AppLanguage.Normalize(input));

    [Fact]
    public void Resolve_AutoKeepsWindowsLanguage() =>
        Assert.Null(AppLanguage.Resolve("auto", CultureInfo.GetCultureInfo("es-AR")));

    [Fact]
    public void Resolve_SameLanguageKeepsRegionalVariant() =>
        Assert.Null(AppLanguage.Resolve("es", CultureInfo.GetCultureInfo("es-AR")));

    [Fact]
    public void Resolve_OtherLanguageForcesIt() =>
        Assert.Equal("en", AppLanguage.Resolve("en", CultureInfo.GetCultureInfo("es-AR"))!.Name);

    [Fact]
    public void Settings_BadLanguageIsNormalized()
    {
        var s = new AppSettings { General = { Language = "klingon" } };
        s.Normalize();
        Assert.Equal(AppLanguage.Auto, s.General.Language);
    }

    [Fact]
    public void Texts_FollowTheInterfaceLanguage()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Download", DownloadFiles.UnnamedName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-AR");
            Assert.Equal("Descarga", DownloadFiles.UnnamedName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("pt-BR"); // sin traducción → inglés
            Assert.Equal("Download", DownloadFiles.UnnamedName);
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

    public static TheoryData<string> ResourceProjects()
    {
        var data = new TheoryData<string>();
        foreach (var resx in Directory.GetFiles(Path.Combine(RepoRoot(), "src"), "Strings.resx", SearchOption.AllDirectories))
            data.Add(Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(resx)))!);
        return data;
    }

    /// <summary>Cada texto existe en los dos idiomas y con los mismos huecos {0}, {1}…</summary>
    [Theory]
    [MemberData(nameof(ResourceProjects))]
    public void EveryText_HasSpanishAndEnglish_WithSamePlaceholders(string project)
    {
        var props = Path.Combine(RepoRoot(), "src", project, "Properties");
        var en = Load(Path.Combine(props, "Strings.resx"));
        var es = Load(Path.Combine(props, "Strings.es.resx"));

        Assert.Empty(en.Keys.Except(es.Keys));
        Assert.Empty(es.Keys.Except(en.Keys));
        foreach (var (key, text) in en)
            Assert.True(Placeholders(text).SetEquals(Placeholders(es[key])), $"{project}/{key}: huecos distintos");
    }

    private static Dictionary<string, string> Load(string path) =>
        XDocument.Load(path).Root!.Elements("data")
            .ToDictionary(d => (string)d.Attribute("name")!, d => (string)d.Element("value")!);

    private static HashSet<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{\d+\}").Select(m => m.Value).ToHashSet();

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexusIsland.slnx"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("No encontré la raíz del repositorio");
    }
}
