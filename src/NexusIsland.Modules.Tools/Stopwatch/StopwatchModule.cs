using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Mvvm;
using NexusIsland.Core.Tools;

namespace NexusIsland.Modules.Tools.Stopwatch;

/// <summary>Cronómetro con vueltas. Queda en la isla hasta que se cierra.</summary>
public sealed class StopwatchModule(ILogger<StopwatchModule> logger) : IIslandModule, IQuickActionProvider
{
    private const string Key = "stopwatch";
    private IActivityHost? _host;
    private StopwatchViewModel? _vm;

    public string Id => "stopwatch";
    public string DisplayName => "Cronómetro";
    public Uri? TemplatesUri => Glyphs.TemplatesUri;

    public Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _vm = new StopwatchViewModel();
        _vm.Closed += () =>
        {
            _host.Remove(Key);
            logger.LogInformation("Cronómetro cerrado");
        };
        return Task.CompletedTask;
    }

    public IEnumerable<QuickAction> GetQuickActions() =>
    [
        new("stopwatch.start", "Cronómetro", Glyphs.Stopwatch, 20, () =>
        {
            if (_vm is null || _host is null) return;
            _vm.StartFresh();
            _host.Publish(new IslandActivity(Key, Id, ActivityPriority.Active, _vm));
            logger.LogInformation("Cronómetro iniciado");
        }),
    ];

    public void Dispose() => _vm?.Stop();
}

public sealed class StopwatchViewModel : ObservableObject
{
    private readonly StopwatchClock _clock = new();
    private readonly DispatcherTimer _tick = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(250) };
    private string _elapsed = "0:00";

    public StopwatchViewModel()
    {
        _tick.Tick += (_, _) => Refresh();
        StartPauseCommand = new RelayCommand(() =>
        {
            var now = DateTimeOffset.Now;
            if (_clock.IsRunning) { _clock.Pause(now); _tick.Stop(); }
            else { _clock.Start(now); _tick.Start(); }
            Refresh();
        });
        LapCommand = new RelayCommand(() =>
        {
            _clock.Lap(DateTimeOffset.Now);
            Laps.Clear();
            for (var i = 0; i < Math.Min(3, _clock.Laps.Count); i++)
                Laps.Add($"Vuelta {_clock.Laps.Count - i}   {StopwatchClock.Format(_clock.Laps[i])}");
        }, () => _clock.IsRunning);
        CloseCommand = new RelayCommand(() =>
        {
            _clock.Reset();
            _tick.Stop();
            Laps.Clear();
            Closed?.Invoke();
        });
    }

    public event Action? Closed;

    public string ElapsedText { get => _elapsed; private set => SetProperty(ref _elapsed, value); }
    public bool IsRunning => _clock.IsRunning;
    public string StartPauseGlyph => _clock.IsRunning ? Glyphs.Pause : Glyphs.Play;
    public string StartPauseLabel => _clock.IsRunning ? "Pausar" : "Seguir";

    /// <summary>Las últimas 3 vueltas, la más reciente primero.</summary>
    public ObservableCollection<string> Laps { get; } = [];

    public RelayCommand StartPauseCommand { get; }
    public RelayCommand LapCommand { get; }
    public ICommand CloseCommand { get; }

    public void StartFresh()
    {
        _clock.Reset();
        Laps.Clear();
        _clock.Start(DateTimeOffset.Now);
        _tick.Start();
        Refresh();
    }

    public void Stop() => _tick.Stop();

    private void Refresh()
    {
        ElapsedText = StopwatchClock.Format(_clock.Elapsed(DateTimeOffset.Now));
        OnPropertyChanged(nameof(IsRunning));
        OnPropertyChanged(nameof(StartPauseGlyph));
        OnPropertyChanged(nameof(StartPauseLabel));
        LapCommand.RaiseCanExecuteChanged();
    }
}
