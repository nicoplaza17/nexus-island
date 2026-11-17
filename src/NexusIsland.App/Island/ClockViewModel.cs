using System.Globalization;
using NexusIsland.App.Properties;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Mvvm;

namespace NexusIsland.App.Island;

/// <summary>Actividad de reposo: la hora y la fecha (se muestra cuando ningún módulo tiene nada).</summary>
public sealed class ClockViewModel : ObservableObject, IAccessibleActivity
{
    public const string ModuleId = "clock";
    public const string ActivityKey = "clock";

    private string _time = "--:--";
    private string _date = "";
    private int _lastMinute = -1;

    public string Time { get => _time; private set => SetProperty(ref _time, value); }
    public string Date { get => _date; private set => SetProperty(ref _date, value); }
    private string _hint = Strings.Clock_DragHint;

    public string Hint { get => _hint; private set => SetProperty(ref _hint, value); }

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => string.Format(Strings.A11y_Clock, Time, Date);

    // Corto: abajo a la derecha está el botón de herramientas y al centro los puntitos de página.
    public void SetHotkeyText(string hotkey) => Hint = string.Format(Strings.Clock_HotkeyHint, hotkey);

    /// <summary>Actualiza solo si cambió el minuto (así no se redibuja la isla cada segundo).</summary>
    public void Refresh(bool force = false)
    {
        var now = DateTime.Now;
        if (!force && now.Minute == _lastMinute) return;
        _lastMinute = now.Minute;

        // Nombres de día y mes en el idioma de la interfaz (si el usuario eligió uno distinto al regional).
        var culture = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == CultureInfo.CurrentCulture.TwoLetterISOLanguageName
            ? CultureInfo.CurrentCulture
            : CultureInfo.CurrentUICulture;
        Time = now.ToString("HH:mm", culture);
        var date = $"{now.ToString("dddd", culture)}, {now.ToString(culture.DateTimeFormat.MonthDayPattern, culture)}";
        Date = culture.TextInfo.ToUpper(date[0]) + date[1..];
    }
}
