namespace NexusIsland.Core.Tools;

/// <summary>Un texto copiado.</summary>
public sealed record ClipboardEntry(string Text, DateTimeOffset CopiedAt)
{
    /// <summary>Una sola línea, recortada, para mostrar en la lista.</summary>
    public string Preview
    {
        get
        {
            var oneLine = string.Join(' ', Text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
            return oneLine.Length <= 120 ? oneLine : oneLine[..117] + "…";
        }
    }
}

/// <summary>
/// Historial del portapapeles: solo texto, solo en memoria (nunca se guarda en disco),
/// sin duplicados (copiar algo que ya estaba lo sube al principio) y con un máximo de entradas.
/// </summary>
public sealed class ClipboardHistory
{
    public const int MaxTextLength = 100_000; // textos enormes no se guardan

    private readonly List<ClipboardEntry> _entries = [];

    public ClipboardHistory(int capacity = 20)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        Capacity = capacity;
    }

    public int Capacity { get; }

    /// <summary>El más reciente primero.</summary>
    public IReadOnlyList<ClipboardEntry> Entries => _entries;

    /// <summary>Agrega un texto. Devuelve false si se ignoró (vacío, solo espacios o demasiado largo).</summary>
    public bool Add(string? text, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength) return false;
        _entries.RemoveAll(e => e.Text == text);
        _entries.Insert(0, new ClipboardEntry(text, now));
        if (_entries.Count > Capacity) _entries.RemoveRange(Capacity, _entries.Count - Capacity);
        return true;
    }

    public void Clear() => _entries.Clear();
}
