using System.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Island;
using NexusIsland.Core.Mvvm;
using NexusIsland.Core.Tools;

using NexusIsland.Modules.Tools.Properties;

namespace NexusIsland.Modules.Tools.Timer;

/// <summary>
/// Temporizador: un botón en el panel de herramientas abre un selector de minutos (1 a 180, con accesos
/// rápidos). Al terminar, alerta con sonido.
/// </summary>
public sealed class TimerModule(ILogger<TimerModule> logger) : IIslandModule, IQuickActionProvider
{
    private const string Key = "timer";
    private const string SetupKey = "timer.setup";
    private static readonly TimeSpan AlertAttention = TimeSpan.FromSeconds(12);

    private IActivityHost? _host;
    private TimerViewModel? _vm;
    private TimerSetupViewModel? _setup;

    public string Id => "timer";
    public string DisplayName => Strings.Timer_Name;
    public Uri? TemplatesUri => Glyphs.TemplatesUri;

    public Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _vm = new TimerViewModel();
        _vm.Finished += OnFinished;
        _vm.Restarted += () => Publish(ActivityPriority.Active);
        _vm.Closed += () =>
        {
            _host.Remove(Key);
            logger.LogInformation("Temporizador cerrado");
        };
        _setup = new TimerSetupViewModel(
            start: minutes =>
            {
                _host.Remove(SetupKey);
                StartTimer(TimeSpan.FromMinutes(minutes));
            },
            cancel: () => _host.Remove(SetupKey));
        return Task.CompletedTask;
    }

    /// <summary>Un solo botón: abre el selector con los minutos usados la última vez.</summary>
    public IEnumerable<QuickAction> GetQuickActions() =>
        [new("timer.setup", Strings.Timer_Name, Glyphs.Timer, 10, () =>
        {
            if (_setup is null) return;
            // Panel: se cierra solo si la isla se contrae (no queda un selector olvidado).
            _host?.Publish(new IslandActivity(SetupKey, SetupKey, ActivityPriority.Panel, _setup));
        })];

    private void StartTimer(TimeSpan duration)
    {
        if (_vm is null) return;
        _vm.Start(duration);
        Publish(ActivityPriority.Active);
        logger.LogInformation("Temporizador de {Minutes} min iniciado", duration.TotalMinutes);
    }

    private void OnFinished()
    {
        logger.LogInformation("Temporizador terminado");
        Publish(ActivityPriority.Urgent); // la alerta le gana a todo hasta que se atienda
        SystemSounds.Exclamation.Play();
        _host?.RequestAttention(Key, AlertAttention);
    }

    private void Publish(int priority) =>
        _host?.Publish(new IslandActivity(Key, Id, priority, _vm!));

    public void Dispose() => _vm?.Stop();
}

public sealed class TimerViewModel : ObservableObject, IAccessibleActivity
{
    private readonly CountdownTimer _timer = new();
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private string _remaining = "0:00", _title = Strings.Timer_Name;
    private double _progress;

    public TimerViewModel()
    {
        _tick.Tick += (_, _) => OnTick();
        PauseResumeCommand = new RelayCommand(PauseResume);
        AddMinuteCommand = new RelayCommand(() =>
        {
            var wasFinished = _timer.State == CountdownState.Finished;
            _timer.Add(TimeSpan.FromMinutes(1), DateTimeOffset.Now);
            if (wasFinished)
            {
                _tick.Start();
                Restarted?.Invoke();
            }
            Refresh();
        });
        CloseCommand = new RelayCommand(() =>
        {
            _timer.Cancel();
            _tick.Stop();
            Closed?.Invoke();
        });
    }

    public event Action? Finished;
    public event Action? Restarted;
    public event Action? Closed;

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string RemainingText { get => _remaining; private set => SetProperty(ref _remaining, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }

    public bool IsFinished => _timer.State == CountdownState.Finished;
    public bool IsNotFinished => !IsFinished;
    public bool IsPaused => _timer.State == CountdownState.Paused;
    public string PauseResumeGlyph => IsPaused ? Glyphs.Play : Glyphs.Pause;
    public string PauseResumeLabel => IsPaused ? Strings.Timer_Resume : Strings.Common_Pause;
    public string CloseLabel => IsFinished ? Strings.Common_Done : Strings.Common_Cancel;

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => IsFinished
        ? Strings.A11y_TimerDone
        : string.Format(IsPaused ? Strings.A11y_TimerPaused : Strings.A11y_TimerRunning, RemainingText);

    public ICommand PauseResumeCommand { get; }
    public ICommand AddMinuteCommand { get; }
    public ICommand CloseCommand { get; }

    public void Start(TimeSpan duration)
    {
        _timer.Start(duration, DateTimeOffset.Now);
        Title = string.Format(Strings.Timer_TitleFormat, CountdownTimer.Format(duration));
        _tick.Start();
        Refresh();
    }

    public void Stop() => _tick.Stop();

    private void PauseResume()
    {
        var now = DateTimeOffset.Now;
        if (_timer.State == CountdownState.Running) { _timer.Pause(now); _tick.Stop(); }
        else if (_timer.State == CountdownState.Paused) { _timer.Resume(now); _tick.Start(); }
        Refresh();
    }

    private void OnTick()
    {
        var now = DateTimeOffset.Now;
        if (_timer.CheckFinished(now))
        {
            _tick.Stop();
            Refresh();
            Finished?.Invoke();
            return;
        }
        Refresh();
    }

    private void Refresh()
    {
        var now = DateTimeOffset.Now;
        RemainingText = IsFinished ? Strings.Timer_TimesUp : CountdownTimer.Format(_timer.Remaining(now));
        Progress = IsFinished ? 1 : _timer.Progress(now);
        OnPropertyChanged(nameof(IsFinished));
        OnPropertyChanged(nameof(IsNotFinished));
        OnPropertyChanged(nameof(IsPaused));
        OnPropertyChanged(nameof(PauseResumeGlyph));
        OnPropertyChanged(nameof(PauseResumeLabel));
        OnPropertyChanged(nameof(CloseLabel));
    }
}
