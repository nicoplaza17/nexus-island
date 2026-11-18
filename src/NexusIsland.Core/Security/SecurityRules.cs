using System.Text;
using NexusIsland.Core.Properties;

namespace NexusIsland.Core.Security;

/// <summary>
/// Reglas fijas que deciden si un evento es sospechoso. La IA no decide: solo explica después.
/// Cada regla está basada en técnicas conocidas de MITRE ATT&amp;CK y en el formato real de los
/// eventos de Windows 11 (revisado en la PC antes de escribirlas).
/// </summary>
public sealed class SecurityRules
{
    public static readonly TimeSpan BruteForceWindow = TimeSpan.FromMinutes(2);
    public const int BruteForceThreshold = 10;
    private static readonly TimeSpan BruteForceMemory = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan Cooldown = TimeSpan.FromMinutes(10);

    private readonly Dictionary<string, List<(DateTimeOffset Time, string Account)>> _failures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _bruteSources = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTimeOffset> _lastAlert = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seenScriptBlocks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Procesa un evento. Devuelve una alerta o null. No es seguro para varios hilos a la vez.</summary>
    public SecurityAlert? Process(SecurityEvent e)
    {
        var alert = (e.Log, e.Id) switch
        {
            (SecurityLogs.Security, 4625) => FailedLogon(e),
            (SecurityLogs.Security, 4624) => SuccessfulLogon(e),
            (SecurityLogs.Security, 4720) => Make(e, "user-created", e.Get("TargetUserName"), AlertSeverity.Warning,
                Strings.Sec_UserCreated_Title, string.Format(Strings.Sec_UserCreated_Detail, Account(e, "Target"), Account(e, "Subject")), "T1136.001"),
            (SecurityLogs.Security, 4732) => AdminAdded(e),
            (SecurityLogs.Security, 1102) => Make(e, "log-cleared", "", AlertSeverity.Critical,
                Strings.Sec_LogCleared_Title, string.Format(Strings.Sec_LogCleared_Detail, Account(e, "Subject")), "T1070.001"),
            (SecurityLogs.System, 7045) => ServiceInstalled(e),
            (SecurityLogs.TaskScheduler, 106) => TaskRegistered(e),
            (SecurityLogs.Defender, 1116) => Make(e, "malware", e.Get("Threat Name") + e.Get("Path"), AlertSeverity.Critical,
                Strings.Sec_Malware_Title, string.Format(Strings.Sec_Malware_Detail, e.Get("Threat Name"), Short(e.Get("Path"))), "T1204"),
            (SecurityLogs.Defender, 1117) => Make(e, "defender-action", e.Get("Threat Name") + e.Get("Path"), AlertSeverity.Info,
                Strings.Sec_DefenderAction_Title, string.Format(Strings.Sec_DefenderAction_Detail, e.Get("Threat Name"), e.Get("Action Name")), "T1204"),
            (SecurityLogs.Defender, 5001) => Make(e, "realtime-off", "", AlertSeverity.Critical,
                Strings.Sec_RealtimeOff_Title, Strings.Sec_RealtimeOff_Detail, "T1562.001"),
            (SecurityLogs.Defender, 5007) => DefenderExclusion(e),
            (SecurityLogs.PowerShell, 4104) => SuspiciousPowerShell(e),
            (SecurityLogs.Firewall, 2082) => FirewallChanged(e),
            _ => null,
        };
        if (alert is null) return null;

        // Misma alerta (regla + origen) dentro de 10 min: no se repite (salvo que suba de gravedad).
        var cooldownKey = alert.RuleId + "|" + alert.Key + "|" + alert.Severity;
        if (_lastAlert.TryGetValue(cooldownKey, out var last) && e.Time - last < Cooldown) return null;
        _lastAlert[cooldownKey] = e.Time;
        return alert;
    }

    // ------------------------------------------------------------------ inicios de sesión

    private SecurityAlert? FailedLogon(SecurityEvent e)
    {
        var source = Source(e);
        if (!_failures.TryGetValue(source, out var list)) _failures[source] = list = [];
        list.Add((e.Time, e.Get("TargetUserName")));
        list.RemoveAll(f => e.Time - f.Time > BruteForceWindow);
        if (list.Count < BruteForceThreshold) return null;

        _bruteSources[source] = e.Time;
        var accounts = string.Join(", ", list.Select(f => f.Account).Where(a => a.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Take(5));
        return Make(e, "brute-force", source, AlertSeverity.Warning, Strings.Sec_BruteForce_Title,
            string.Format(Strings.Sec_BruteForce_Detail, list.Count, SourceName(source), accounts), "T1110",
            extra: $"intentos en 2 min: {list.Count}\ncuentas probadas: {accounts}");
    }

    private SecurityAlert? SuccessfulLogon(SecurityEvent e)
    {
        var source = Source(e);
        if (!_bruteSources.TryGetValue(source, out var when) || e.Time - when > BruteForceMemory) return null;
        var type = e.Get("LogonType");
        if (type is not ("3" or "10" or "2" or "7")) return null; // red, escritorio remoto, consola, desbloqueo
        return Make(e, "brute-success", source, AlertSeverity.Critical, Strings.Sec_BruteSuccess_Title,
            string.Format(Strings.Sec_BruteSuccess_Detail, Account(e, "Target"), SourceName(source)), "T1110 → T1078");
    }

    private static SecurityAlert? AdminAdded(SecurityEvent e)
    {
        if (!string.Equals(e.Get("TargetSid"), "S-1-5-32-544", StringComparison.OrdinalIgnoreCase)) return null; // solo "Administradores"
        var member = e.Get("MemberName") is { Length: > 1 } m ? m : e.Get("MemberSid");
        return Make(e, "admin-added", member, AlertSeverity.Critical, Strings.Sec_AdminAdded_Title,
            string.Format(Strings.Sec_AdminAdded_Detail, member, Account(e, "Subject")), "T1098");
    }

    /// <summary>Origen de un intento: la IP, o "este equipo" si es local.</summary>
    private static string Source(SecurityEvent e)
    {
        var ip = e.Get("IpAddress");
        return ip is "" or "-" or "::1" or "127.0.0.1" ? "local" : ip;
    }

    private static string SourceName(string source) => source == "local" ? Strings.Sec_LocalSource : source;

    private static string Account(SecurityEvent e, string prefix)
    {
        var user = e.Get(prefix + "UserName");
        var domain = e.Get(prefix + "DomainName");
        return domain.Length > 0 && domain != "-" ? $"{domain}\\{user}" : user;
    }

    // ------------------------------------------------------------------ persistencia

    private static readonly string[] SuspiciousServicePaths =
    [
        "powershell", "cmd.exe", "cmd /", "%comspec%", "mshta", "rundll32", "regsvr32", "wscript", "cscript",
        "\\users\\", "\\temp\\", "\\appdata\\", "\\downloads\\", ".ps1", ".bat", ".vbs", "http://", "https://",
    ];

    private static readonly string[] TrustedServicePaths =
    [
        "c:\\program files\\windowsapps\\", "c:\\windows\\system32\\driverstore\\",
    ];

    private static SecurityAlert ServiceInstalled(SecurityEvent e)
    {
        var path = e.Get("ImagePath");
        var lower = path.Trim('"', ' ').ToLowerInvariant();
        var severity = SuspiciousServicePaths.Any(lower.Contains) ? AlertSeverity.Critical
            : TrustedServicePaths.Any(lower.StartsWith) ? AlertSeverity.Info
            : AlertSeverity.Warning;
        return Make(e, "service", e.Get("ServiceName"), severity, Strings.Sec_Service_Title,
            string.Format(Strings.Sec_Service_Detail, e.Get("ServiceName"), Short(path)), "T1543.003");
    }

    private static readonly string[] KnownUpdaterTasks =
    [
        "googleupdate", "googleupdater", "microsoftedgeupdate", "onedrive", "adobe acrobat update", "operaautoupdate",
        "braveupdate", "mozilla\\firefox", "npcapwatchdog", "lenovo", "hp\\", "dell",
    ];

    private static SecurityAlert? TaskRegistered(SecurityEvent e)
    {
        var name = e.Get("TaskName");
        if (name.StartsWith("\\Microsoft\\", StringComparison.OrdinalIgnoreCase)) return null; // Windows crea estas todo el tiempo
        var lower = name.ToLowerInvariant();
        var severity = KnownUpdaterTasks.Any(lower.Contains) ? AlertSeverity.Info : AlertSeverity.Warning;
        return Make(e, "task", name, severity, Strings.Sec_Task_Title,
            string.Format(Strings.Sec_Task_Detail, name, e.Get("UserContext")), "T1053.005");
    }

    // ------------------------------------------------------------------ evasión de defensas

    private static SecurityAlert? DefenderExclusion(SecurityEvent e)
    {
        var value = e.Get("New Value");
        if (!value.Contains("\\Exclusions\\", StringComparison.OrdinalIgnoreCase)) return null; // otros cambios de configuración: normales
        var excluded = value[(value.IndexOf("\\Exclusions\\", StringComparison.OrdinalIgnoreCase) + 12)..];
        return Make(e, "defender-exclusion", excluded, AlertSeverity.Warning, Strings.Sec_Exclusion_Title,
            string.Format(Strings.Sec_Exclusion_Detail, Short(excluded)), "T1562.001");
    }

    private static SecurityAlert? FirewallChanged(SecurityEvent e)
    {
        // SettingType 1 = "Firewall activado"; SettingValue es un número de 4 bytes en hexadecimal (01000000 = sí).
        if (e.Get("SettingType") != "1" || !e.Get("SettingValue").StartsWith("00", StringComparison.Ordinal)) return null;
        var profiles = int.TryParse(e.Get("Profiles"), out var p) ? p : 0;
        var names = new List<string>();
        if ((profiles & 1) != 0) names.Add(Strings.Sec_Profile_Domain);
        if ((profiles & 2) != 0) names.Add(Strings.Sec_Profile_Private);
        if ((profiles & 4) != 0) names.Add(Strings.Sec_Profile_Public);
        return Make(e, "firewall-off", profiles.ToString(), AlertSeverity.Critical, Strings.Sec_FirewallOff_Title,
            string.Format(Strings.Sec_FirewallOff_Detail, string.Join(", ", names)), "T1562.004");
    }

    // ------------------------------------------------------------------ PowerShell

    private SecurityAlert? SuspiciousPowerShell(SecurityEvent e)
    {
        // Un script largo llega partido en varios eventos con el mismo ScriptBlockId: se evalúa una vez.
        var id = e.Get("ScriptBlockId");
        if (id.Length > 0 && !_seenScriptBlocks.Add(id + "#" + e.Get("MessageNumber"))) return null;
        if (_seenScriptBlocks.Count > 5000) _seenScriptBlocks.Clear();

        var signals = PowerShellSignals.Analyze(e.Get("ScriptBlockText"));
        if (signals.Severity is null) return null;
        // Clave = las señales encontradas: el mismo patrón (que Windows suele registrar en varios bloques) se avisa una vez.
        return Make(e, "powershell", string.Join("+", signals.Names.Order()), signals.Severity.Value, Strings.Sec_PowerShell_Title,
            string.Format(Strings.Sec_PowerShell_Detail, string.Join(", ", signals.Names)), "T1059.001",
            extra: "fragmento: " + Short(e.Get("ScriptBlockText").Replace('\n', ' '), 600));
    }

    // ------------------------------------------------------------------ armado

    private static SecurityAlert Make(SecurityEvent e, string rule, string key, AlertSeverity severity, string title, string detail,
        string mitre, string? extra = null)
    {
        var evidence = new StringBuilder();
        evidence.AppendLine($"registro: {e.Log} · evento {e.Id} · {e.Time:yyyy-MM-dd HH:mm:ss}");
        foreach (var (k, v) in e.Data)
        {
            if (k == "ScriptBlockText" || v.Length == 0 || v == "-") continue;
            evidence.AppendLine($"{k}: {Short(v, 300)}");
        }
        if (extra is not null) evidence.AppendLine(extra);
        return new SecurityAlert(rule, key, severity, title, detail, mitre, e.Time, evidence.ToString().Trim());
    }

    private static string Short(string s, int max = 90) => s.Length <= max ? s : s[..(max - 1)] + "…";
}

/// <summary>Señales de ataque en un script de PowerShell (lo que Windows registra en el evento 4104).</summary>
public static class PowerShellSignals
{
    private static readonly (string Name, string[] Tokens)[] Groups =
    [
        ("download", ["downloadstring", "downloadfile", "downloaddata", "net.webclient", "invoke-webrequest", "start-bitstransfer", "invoke-restmethod", "iwr ", "wget ", "curl "]),
        ("exec", ["iex ", "iex(", "| iex", "|iex", "invoke-expression", ".invoke(", "[scriptblock]::create"]),
        ("obfuscation", ["frombase64string", "-encodedcommand", " -enc ", "gzipstream", "deflatestream", "[char[]]", "-bxor"]),
        ("amsi", ["amsiutils", "amsiinitfailed", "amsiscanbuffer", "amsicontext"]),
        ("credentials", ["mimikatz", "sekurlsa", "invoke-mimikatz", "lsass", "get-gpppassword", "kerberoast"]),
        ("defender", ["disablerealtimemonitoring", "add-mppreference", "set-mppreference -disable", "exclusionpath"]),
        ("hidden", ["-windowstyle hidden", "-w hidden", "-nop -w"]),
    ];

    public sealed record Result(AlertSeverity? Severity, IReadOnlyList<string> Names);

    public static Result Analyze(string script)
    {
        var lower = script.ToLowerInvariant();
        var found = Groups.Where(g => g.Tokens.Any(lower.Contains)).Select(g => g.Name).ToHashSet();
        AlertSeverity? severity =
            found.Contains("amsi") || found.Contains("credentials") || found.Contains("defender")
            || (found.Contains("download") && found.Contains("exec")) ? AlertSeverity.Critical
            : found.Contains("obfuscation") && (found.Contains("exec") || found.Contains("download")) ? AlertSeverity.Warning
            : null;
        var names = found.Select(n => n switch
        {
            "download" => Strings.Sec_Ind_Download,
            "exec" => Strings.Sec_Ind_Exec,
            "obfuscation" => Strings.Sec_Ind_Obfuscation,
            "amsi" => Strings.Sec_Ind_Amsi,
            "credentials" => Strings.Sec_Ind_Credentials,
            "defender" => Strings.Sec_Ind_Defender,
            _ => Strings.Sec_Ind_Hidden,
        }).ToList();
        return new Result(severity, names);
    }
}
