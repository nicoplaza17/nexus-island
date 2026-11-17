using System.Globalization;
using NexusIsland.Core.Mvvm;

namespace NexusIsland.App.Island;

/// <summary>Actividad de reposo: la hora y la fecha (se muestra cuando ningún módulo tiene nada).</summary>
public sealed class ClockViewModel : ObservableObject
{
    public const string ModuleId = "clock";
    public const string ActivityKey = "clock";

    private string _time = "--:--";
    private string _date = "";
    private int _lastMinute = -1;

    public string Time { get => _time; private set => SetProperty(ref _time, value); }
    public string Date { get => _date; private set => SetProperty(ref _date, value); }
    private string _hint = "Arrastrá para mover";

    public string Hint { get => _hint; private set => SetProperty(ref _hint, value); }

    // Corto: abajo a la derecha está el botón de herramientas y al centro los puntitos de página.
    public void SetHotkeyText(string hotkey) => Hint = $"{hotkey} oculta la isla";

    /// <summary>Actualiza solo si cambió el minuto (así no se redibuja la isla cada segundo).</summary>
    public void Refresh(bool force = false)
    {
        var now = DateTime.Now;
        if (!force && now.Minute == _lastMinute) return;
        _lastMinute = now.Minute;

        var culture = CultureInfo.CurrentCulture;
        Time = now.ToString("HH:mm", culture);
        var date = $"{now.ToString("dddd", culture)}, {now.ToString(culture.DateTimeFormat.MonthDayPattern, culture)}";
        Date = culture.TextInfo.ToUpper(date[0]) + date[1..];
    }
}
