using NexusIsland.Core.Monitoring;
using NexusIsland.Core.Tasks;

namespace NexusIsland.Core.Tests;

public class TaskRelevanceTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    private static BackgroundTask Dl(string id, TaskState state, double startedAgo = 0, double? progress = null) =>
        new(id, TaskKind.Download, id, state, T0.AddSeconds(-startedAgo), T0, progress);

    [Fact]
    public void Short_downloads_never_show_or_notify()
    {
        var r = new TaskRelevance();
        r.Apply(Dl("a", TaskState.Running));
        Assert.Null(r.Summary(T0.AddSeconds(1)));          // todavía no: menos de 3 s
        Assert.Null(r.Apply(Dl("a", TaskState.Completed))); // terminó rápido: sin aviso
        Assert.Null(r.Summary(T0.AddSeconds(2)));
    }

    [Fact]
    public void Long_download_shows_then_notifies_once()
    {
        var r = new TaskRelevance();
        r.Apply(Dl("a", TaskState.Running));
        Assert.Equal("a", r.Summary(T0.AddSeconds(4))!.Primary.Id);
        var done = r.Apply(Dl("a", TaskState.Completed));
        Assert.Equal(TaskState.Completed, done!.State);
        Assert.Null(r.Summary(T0.AddSeconds(5)));
    }

    [Fact]
    public void Cancelled_is_silent_failed_always_notifies()
    {
        var r = new TaskRelevance();
        r.Apply(Dl("a", TaskState.Running));
        r.Summary(T0.AddSeconds(10));
        Assert.Null(r.Apply(Dl("a", TaskState.Cancelled)));
        Assert.NotNull(r.Apply(Dl("b", TaskState.Failed))); // falló aunque nunca se mostró
    }

    [Fact]
    public void Install_results_always_notify()
    {
        var r = new TaskRelevance();
        var msi = new BackgroundTask("msi:1", TaskKind.Install, "Programa", TaskState.Completed, T0, T0, 1);
        Assert.NotNull(r.Apply(msi));
    }

    [Fact]
    public void Several_tasks_group_with_most_advanced_first()
    {
        var r = new TaskRelevance();
        r.Apply(Dl("a", TaskState.Running, startedAgo: 10));
        r.Apply(new BackgroundTask("c", TaskKind.FileCopy, "copia", TaskState.Running, T0.AddSeconds(-10), T0, 0.6));
        r.Apply(Dl("b", TaskState.Running, startedAgo: 0)); // recién empezada: todavía no cuenta
        var s = r.Summary(T0)!;
        Assert.Equal(2, s.RunningCount);
        Assert.Equal("c", s.Primary.Id); // la que tiene progreso conocido va primero
    }
}

public class DownloadFilesTests
{
    [Theory]
    [InlineData(@"C:\Users\x\Downloads\setup.exe.crdownload", "setup.exe", "Chrome / Edge")]
    [InlineData(@"C:\Users\x\Downloads\Unconfirmed 812345.crdownload", "@Download_Unnamed", "Chrome / Edge")]
    [InlineData(@"C:\Users\x\Downloads\video.mp4.part", "video.mp4", "Firefox")]
    [InlineData(@"C:\Users\x\Downloads\doc.pdf.partial", "doc.pdf", "Edge")]
    public void Recognizes_browser_temp_files(string path, string expectedName, string expectedBrowser)
    {
        Assert.True(DownloadFiles.TryParseTemp(path, out var name, out var browser));
        Assert.Equal(TestText.R(expectedName), name);
        Assert.Equal(expectedBrowser, browser);
    }

    [Theory]
    [InlineData(@"C:\Users\x\Downloads\informe.pdf")]
    [InlineData(@"C:\Users\x\Downloads\.crdownload")]
    [InlineData(@"C:\Users\x\Downloads\departamento.txt")]
    public void Ignores_regular_files(string path) => Assert.False(DownloadFiles.IsTemp(path));
}

public class CopyAndInstallerHeuristicsTests
{
    [Theory]
    [InlineData("37 % completado", 0.37)]
    [InlineData("100% complete", 1.0)]
    [InlineData("5% completado", 0.05)]
    public void Reads_percent_from_explorer_title(string title, double expected) =>
        Assert.Equal(expected, CopyProgressTitle.Parse(title)!.Value, 3);

    [Theory]
    [InlineData("Calculando...")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("250 % raro")]
    public void Unknown_titles_have_no_percent(string? title) => Assert.Null(CopyProgressTitle.Parse(title));

    [Theory]
    [InlineData("NexusIsland-Setup-0.7.0", true)] // el instalador de la isla es un instalador como cualquier otro
    [InlineData("NexusIsland", false)]            // la isla en sí, no
    [InlineData("ChromeSetup", true)]
    [InlineData("vs_installer", true)]
    [InlineData("unins000", true)]
    [InlineData("msiexec", false)]        // el servicio de Windows Installer (se informa por eventos)
    [InlineData("TrustedInstaller", false)]
    [InlineData("TiWorker", false)]
    [InlineData("chrome", false)]
    [InlineData("OmenInstallMonitor", false)]  // servicio de HP que corre siempre (falso positivo real)
    [InlineData("InstallService", false)]
    [InlineData("GoogleUpdateSetup", false)]     // actualizador de fondo
    public void Installer_process_heuristic(string name, bool expected) =>
        Assert.Equal(expected, InstallerProcess.Looks(name));

    [Fact]
    public void Background_process_from_before_without_window_is_ignored()
    {
        var watch = new DateTimeOffset(2026, 11, 17, 11, 0, 0, TimeSpan.Zero);
        Assert.False(InstallerProcess.IsRelevant(watch.AddHours(-3), watch, hasWindow: false)); // desde el arranque, sin ventana
        Assert.True(InstallerProcess.IsRelevant(watch.AddHours(-3), watch, hasWindow: true));   // ya abierto pero con ventana
        Assert.True(InstallerProcess.IsRelevant(watch.AddMinutes(5), watch, hasWindow: false)); // lanzado después (silencioso)
    }

    [Fact]
    public void Size_format()
    {
        Assert.Equal("—", MetricFormat.Size(null));
        Assert.Equal("820 B", MetricFormat.Size(820));
        Assert.Equal($"{45.3:0.#} MB", MetricFormat.Size((long)(45.3 * 1024 * 1024)));
    }
}
