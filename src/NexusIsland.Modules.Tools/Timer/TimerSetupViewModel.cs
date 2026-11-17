using System.Windows.Input;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Mvvm;
using NexusIsland.Core.Tools;
using NexusIsland.Modules.Tools.Properties;

namespace NexusIsland.Modules.Tools.Timer;

/// <summary>Panel para elegir los minutos del temporizador (− / + y accesos rápidos 5 · 10 · 15 · 25).</summary>
public sealed class TimerSetupViewModel : ObservableObject, IAccessibleActivity
{
    private int _minutes = TimerDuration.Default;

    public TimerSetupViewModel(Action<int> start, Action cancel)
    {
        LessCommand = new RelayCommand(() => Minutes = TimerDuration.Step(_minutes, -1));
        MoreCommand = new RelayCommand(() => Minutes = TimerDuration.Step(_minutes, +1));
        StartCommand = new RelayCommand(() => start(_minutes));
        CancelCommand = new RelayCommand(cancel);
        Presets = TimerDuration.Presets
            .Select(m => new TimerPreset(m, string.Format(Strings.Timer_MinutesShort, m), string.Format(Strings.Timer_PresetA11y, m),
                new RelayCommand(() => Minutes = m)))
            .ToList();
    }

    public int Minutes
    {
        get => _minutes;
        set
        {
            if (!SetProperty(ref _minutes, TimerDuration.Clamp(value))) return;
            OnPropertyChanged(nameof(MinutesText));
            OnPropertyChanged(nameof(AccessibleSummary));
        }
    }

    public string MinutesText => string.Format(Strings.Timer_MinutesShort, _minutes);
    public IReadOnlyList<TimerPreset> Presets { get; }

    public ICommand LessCommand { get; }
    public ICommand MoreCommand { get; }
    public ICommand StartCommand { get; }
    public ICommand CancelCommand { get; }

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => string.Format(Strings.A11y_TimerSetup, _minutes);
}

public sealed record TimerPreset(int Minutes, string Label, string AccessibleName, ICommand Command);
