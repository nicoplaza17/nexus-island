using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Mvvm;
using NexusIsland.Core.Security;
using NexusIsland.Modules.Security.Properties;

namespace NexusIsland.Modules.Security;

/// <summary>
/// Vigilante de seguridad: escucha los registros de Windows, aplica reglas fijas (SecurityRules) y avisa
/// en la isla. "Explicar con IA" abre una ventana donde un modelo local (LM Studio) explica la alerta.
/// Privacidad: en el log de la isla solo queda qué regla saltó, nunca los datos del evento.
/// </summary>
public sealed class SecurityModule(ISecurityEventSource source, ISecurityExplainer explainer, ILogger<SecurityModule> logger)
    : IIslandModule, IQuickActionProvider
{
    public const string ModuleId = "security";
    private const string AlertKey = "security.alert";
    private const string PanelKey = "security.panel";
    private const string PanelModuleId = "security.panel";
    private const string ShieldGlyph = "\uEA18";
    private static readonly TimeSpan AlertAttention = TimeSpan.FromSeconds(12);

    private readonly SecurityRules _rules = new();
    private readonly object _gate = new();
    private IActivityHost? _host;
    private SynchronizationContext? _ui;
    private SecurityPanelViewModel? _panel;

    public string Id => ModuleId;
    public string DisplayName => Strings.Module_Name;
    public Uri? TemplatesUri { get; } = new("pack://application:,,,/NexusIsland.Modules.Security;component/SecurityTemplates.xaml", UriKind.Absolute);

    public Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _ui = SynchronizationContext.Current ?? throw new InvalidOperationException("Iniciar en el hilo de la interfaz");
        _panel = new SecurityPanelViewModel(vm => OpenWindow(vm, autoExplain: false));
        source.EventArrived += OnEvent;
        source.Start();
        _panel.SetStatus(source.ActiveLogs.Count, SecurityLogs.WatchedIds.Count, source.UnavailableLogs.Contains(SecurityLogs.Security));
        return Task.CompletedTask;
    }

    public IEnumerable<QuickAction> GetQuickActions() =>
        [new("security.panel", Strings.Module_Name, ShieldGlyph, 70, OpenPanel)];

    private void OpenPanel()
    {
        if (_panel is not null) _host?.Publish(new IslandActivity(PanelKey, PanelModuleId, ActivityPriority.Panel, _panel));
    }

    /// <summary>Llega en un hilo de Windows: las reglas se evalúan acá y la alerta se muestra en el hilo de la interfaz.</summary>
    private void OnEvent(SecurityEvent e)
    {
        SecurityAlert? alert;
        lock (_gate) alert = _rules.Process(e);
        if (alert is null) return;
        logger.LogInformation("Seguridad: alerta {Rule} ({Severity}, {Mitre})", alert.RuleId, alert.Severity, alert.Mitre);
        _ui?.Post(_ => Show(alert), null);
    }

    private void Show(SecurityAlert alert)
    {
        if (_host is null || _panel is null) return;
        var vm = new SecurityAlertViewModel(alert, explainer,
            explain: v => OpenWindow(v, autoExplain: true),
            dismiss: () => _host.Remove(AlertKey),
            history: () => { _host.Remove(AlertKey); OpenPanel(); });
        _panel.Add(vm);
        if (alert.Severity == AlertSeverity.Info) return; // solo historial

        var priority = alert.Severity == AlertSeverity.Critical ? ActivityPriority.Urgent : ActivityPriority.Active + 50;
        _host.Publish(new IslandActivity(AlertKey, ModuleId, priority, vm));
        _host.RequestAttention(AlertKey, AlertAttention);
        // Diagnóstico: NEXUS_SECURITY_AUTOEXPLAIN=1 abre la explicación sola (para probar sin tocar la isla).
        if (Environment.GetEnvironmentVariable("NEXUS_SECURITY_AUTOEXPLAIN") == "1") OpenWindow(vm, autoExplain: true);
    }

    private void OpenWindow(SecurityAlertViewModel vm, bool autoExplain)
    {
        if (vm.Window is { IsLoaded: true } open)
        {
            open.Activate();
        }
        else
        {
            vm.Window = new SecurityWindow(vm);
            vm.Window.Show();
            vm.Window.Activate();
        }
        if (autoExplain) vm.StartExplaining();
    }

    public void Dispose() => source.EventArrived -= OnEvent;
}

/// <summary>Una alerta: lo que muestra la isla y la ventana de detalle (con la explicación de la IA).</summary>
public sealed class SecurityAlertViewModel : ObservableObject, IAccessibleActivity
{
    private readonly ISecurityExplainer _explainer;
    private readonly StringBuilder _explanation = new();
    private string _stage = "";
    private bool _explaining, _copied;
    private CancellationTokenSource? _cts;

    public SecurityAlertViewModel(SecurityAlert alert, ISecurityExplainer explainer,
        Action<SecurityAlertViewModel> explain, Action dismiss, Action history)
    {
        Alert = alert;
        _explainer = explainer;
        ExplainCommand = new RelayCommand(() => explain(this));
        DismissCommand = new RelayCommand(dismiss);
        HistoryCommand = new RelayCommand(history);
        StartExplainCommand = new RelayCommand(StartExplaining);
        CopyCommand = new RelayCommand(Copy);
    }

    public SecurityAlert Alert { get; }
    public Window? Window { get; set; }

    public string Title => Alert.Title;
    public string Detail => Alert.Detail;
    public string Evidence => Alert.Evidence;
    public string TimeText => Alert.Time.LocalDateTime.ToString("HH:mm");
    public string MitreText => string.Format(Strings.Mitre, Alert.Mitre);
    public string SeverityText => Alert.Severity switch
    {
        AlertSeverity.Critical => Strings.Severity_Critical,
        AlertSeverity.Warning => Strings.Severity_Warning,
        _ => Strings.Severity_Info,
    };
    public string HeaderLine => $"{TimeText} · {SeverityText} · {MitreText}";
    public bool IsCritical => Alert.Severity == AlertSeverity.Critical;
    public bool IsWarning => Alert.Severity == AlertSeverity.Warning;
    public bool IsInfo => Alert.Severity == AlertSeverity.Info;
    public string ModelLabel => string.Format(Strings.Window_Explanation, _explainer.ModelName);

    public string Explanation => _explanation.ToString();
    public string Stage { get => _stage; private set => SetProperty(ref _stage, value); }
    public bool IsExplaining { get => _explaining; private set { if (SetProperty(ref _explaining, value)) OnPropertyChanged(nameof(CanExplain)); } }
    public bool CanExplain => !_explaining && _explanation.Length == 0;
    public string CopyLabel => _copied ? Strings.Copied : Strings.Copy;

    public ICommand ExplainCommand { get; }
    public ICommand DismissCommand { get; }
    public ICommand HistoryCommand { get; }
    public ICommand StartExplainCommand { get; }
    public ICommand CopyCommand { get; }

    /// <summary>Accesibilidad: lo que lee el Narrador.</summary>
    public string AccessibleSummary => string.Format(Strings.A11y_Alert, SeverityText, Title, Detail);

    /// <summary>Pide la explicación (una sola vez por alerta; si ya está, no la repite).</summary>
    public async void StartExplaining()
    {
        if (!CanExplain) return;
        var dispatcher = Application.Current.Dispatcher;
        IsExplaining = true;
        _cts = new CancellationTokenSource();
        try
        {
            await _explainer.ExplainAsync(Alert,
                text => dispatcher.BeginInvoke(() => { _explanation.Append(text); OnPropertyChanged(nameof(Explanation)); }),
                stage => dispatcher.BeginInvoke(() => Stage = stage switch
                {
                    ExplainStage.StartingServer => Strings.Stage_StartingServer,
                    ExplainStage.LoadingModel => Strings.Stage_LoadingModel,
                    _ => Strings.Stage_Writing,
                }),
                _cts.Token);
            Stage = Strings.Stage_Done;
        }
        catch (OperationCanceledException)
        {
            Stage = "";
        }
        catch (Exception ex)
        {
            Stage = string.Format(Strings.Explain_Failed, ex.Message, _explainer.ModelName);
            System.Diagnostics.Trace.WriteLine("Seguridad: falló la explicación: " + ex.GetType().Name);
        }
        finally
        {
            IsExplaining = false;
            OnPropertyChanged(nameof(CanExplain));
        }
    }

    public void CancelExplaining() => _cts?.Cancel();

    private void Copy()
    {
        var text = $"{Title}\n{HeaderLine}\n{Detail}\n\n{Strings.Window_Evidence}:\n{Evidence}\n\n{ModelLabel}:\n{Explanation}";
        try
        {
            Clipboard.SetText(text);
            _copied = true;
            OnPropertyChanged(nameof(CopyLabel));
        }
        catch (Exception) { /* portapapeles ocupado: no es grave */ }
    }
}

/// <summary>Panel "Seguridad": estado del vigilante y últimas alertas.</summary>
public sealed class SecurityPanelViewModel(Action<SecurityAlertViewModel> open) : ObservableObject, IAccessibleActivity
{
    private const int MaxItems = 50;
    private string _status = "";

    public ObservableCollection<SecurityHistoryItem> Items { get; } = [];
    public bool HasItems => Items.Count > 0;
    public bool IsEmpty => Items.Count == 0;
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }

    public string AccessibleSummary => $"{Strings.Module_Name}. {StatusText} {Items.Count}";

    public void SetStatus(int active, int total, bool missingSecurityLog) =>
        StatusText = string.Format(Strings.Status_Watching, active, total) + (missingSecurityLog ? " " + Strings.Status_NoSecurityLog : "");

    public void Add(SecurityAlertViewModel vm)
    {
        Items.Insert(0, new SecurityHistoryItem(vm, new RelayCommand(() => open(vm))));
        while (Items.Count > MaxItems) Items.RemoveAt(Items.Count - 1);
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(IsEmpty));
    }
}

public sealed record SecurityHistoryItem(SecurityAlertViewModel Alert, ICommand OpenCommand)
{
    public string OpenLabel => string.Format(Strings.Panel_OpenItemA11y, Alert.Title);
}
