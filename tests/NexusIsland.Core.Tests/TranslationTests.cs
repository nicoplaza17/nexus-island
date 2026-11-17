using System.Net;
using System.Text.RegularExpressions;
using NexusIsland.Core.Translation;

namespace NexusIsland.Core.Tests;

public class TranslationTests
{
    [Theory]
    [InlineData("Hello, how are you?", TranslationDirection.EnglishToSpanish)]
    [InlineData("Could you send me the report by the end of the day?", TranslationDirection.EnglishToSpanish)]
    [InlineData("I'm running a bit late.", TranslationDirection.EnglishToSpanish)]
    [InlineData("Che, ¿me pasás el archivo cuando puedas?", TranslationDirection.SpanishToEnglish)]
    [InlineData("La reunión se pasó al jueves a las 15.", TranslationDirection.SpanishToEnglish)]
    [InlineData("Gracias por todo, nos vemos mañana.", TranslationDirection.SpanishToEnglish)]
    [InlineData("El auto tiene un ruido raro cuando frena", TranslationDirection.SpanishToEnglish)]
    [InlineData("Che, ¿me pasás el archivo cuando puedas? El auto tiene un ruido raro cuando frena.", TranslationDirection.SpanishToEnglish)]
    [InlineData("Spotify", TranslationDirection.EnglishToSpanish)] // ante la duda, inglés
    public void Detect_GuessesLanguage(string text, TranslationDirection expected) =>
        Assert.Equal(expected, TranslationText.Detect(text));

    [Fact]
    public void Reverse_And_Code()
    {
        Assert.Equal(TranslationDirection.SpanishToEnglish, TranslationDirection.EnglishToSpanish.Reverse());
        Assert.Equal("en-es", TranslationDirection.EnglishToSpanish.Code());
        Assert.Equal("es-en", TranslationDirection.SpanishToEnglish.Code());
    }

    [Fact]
    public void Split_KeepsLinesAndSentences()
    {
        var parts = TranslationText.Split("Hello there. How are you?\n\nSee you!");
        Assert.Equal(3, parts.Count);
        Assert.Equal(["Hello there.", "How are you?"], parts[0]);
        Assert.Empty(parts[1]);
        Assert.Equal(["See you!"], parts[2]);
        Assert.Equal("A. B.\n\nC!", TranslationText.Join(parts.Select((l, i) => l.Select(_ => i == 0 ? (_ == "Hello there." ? "A." : "B.") : "C!"))));
    }

    [Fact]
    public void Split_CutsVeryLongSentences()
    {
        var longText = string.Join(" ", Enumerable.Repeat("word", 300)); // 1499 caracteres, sin puntos
        var parts = TranslationText.Split(longText).Single();
        Assert.True(parts.Count >= 4);
        Assert.All(parts, p => Assert.True(p.Length <= TranslationText.MaxChunk));
        Assert.Equal(longText, string.Join(" ", parts));
    }

    [Fact]
    public void Prepare_TrimsAndLimits()
    {
        Assert.Equal("hola", TranslationText.Prepare("  hola \r\n", out var t1));
        Assert.False(t1);
        var big = new string('a', TranslationText.MaxLength + 50);
        Assert.Equal(TranslationText.MaxLength, TranslationText.Prepare(big, out var t2).Length);
        Assert.True(t2);
    }

    [Fact]
    public void Catalog_IsPinnedAndComplete()
    {
        foreach (var model in new[] { TranslationModels.EnglishToSpanish, TranslationModels.SpanishToEnglish })
        {
            Assert.Matches("^[0-9a-f]{40}$", model.Revision); // revisión fija, no "main"
            Assert.Contains(model.Files, f => f.Name == TranslationModels.EncoderFile);
            Assert.Contains(model.Files, f => f.Name == TranslationModels.DecoderFile);
            Assert.All(model.Files, f => Assert.Matches("^[0-9a-f]{64}$", f.Sha256));
            Assert.InRange(model.TotalBytes, 100_000_000, 130_000_000);
            var url = model.UrlOf(model.Files.First(f => f.Name == TranslationModels.EncoderFile)).ToString();
            Assert.StartsWith($"https://huggingface.co/{model.Repository}/resolve/{model.Revision}/onnx/", url);
        }
    }

    [Fact]
    public async Task Download_RejectsTamperedFile_AndLeavesNothing()
    {
        var root = TempDir();
        try
        {
            var store = new ModelStore(root, new HttpClient(new FakeServer(_ => new byte[1468]))); // config.json "de 1468 bytes" con ceros
            await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadAsync(TranslationDirection.EnglishToSpanish, null, CancellationToken.None));
            Assert.False(store.IsInstalled(TranslationDirection.EnglishToSpanish));
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Download_RejectsOversizedFile()
    {
        var root = TempDir();
        try
        {
            var store = new ModelStore(root, new HttpClient(new FakeServer(_ => new byte[5_000])));
            await Assert.ThrowsAsync<InvalidDataException>(() => store.DownloadAsync(TranslationDirection.SpanishToEnglish, null, CancellationToken.None));
            Assert.Empty(Directory.GetFiles(root, "*", SearchOption.AllDirectories));
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>
    /// Descarga completa verificada, sirviendo los archivos reales desde la carpeta de la prueba de laboratorio
    /// (artifacts\translate-lab). Si no está (otra PC), el test no hace nada.
    /// </summary>
    [Fact]
    public async Task Download_AcceptsGenuineFiles_AndReportsProgress()
    {
        var lab = Path.Combine(RepoRoot(), "artifacts", "translate-lab", "models", "en-es");
        if (!Directory.Exists(lab)) return;
        var root = TempDir();
        try
        {
            var store = new ModelStore(root, new HttpClient(new FakeServer(name => File.ReadAllBytes(Path.Combine(lab, name)))));
            var reports = new List<double>();
            await store.DownloadAsync(TranslationDirection.EnglishToSpanish, new SyncProgress(reports.Add), CancellationToken.None);
            Assert.True(store.IsInstalled(TranslationDirection.EnglishToSpanish));
            Assert.False(store.IsInstalled(TranslationDirection.SpanishToEnglish));
            Assert.Equal(1.0, reports[^1]);
            Assert.Equal(TranslationModels.EnglishToSpanish.TotalBytes, store.InstalledBytes());
            store.DeleteAll();
            Assert.Equal(0, store.InstalledBytes());
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private static string TempDir()
    {
        var d = Path.Combine(Path.GetTempPath(), "nexus-models-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(d);
        return d;
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "NexusIsland.slnx"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private sealed class SyncProgress(Action<double> report) : IProgress<double>
    {
        public void Report(double value) => report(value);
    }

    /// <summary>Servidor falso: responde cada archivo según su nombre (el final de la URL).</summary>
    private sealed class FakeServer(Func<string, byte[]> content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Assert.Matches(new Regex("^https://huggingface\\.co/Xenova/opus-mt-(en-es|es-en)/resolve/[0-9a-f]{40}/"), request.RequestUri!.ToString());
            var name = request.RequestUri.Segments[^1];
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(content(name)) });
        }
    }
}
