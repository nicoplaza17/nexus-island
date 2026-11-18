using System.Diagnostics.Eventing.Reader;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Security;

namespace NexusIsland.Services.Windows.Security;

/// <summary>
/// Escucha los registros de eventos de Windows en tiempo real (EventLogWatcher: Windows avisa cada evento
/// nuevo; no se revisa periódicamente). Solo pide los eventos que usan las reglas. Los eventos viejos no se
/// procesan: el vigilante empieza a contar desde que abre la isla.
/// El registro "Security" solo se puede leer como administrador o siendo miembro de "Lectores del registro
/// de eventos"; si no hay permiso, ese registro queda afuera y el resto funciona igual.
/// </summary>
public sealed class SecurityEventWatcher(ILogger<SecurityEventWatcher> logger) : ISecurityEventSource
{
    private readonly List<EventLogWatcher> _watchers = [];
    private readonly List<string> _active = [], _unavailable = [];

    public event Action<SecurityEvent>? EventArrived;
    public IReadOnlyList<string> ActiveLogs => _active;
    public IReadOnlyList<string> UnavailableLogs => _unavailable;

    public void Start()
    {
        if (_watchers.Count > 0) return;
        foreach (var (log, ids) in SecurityLogs.WatchedIds)
        {
            var xpath = "*[System[(" + string.Join(" or ", ids.Select(id => $"EventID={id}")) + ")]]";
            try
            {
                // Suscribirse sin permiso NO da error (simplemente nunca llegan eventos): se prueba leer uno antes.
                using (var probe = new EventLogReader(new EventLogQuery(log, PathType.LogName) { ReverseDirection = true }))
                    probe.ReadEvent()?.Dispose();

                var watcher = new EventLogWatcher(new EventLogQuery(log, PathType.LogName, xpath));
                watcher.EventRecordWritten += (_, args) => OnRecord(log, args);
                watcher.Enabled = true;
                _watchers.Add(watcher);
                _active.Add(log);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or EventLogException or InvalidOperationException)
            {
                _unavailable.Add(log);
                logger.LogInformation("Seguridad: no se puede leer el registro {Log} ({Reason})", log, ex.GetType().Name);
            }
        }
        logger.LogInformation("Seguridad: vigilando {Count} registros ({Logs})", _active.Count, string.Join(", ", _active.Select(Short)));
    }

    private void OnRecord(string log, EventRecordWrittenEventArgs args)
    {
        try
        {
            using var record = args.EventRecord;
            if (record is null) return;
            var e = Parse(log, record.Id, record.TimeCreated ?? DateTime.Now, record.ToXml());
            EventArrived?.Invoke(e);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Seguridad: evento ilegible en {Log}", log);
        }
    }

    /// <summary>Extrae los campos con nombre de EventData (o UserData, como el evento 1102).</summary>
    public static SecurityEvent Parse(string log, int id, DateTime time, string xml)
    {
        var doc = XDocument.Parse(xml);
        var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var root = doc.Root!;
        var eventData = root.Elements().FirstOrDefault(e => e.Name.LocalName == "EventData");
        if (eventData is not null)
        {
            foreach (var d in eventData.Elements().Where(e => e.Name.LocalName == "Data"))
            {
                var name = (string?)d.Attribute("Name");
                if (!string.IsNullOrEmpty(name)) data[name] = d.Value;
            }
        }
        var userData = root.Elements().FirstOrDefault(e => e.Name.LocalName == "UserData");
        if (userData is not null)
        {
            foreach (var leaf in userData.Descendants().Where(e => !e.HasElements))
                data[leaf.Name.LocalName] = leaf.Value;
        }
        return new SecurityEvent(log, id, new DateTimeOffset(time), data);
    }

    private static string Short(string log) => log.Split('/')[0].Replace("Microsoft-Windows-", "");

    public void Dispose()
    {
        foreach (var w in _watchers)
        {
            try { w.Enabled = false; w.Dispose(); } catch (Exception) { /* al salir */ }
        }
        _watchers.Clear();
    }
}
