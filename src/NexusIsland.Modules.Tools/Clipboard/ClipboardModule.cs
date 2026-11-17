using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Mvvm;
using NexusIsland.Core.Tools;

namespace NexusIsland.Modules.Tools.Clipboard;

/// <summary>
/// Historial del portapapeles (últimos 20 textos). Privacidad:
///  - Solo en memoria: nunca se escribe en disco y se borra al cerrar la isla.
///  - Respeta a los gestores de contraseñas (ver ClipboardMonitor).
///  - El contenido nunca va al log.
/// </summary>
public sealed class ClipboardModule(IClipboardMonitor monitor, ILogger<ClipboardModule> logger) : IIslandModule, IQuickActionProvider
{
    private const string Key = "clipboard.panel";
    private readonly ClipboardHistory _history = new(capacity: 20);
    private IActivityHost? _host;
    private ClipboardViewModel? _vm;

    public string Id => "clipboard";
    public string DisplayName => "Portapapeles";
    public Uri? TemplatesUri => Glyphs.TemplatesUri;

    public Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _vm = new ClipboardViewModel(Copy, Clear);
        monitor.TextCopied += OnTextCopied;
        monitor.Start();
        return Task.CompletedTask;
    }

    public IEnumerable<QuickAction> GetQuickActions() =>
    [
        new("clipboard.open", "Portapapeles", Glyphs.Clipboard, 40, () =>
        {
            if (_vm is null || _host is null) return;
            _vm.Refresh(_history.Entries, DateTimeOffset.Now);
            _host.Publish(new IslandActivity(Key, Id, ActivityPriority.Panel, _vm));
        }),
    ];

    private void OnTextCopied(string text)
    {
        if (_history.Add(text, DateTimeOffset.Now)) _vm?.Refresh(_history.Entries, DateTimeOffset.Now);
    }

    private void Copy(ClipboardEntry entry)
    {
        if (monitor.TrySetText(entry.Text))
        {
            logger.LogInformation("Texto del historial copiado de nuevo");
            _host?.Remove(Key);
        }
        else
        {
            _vm?.ShowMessage("El portapapeles está ocupado por otra app. Probá de nuevo.");
        }
    }

    private void Clear()
    {
        _history.Clear();
        _vm?.Refresh(_history.Entries, DateTimeOffset.Now);
        logger.LogInformation("Historial del portapapeles borrado");
    }

    public void Dispose()
    {
        monitor.TextCopied -= OnTextCopied;
        monitor.Stop();
        _history.Clear();
    }
}

public sealed class ClipboardItemViewModel(ClipboardEntry entry, string ago, ICommand copy)
{
    public string Preview => entry.Preview;
    public string Ago => ago;
    public ClipboardEntry Entry => entry;
    public ICommand CopyCommand => copy;
}

public sealed class ClipboardViewModel : ObservableObject
{
    private readonly Action<ClipboardEntry> _copy;
    private string _message = "";

    public ClipboardViewModel(Action<ClipboardEntry> copy, Action clear)
    {
        _copy = copy;
        ClearCommand = new RelayCommand(clear);
    }

    public ObservableCollection<ClipboardItemViewModel> Items { get; } = [];
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => Items.Count == 0;
    public string CountText => Items.Count == 1 ? "1 texto" : $"{Items.Count} textos";
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public ICommand ClearCommand { get; }

    public void Refresh(IReadOnlyList<ClipboardEntry> entries, DateTimeOffset now)
    {
        Items.Clear();
        foreach (var entry in entries)
        {
            var copy = new RelayCommand(() => _copy(entry));
            Items.Add(new ClipboardItemViewModel(entry, Ago(now - entry.CopiedAt), copy));
        }
        Message = "";
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(CountText));
    }

    public void ShowMessage(string message) => Message = message;

    private static string Ago(TimeSpan t) => t.TotalMinutes < 1 ? "ahora"
        : t.TotalHours < 1 ? $"{(int)t.TotalMinutes} min"
        : t.TotalDays < 1 ? $"{(int)t.TotalHours} h"
        : $"{(int)t.TotalDays} d";
}
