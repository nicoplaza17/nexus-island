using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Island;
using NexusIsland.Core.Monitoring;
using NexusIsland.Core.Mvvm;
using NexusIsland.Core.Settings;
using NexusIsland.Core.Tasks;

using NexusIsland.Modules.Tasks.Properties;

namespace NexusIsland.Modules.Tasks;

/// <summary>
/// Tareas en segundo plano en la isla: descargas, copias del Explorador e instalaciones.
/// Solo aparece lo relevante (lo que dura más de 3 s); varias a la vez se agrupan; al terminar, aviso breve.
/// </summary>
public sealed class TasksModule(IBackgroundTaskSource source, SettingsStore settings, ILogger<TasksModule> logger) : IIslandModule
{
    public const string ModuleId = "tasks";
    public const string DoneModuleId = "tasks.done";
    private const string RunningKey = "tasks.running";
    private const string DoneKey = "tasks.done";
    private const int DonePriority = 150;
    private static readonly TimeSpan DoneAttention = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DoneVisible = TimeSpan.FromSeconds(7);

    private readonly TaskRelevance _relevance = new();
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _hideDone = new(DispatcherPriority.Background) { Interval = DoneVisible };
    private readonly RunningTasksViewModel _running = new();
    private IActivityHost? _host;
    private SynchronizationContext? _ui;
    private TaskDoneViewModel? _done;
    private bool _runningPublished;

    public string Id => ModuleId;
    public string DisplayName => Strings.Module_Name;
    public Uri? TemplatesUri { get; } =
        new("pack://application:,,,/NexusIsland.Modules.Tasks;component/TaskTemplates.xaml", UriKind.Absolute);

    public Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _ui = SynchronizationContext.Current;
        _tick.Tick += (_, _) => Refresh();
        _hideDone.Tick += (_, _) =>
        {
            if (_done is { IsBeingRead: true }) return;
            RemoveDone();
        };
        source.Changed += t => _ui?.Post(_ => OnChanged(t), null);
        settings.Changed += s => source.UpdateOptions(Options(s));
        source.Start(Options(settings.Current));
        return Task.CompletedTask;
    }

    private static TaskWatchOptions Options(AppSettings s) => new(s.Tasks.Downloads, s.Tasks.FileCopies, s.Tasks.Installers);

    private void OnChanged(BackgroundTask task)
    {
        if (task.State != TaskState.Running)
            logger.LogInformation("Tarea {Kind} {State}", task.Kind, task.State); // sin nombres de archivo en el log
        var finished = _relevance.Apply(task);
        if (finished is not null) ShowDone(finished);
        Refresh();
    }

    /// <summary>Actualiza la actividad "en curso" (también cada segundo, porque lo visible depende del tiempo).</summary>
    private void Refresh()
    {
        if (_host is null) return;
        var summary = _relevance.Summary(DateTimeOffset.Now);
        if (_relevance.RunningCount > 0) _tick.Start(); else _tick.Stop();

        if (summary is null)
        {
            if (_runningPublished) { _host.Remove(RunningKey); _runningPublished = false; }
            return;
        }
        _running.Update(summary);
        if (!_runningPublished)
        {
            _host.Publish(new IslandActivity(RunningKey, ModuleId, ActivityPriority.Active, _running));
            _runningPublished = true;
        }
    }

    private void ShowDone(BackgroundTask task)
    {
        if (_host is null) return;
        _done = new TaskDoneViewModel(task, ShowInFolder, RemoveDone);
        _host.Publish(new IslandActivity(DoneKey, DoneModuleId, DonePriority, _done));
        _host.RequestAttention(DoneKey, DoneAttention);
        _hideDone.Stop();
        _hideDone.Start();
    }

    private void RemoveDone()
    {
        _hideDone.Stop();
        _done = null;
        _host?.Remove(DoneKey);
    }

    /// <summary>Abre el Explorador con el archivo seleccionado (no lo ejecuta: podría ser un instalador).</summary>
    private void ShowInFolder(string location)
    {
        try
        {
            var args = File.Exists(location) ? $"/select,\"{location}\"" : $"\"{location}\"";
            Process.Start(new ProcessStartInfo("explorer.exe", args) { UseShellExecute = true });
            RemoveDone();
        }
        catch (Exception ex) { logger.LogDebug(ex, "No se pudo abrir la carpeta"); }
    }

    public void Dispose()
    {
        _tick.Stop();
        _hideDone.Stop();
    }
}

internal static class TaskText
{
    public static string Glyph(TaskKind kind) => kind switch
    {
        TaskKind.Download => "\uE896",
        TaskKind.FileCopy => "\uE8C8",
        _ => "\uE7B8",
    };

    public static string Verb(BackgroundTask t) => t.Kind switch
    {
        TaskKind.Download => Strings.Verb_Download,
        TaskKind.FileCopy => Strings.Verb_Copy,
        _ => Strings.Verb_Install,
    };

    /// <summary>"45,3 MB · 3,2 MB/s", "37 %", "Instalador en curso".</summary>
    public static string Detail(BackgroundTask t) => t.Kind switch
    {
        TaskKind.Download => t.BytesPerSecond is > 0
            ? $"{MetricFormat.Size(t.Bytes)} · {MetricFormat.Rate(t.BytesPerSecond)}"
            : MetricFormat.Size(t.Bytes),
        TaskKind.FileCopy => t.Progress is { } p ? $"{p:P0}" : Strings.Preparing,
        _ => t.Detail ?? "",
    };
}

public sealed class TaskRowViewModel(BackgroundTask t)
{
    public string Glyph => TaskText.Glyph(t.Kind);
    public string Title => t.Kind == TaskKind.FileCopy ? Strings.Copying_Files : $"{TaskText.Verb(t)} {t.Title}";
    public string Detail => TaskText.Detail(t);
    public bool HasProgress => t.Progress is not null;
    public double Progress => t.Progress ?? 0;
}

/// <summary>Las tareas en curso (la principal + el resto).</summary>
public sealed class RunningTasksViewModel : ObservableObject, IAccessibleActivity
{
    private string _glyph = "", _title = "", _short = "", _detail = "", _more = "";
    private double _progress;
    private bool _hasProgress;

    public string Glyph { get => _glyph; private set => SetProperty(ref _glyph, value); }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string ShortText { get => _short; private set => SetProperty(ref _short, value); }
    public string Detail { get => _detail; private set => SetProperty(ref _detail, value); }
    public string MoreText { get => _more; private set => SetProperty(ref _more, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public bool HasProgress { get => _hasProgress; private set => SetProperty(ref _hasProgress, value); }
    public ObservableCollection<TaskRowViewModel> Rows { get; } = [];

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => string.Format(Strings.A11y_Running, Title, Detail) + (MoreText.Length > 0 ? " " + MoreText : "");

    public void Update(TaskSummary s)
    {
        var p = s.Primary;
        Glyph = TaskText.Glyph(p.Kind);
        Title = p.Kind == TaskKind.FileCopy ? Strings.Copying_Files : p.Title;
        Detail = TaskText.Detail(p);
        HasProgress = p.Progress is not null;
        Progress = p.Progress ?? 0;
        // Compacto: "37 %" si hay porcentaje; si no, la cantidad de tareas o el tamaño descargado.
        ShortText = p.Progress is { } pr ? $"{pr:P0}" : s.RunningCount > 1 ? string.Format(Strings.Task_Count, s.RunningCount) : MetricFormat.Size(p.Bytes);
        MoreText = s.RunningCount > 1 ? string.Format(Strings.More_Count, s.RunningCount - 1) : "";

        Rows.Clear();
        foreach (var t in s.Running.Take(3)) Rows.Add(new TaskRowViewModel(t));
    }
}

/// <summary>Aviso de tarea terminada.</summary>
public sealed class TaskDoneViewModel : ObservableObject, IIslandAware, IAccessibleActivity
{
    public TaskDoneViewModel(BackgroundTask t, Action<string> showInFolder, Action close)
    {
        Glyph = t.State == TaskState.Failed ? "\uEA39" : "\uE73E";
        IsFailed = t.State == TaskState.Failed;
        Title = (t.Kind, t.State) switch
        {
            (_, TaskState.Failed) => t.Kind == TaskKind.Install ? Strings.Done_InstallFailed : Strings.Done_Failed,
            (TaskKind.Download, _) => Strings.Done_Download,
            (TaskKind.FileCopy, _) => Strings.Done_Copy,
            (TaskKind.Install, TaskState.Ended) => Strings.Done_InstallerClosed,
            _ => BackgroundTaskIds.IsRemoval(t) ? Strings.Done_Uninstall : Strings.Done_Install,
        };
        Detail = t.Kind == TaskKind.FileCopy ? "" : t.Title + (t.Kind == TaskKind.Install && t.Detail is { } d ? $" · {d}" : "");
        CanShowInFolder = t.Kind == TaskKind.Download && t.State == TaskState.Completed && t.Location is not null;
        ShowInFolderCommand = new RelayCommand(() => { if (t.Location is not null) showInFolder(t.Location); });
        CloseCommand = new RelayCommand(close);
    }

    public string Glyph { get; }
    public bool IsFailed { get; }
    public bool IsOk => !IsFailed;
    public string Title { get; }
    public string Detail { get; }
    public bool CanShowInFolder { get; }
    public ICommand ShowInFolderCommand { get; }
    public ICommand CloseCommand { get; }
    public bool IsBeingRead { get; private set; }

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => Detail.Length > 0 ? string.Format(Strings.A11y_Done, Title, Detail) : Title;

    public void OnIslandStateChanged(IslandState state, bool isShown) =>
        IsBeingRead = isShown && state == IslandState.Expanded;
}
