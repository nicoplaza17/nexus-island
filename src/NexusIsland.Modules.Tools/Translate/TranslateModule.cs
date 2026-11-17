using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Mvvm;
using NexusIsland.Core.Tools;
using NexusIsland.Core.Translation;
using NexusIsland.Modules.Tools.Properties;

namespace NexusIsland.Modules.Tools.Translate;

/// <summary>
/// Traducir el texto copiado, inglés ↔ español, en la PC (sin Internet una vez bajado el modelo).
/// Privacidad: el texto y la traducción solo viven en memoria mientras el aviso está abierto; nunca van al log.
/// </summary>
public sealed class TranslateModule(ITranslationService translator, IClipboardMonitor clipboard, ILogger<TranslateModule> logger)
    : IIslandModule, IQuickActionProvider
{
    private const string Key = "translate.result";
    private IActivityHost? _host;
    private TranslateViewModel? _vm;
    private CancellationTokenSource? _cts;
    private string _source = "";
    private TranslationDirection _direction;
    private int _generation; // descarta resultados viejos (si se invirtió o cerró mientras traducía)

    public string Id => "translate";
    public string DisplayName => Strings.Translate_Name;
    public Uri? TemplatesUri => Glyphs.TemplatesUri;

    public Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _vm = new TranslateViewModel(Download, CancelDownload, Copy, Invert, Retry, Close);
        return Task.CompletedTask;
    }

    public IEnumerable<QuickAction> GetQuickActions() =>
        [new("translate.run", Strings.Translate_Name, Glyphs.Translate, 45, Run)];

    private void Run()
    {
        if (_vm is null || _host is null) return;
        CancelWork();
        var text = clipboard.TryGetText();
        if (string.IsNullOrWhiteSpace(text))
        {
            _source = "";
            _vm.ShowNoText();
        }
        else
        {
            _source = TranslationText.Prepare(text, out var truncated);
            _direction = TranslationText.Detect(_source);
            _vm.SetSource(_source, truncated);
            Start();
        }
        _host.Publish(new IslandActivity(Key, Id, ActivityPriority.Active, _vm));
    }

    /// <summary>Traduce, o pide permiso para bajar el modelo si todavía no está.</summary>
    private void Start()
    {
        if (_vm is null) return;
        _vm.SetDirection(_direction);
        if (translator.IsModelInstalled(_direction))
            _ = TranslateAsync();
        else
            _vm.AskDownload(translator.DownloadSize(_direction));
    }

    private async Task TranslateAsync()
    {
        if (_vm is null) return;
        var generation = ++_generation;
        _cts = new CancellationTokenSource();
        _vm.SetWorking();
        try
        {
            var result = await translator.TranslateAsync(_source, _direction, _cts.Token);
            if (generation != _generation) return;
            _vm.SetResult(result);
            _host?.RequestAttention(Key, TimeSpan.FromSeconds(8)); // si la isla se contrajo, se ve el resultado un momento
        }
        catch (OperationCanceledException) { /* se cerró o se invirtió */ }
        catch (Exception ex)
        {
            if (generation != _generation) return;
            logger.LogWarning("No se pudo traducir: {Error}", ex.GetType().Name); // sin el mensaje: podría incluir texto
            _vm.SetError(Strings.Translate_Failed);
        }
    }

    private async void Download()
    {
        if (_vm is null) return;
        var generation = ++_generation;
        _cts = new CancellationTokenSource();
        _vm.SetDownloading(0);
        var progress = new Progress<double>(p => { if (generation == _generation) _vm.SetDownloading(p); });
        try
        {
            await translator.DownloadModelAsync(_direction, progress, _cts.Token);
            if (generation != _generation) return;
            await TranslateAsync();
        }
        catch (OperationCanceledException)
        {
            if (generation == _generation) _vm.AskDownload(translator.DownloadSize(_direction));
        }
        catch (Exception)
        {
            if (generation == _generation) _vm.SetError(Strings.Translate_DownloadFailed);
        }
    }

    private void CancelDownload() => _cts?.Cancel();

    private void Invert()
    {
        CancelWork();
        _direction = _direction.Reverse();
        Start();
    }

    private void Retry()
    {
        CancelWork();
        Start();
    }

    private void Copy()
    {
        if (_vm is null) return;
        if (clipboard.TrySetText(_vm.Result))
        {
            logger.LogInformation("Traducción copiada");
            _vm.ShowCopied();
        }
        else
        {
            _vm.SetError(Strings.Translate_ClipboardBusy, keepResult: true);
        }
    }

    private void Close()
    {
        CancelWork();
        _source = "";
        _vm?.Clear();
        _host?.Remove(Key);
    }

    private void CancelWork()
    {
        _generation++;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    public void Dispose()
    {
        CancelWork(); // el servicio (y su proceso traductor) lo cierra el contenedor al salir
    }
}

public enum TranslateStage { NoText, NeedsModel, Downloading, Working, Done, Failed }

public sealed class TranslateViewModel : ObservableObject, IAccessibleActivity
{
    private TranslateStage _stage = TranslateStage.NoText;
    private string _source = "", _result = "", _message = "", _directionText = "", _downloadLabel = "", _shortStatus = "";
    private double _progress;
    private bool _truncated, _copied;

    public TranslateViewModel(Action download, Action cancelDownload, Action copy, Action invert, Action retry, Action close)
    {
        DownloadCommand = new RelayCommand(download);
        CancelDownloadCommand = new RelayCommand(cancelDownload);
        CopyCommand = new RelayCommand(copy);
        InvertCommand = new RelayCommand(invert);
        RetryCommand = new RelayCommand(retry);
        CloseCommand = new RelayCommand(close);
    }

    public ICommand DownloadCommand { get; }
    public ICommand CancelDownloadCommand { get; }
    public ICommand CopyCommand { get; }
    public ICommand InvertCommand { get; }
    public ICommand RetryCommand { get; }
    public ICommand CloseCommand { get; }

    public TranslateStage Stage
    {
        get => _stage;
        private set
        {
            if (!SetProperty(ref _stage, value)) return;
            foreach (var p in new[] { nameof(IsNoText), nameof(IsNeedsModel), nameof(IsDownloading), nameof(IsWorking), nameof(IsDone),
                         nameof(IsFailed), nameof(ShowMessage), nameof(CanInvert), nameof(HasResult) })
                OnPropertyChanged(p);
        }
    }

    public bool IsNoText => _stage == TranslateStage.NoText;
    public bool IsNeedsModel => _stage == TranslateStage.NeedsModel;
    public bool IsDownloading => _stage == TranslateStage.Downloading;
    public bool IsWorking => _stage == TranslateStage.Working;
    public bool IsDone => _stage == TranslateStage.Done;
    public bool IsFailed => _stage == TranslateStage.Failed;
    public bool HasResult => _stage == TranslateStage.Done || (_stage == TranslateStage.Failed && _result.Length > 0);
    public bool ShowMessage => !HasResult;
    public bool CanInvert => _stage is TranslateStage.Done or TranslateStage.NeedsModel;

    public string Source { get => _source; private set => SetProperty(ref _source, value); }
    public string Result { get => _result; private set => SetProperty(ref _result, value); }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string DirectionText { get => _directionText; private set => SetProperty(ref _directionText, value); }
    public string DownloadLabel { get => _downloadLabel; private set => SetProperty(ref _downloadLabel, value); }
    public string ShortStatus { get => _shortStatus; private set => SetProperty(ref _shortStatus, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string CopyLabel => _copied ? Strings.Translate_Copied : Strings.Translate_Copy;

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => _stage == TranslateStage.Done
        ? string.Format(Strings.A11y_Translation, DirectionText, Result)
        : $"{Strings.Translate_Name}. {Message}";

    public void SetSource(string source, bool truncated)
    {
        _truncated = truncated;
        Source = source.Replace('\n', ' ');
        Result = "";
    }

    public void SetDirection(TranslationDirection d)
    {
        _direction = d;
        DirectionText = d == TranslationDirection.EnglishToSpanish ? Strings.Translate_EnToEs : Strings.Translate_EsToEn;
    }

    private TranslationDirection _direction;

    public void ShowNoText()
    {
        Source = Result = "";
        DirectionText = "";
        Message = Strings.Translate_NoText;
        ShortStatus = "";
        Stage = TranslateStage.NoText;
    }

    public void AskDownload(long bytes)
    {
        var mb = Math.Round(bytes / 1048576.0);
        var phrase = _direction == TranslationDirection.EnglishToSpanish ? Strings.Translate_FromEnglish : Strings.Translate_FromSpanish;
        Message = string.Format(Strings.Translate_NeedsModel, phrase, mb);
        DownloadLabel = string.Format(Strings.Translate_Download, mb);
        ShortStatus = "↓";
        Stage = TranslateStage.NeedsModel;
    }

    public void SetDownloading(double progress)
    {
        Progress = progress;
        Message = string.Format(Strings.Translate_Downloading, progress);
        ShortStatus = $"{progress:P0}";
        Stage = TranslateStage.Downloading;
    }

    public void SetWorking()
    {
        Message = Strings.Translate_Working;
        ShortStatus = "…";
        Result = "";
        Stage = TranslateStage.Working;
    }

    public void SetResult(string result)
    {
        Result = _truncated ? result + "\n" + Strings.Translate_Truncated : result;
        _copied = false;
        OnPropertyChanged(nameof(CopyLabel));
        ShortStatus = DirectionText;
        Stage = TranslateStage.Done;
    }

    public void SetError(string message, bool keepResult = false)
    {
        if (!keepResult) Result = "";
        Message = message;
        ShortStatus = "!";
        Stage = TranslateStage.Failed;
    }

    public void ShowCopied()
    {
        _copied = true;
        OnPropertyChanged(nameof(CopyLabel));
    }

    /// <summary>Al cerrar: no queda texto en memoria.</summary>
    public void Clear()
    {
        Source = Result = Message = "";
        Stage = TranslateStage.NoText;
    }
}
