using System.Media;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Mvvm;
using NexusIsland.Core.Tools;

namespace NexusIsland.Modules.Tools.Reminders;

/// <summary>
/// Recordatorios: se crean en una ventanita (en la isla no se puede escribir, porque no toma el foco),
/// se guardan en reminders.json y, al vencer, la isla avisa con sonido hasta que se atienden.
/// </summary>
public sealed class ReminderModule(ReminderStore store, ILogger<ReminderModule> logger) : IIslandModule, IQuickActionProvider
{
    private const string Key = "reminder.alert";
    private static readonly TimeSpan AlertAttention = TimeSpan.FromSeconds(12);
    private static readonly TimeSpan SnoozeTime = TimeSpan.FromMinutes(10);

    private readonly DispatcherTimer _check = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(5) };
    private IActivityHost? _host;
    private ReminderAlertViewModel? _alert;
    private ReminderWindow? _window;

    public string Id => "reminders";
    public string DisplayName => "Recordatorios";
    public Uri? TemplatesUri => Glyphs.TemplatesUri;

    public Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _alert = new ReminderAlertViewModel(Snooze, Complete);
        _check.Tick += (_, _) => CheckDue();
        _check.Start();
        CheckDue(); // si la PC estaba apagada cuando vencía alguno, avisa ahora
        return Task.CompletedTask;
    }

    public IEnumerable<QuickAction> GetQuickActions() =>
        [new("reminders.new", "Recordatorio", Glyphs.Bell, 30, OpenWindow)];

    private void OpenWindow()
    {
        if (_window is { IsLoaded: true })
        {
            _window.Activate();
            return;
        }
        _window = new ReminderWindow(store);
        _window.Closed += (_, _) => _window = null;
        _window.Show();
        _window.Activate();
    }

    private void CheckDue()
    {
        if (_host is null || _alert is null || _alert.Current is not null) return;
        var due = store.Due(DateTimeOffset.Now).FirstOrDefault();
        if (due is null) return;

        _alert.Show(due, DateTimeOffset.Now);
        _host.Publish(new IslandActivity(Key, Id, ActivityPriority.Urgent, _alert));
        SystemSounds.Asterisk.Play();
        _host.RequestAttention(Key, AlertAttention);
        logger.LogInformation("Recordatorio vencido ({Late})", _alert.IsLate ? "atrasado" : "a tiempo"); // el texto no se loguea
    }

    private void Snooze(Reminder reminder)
    {
        store.Snooze(reminder.Id, DateTimeOffset.Now + SnoozeTime);
        CloseAlert();
        logger.LogInformation("Recordatorio pospuesto {Minutes} min", SnoozeTime.TotalMinutes);
    }

    private void Complete(Reminder reminder)
    {
        store.Remove(reminder.Id);
        CloseAlert();
        logger.LogInformation("Recordatorio completado");
    }

    private void CloseAlert()
    {
        _alert?.Clear();
        _host?.Remove(Key);
        CheckDue(); // si había otro vencido, se muestra enseguida
    }

    public void Dispose()
    {
        _check.Stop();
        _window?.Close();
    }
}

public sealed class ReminderAlertViewModel : ObservableObject
{
    private Reminder? _current;
    private string _text = "", _when = "";
    private bool _isLate;

    public ReminderAlertViewModel(Action<Reminder> snooze, Action<Reminder> complete)
    {
        SnoozeCommand = new RelayCommand(() => { if (_current is not null) snooze(_current); });
        DoneCommand = new RelayCommand(() => { if (_current is not null) complete(_current); });
    }

    public Reminder? Current => _current;
    public string Text { get => _text; private set => SetProperty(ref _text, value); }
    public string WhenText { get => _when; private set => SetProperty(ref _when, value); }
    public bool IsLate { get => _isLate; private set => SetProperty(ref _isLate, value); }

    public ICommand SnoozeCommand { get; }
    public ICommand DoneCommand { get; }

    public void Show(Reminder reminder, DateTimeOffset now)
    {
        _current = reminder;
        Text = reminder.Text;
        IsLate = now - reminder.DueAt > TimeSpan.FromMinutes(2);
        var at = reminder.DueAt.LocalDateTime;
        WhenText = IsLate
            ? $"Vencía {(at.Date == DateTime.Today ? "hoy" : at.ToString("dd/MM"))} a las {at:HH:mm}"
            : $"Recordatorio · {at:HH:mm}";
    }

    public void Clear() => _current = null;
}
