using System.Text.Json;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Storage;

namespace NexusIsland.Core.Tools;

public sealed record Reminder(Guid Id, string Text, DateTimeOffset DueAt)
{
    public static Reminder Create(string text, DateTimeOffset dueAt) => new(Guid.NewGuid(), text.Trim(), dueAt);
}

/// <summary>
/// Recordatorios pendientes, guardados en reminders.json (sobreviven a reinicios).
/// Si la PC estaba apagada cuando vencía uno, avisa al volver (figura como vencido).
/// No es thread-safe: usar desde el hilo de la interfaz.
/// </summary>
public sealed class ReminderStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _path;
    private readonly ILogger? _logger;
    private readonly List<Reminder> _items;

    public ReminderStore(string path, ILogger<ReminderStore>? logger = null)
    {
        _path = path;
        _logger = logger;
        _items = Load();
    }

    /// <summary>Ordenados por vencimiento.</summary>
    public IReadOnlyList<Reminder> Pending => _items;

    public event Action? Changed;

    public Reminder Add(string text, DateTimeOffset dueAt)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("El recordatorio necesita un texto.", nameof(text));
        var reminder = Reminder.Create(text, dueAt);
        _items.Add(reminder);
        SortAndSave();
        return reminder;
    }

    public bool Remove(Guid id)
    {
        if (_items.RemoveAll(r => r.Id == id) == 0) return false;
        SortAndSave();
        return true;
    }

    /// <summary>Pospone (cambia la hora de vencimiento).</summary>
    public void Snooze(Guid id, DateTimeOffset newDueAt)
    {
        var index = _items.FindIndex(r => r.Id == id);
        if (index < 0) return;
        _items[index] = _items[index] with { DueAt = newDueAt };
        SortAndSave();
    }

    /// <summary>Los que ya vencieron (para avisar), del más viejo al más nuevo.</summary>
    public IReadOnlyList<Reminder> Due(DateTimeOffset now) => _items.Where(r => r.DueAt <= now).ToList();

    public DateTimeOffset? NextDue => _items.Count > 0 ? _items[0].DueAt : null;

    private void SortAndSave()
    {
        _items.Sort((a, b) => a.DueAt.CompareTo(b.DueAt));
        try
        {
            AtomicFile.WriteAllText(_path, JsonSerializer.Serialize(_items, JsonOptions));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "No se pudieron guardar los recordatorios");
        }
        Changed?.Invoke();
    }

    private List<Reminder> Load()
    {
        if (!File.Exists(_path)) return [];
        try
        {
            var items = JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(_path), JsonOptions) ?? [];
            items.RemoveAll(r => string.IsNullOrWhiteSpace(r.Text));
            items.Sort((a, b) => a.DueAt.CompareTo(b.DueAt));
            _logger?.LogInformation("{Count} recordatorios pendientes", items.Count);
            return items;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            try { File.Copy(_path, _path + ".bad", overwrite: true); } catch (IOException) { }
            _logger?.LogWarning(ex, "Archivo de recordatorios dañado: se respaldó como .bad");
            return [];
        }
    }
}
