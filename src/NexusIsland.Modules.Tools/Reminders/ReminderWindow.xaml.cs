using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using NexusIsland.Core.Tools;

using NexusIsland.Modules.Tools.Properties;

namespace NexusIsland.Modules.Tools.Reminders;

/// <summary>Ventana para crear y ver recordatorios (ventana normal: acá sí se puede escribir).</summary>
public partial class ReminderWindow : Window
{
    private readonly ReminderStore _store;

    public ReminderWindow(ReminderStore store)
    {
        _store = store;
        ThemeMode = ThemeMode.System; // tema Fluent (Windows 11), igual que la Configuración
        InitializeComponent();

        TimeInput.Text = DateTime.Now.AddHours(1).ToString("HH:00");
        AddButton.Click += (_, _) => Add();
        MinutesInput.GotFocus += (_, _) => InMinutesRadio.IsChecked = true;
        TimeInput.GotFocus += (_, _) => AtTimeRadio.IsChecked = true;
        _store.Changed += RefreshList;
        Closed += (_, _) => _store.Changed -= RefreshList;
        Loaded += (_, _) => ReminderText.Focus();
        RefreshList();
    }

    private void Add()
    {
        var text = ReminderText.Text.Trim();
        if (text.Length == 0)
        {
            Status.Text = Strings.Reminders_NeedText;
            ReminderText.Focus();
            return;
        }

        if (!TryGetDueTime(out var due, out var error))
        {
            Status.Text = error;
            return;
        }

        _store.Add(text, due);
        ReminderText.Clear();
        Status.Text = string.Format(Strings.Reminders_Scheduled, due.LocalDateTime.ToString("HH:mm"), due.Date > DateTimeOffset.Now.Date ? Strings.Reminders_Tomorrow : "");
        ReminderText.Focus();
    }

    private bool TryGetDueTime(out DateTimeOffset due, out string error)
    {
        var now = DateTimeOffset.Now;
        due = default;
        error = "";

        if (InMinutesRadio.IsChecked == true)
        {
            if (!int.TryParse(MinutesInput.Text.Trim(), out var minutes) || minutes < 1 || minutes > 10_080)
            {
                error = Strings.Reminders_BadMinutes;
                return false;
            }
            due = now.AddMinutes(minutes);
            return true;
        }

        if (!TimeSpan.TryParseExact(TimeInput.Text.Trim(), [@"h\:mm", @"hh\:mm", "%h"], CultureInfo.InvariantCulture, out var time)
            || time >= TimeSpan.FromDays(1))
        {
            error = Strings.Reminders_BadTime;
            return false;
        }
        var today = new DateTimeOffset(now.Date + time, now.Offset);
        due = today > now ? today : today.AddDays(1); // si ya pasó, es mañana
        return true;
    }

    private void RefreshList()
    {
        PendingList.ItemsSource = _store.Pending.Select(r => new
        {
            r.Id,
            r.Text,
            When = r.DueAt.LocalDateTime.Date == DateTime.Today
                ? r.DueAt.LocalDateTime.ToString("HH:mm")
                : r.DueAt.LocalDateTime.ToString("dd/MM HH:mm"),
            DeleteLabel = string.Format(Strings.Reminders_DeleteA11y, r.Text),
        }).ToList();
        EmptyText.Visibility = _store.Pending.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: Guid id }) _store.Remove(id);
    }
}
