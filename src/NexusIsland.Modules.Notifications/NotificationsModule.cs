using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Island;
using NexusIsland.Core.Mvvm;
using NexusIsland.Core.Notifications;
using NexusIsland.Core.Settings;

namespace NexusIsland.Modules.Notifications;

/// <summary>
/// Muestra en la isla las notificaciones de otras apps (WhatsApp, Outlook, Teams...).
/// Privacidad: el contenido vive solo en memoria (últimas 20), nunca en disco ni en el log,
/// y se puede ocultar el texto (solo "Nuevo mensaje de X") para compartir pantalla tranquilo.
/// </summary>
public sealed class NotificationsModule(INotificationSource source, SettingsStore settings, ILogger<NotificationsModule> logger)
    : IIslandModule, IQuickActionProvider
{
    public const string ModuleId = "notifications";
    public const string PanelModuleId = "notifications.panel";
    private const string CurrentKey = "notification.current";
    private const string PanelKey = "notification.panel";
    private const int NotificationPriority = 150; // entre una tarea activa (100) y una alerta (200)
    private static readonly TimeSpan Attention = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan VisibleFor = TimeSpan.FromSeconds(8);

    private readonly NotificationHistory _history = new(capacity: 20);
    private readonly DispatcherTimer _hide = new(DispatcherPriority.Background) { Interval = VisibleFor };
    private IActivityHost? _host;
    private SynchronizationContext? _ui;
    private NotificationViewModel? _current;
    private NotificationsPanelViewModel? _panel;

    public string Id => ModuleId;
    public string DisplayName => "Notificaciones";
    public Uri? TemplatesUri { get; } =
        new("pack://application:,,,/NexusIsland.Modules.Notifications;component/NotificationTemplates.xaml", UriKind.Absolute);

    public async Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _ui = SynchronizationContext.Current;
        _panel = new NotificationsPanelViewModel(OpenFromPanel, ClearHistory, RequestAccessAsync, OpenWindowsPrivacy);
        _hide.Tick += (_, _) => HideCurrentIfIdle();
        source.Arrived += n => _ui?.Post(_ => Show(n), null);
        await source.StartAsync();
        logger.LogInformation("Notificaciones: {Access}", source.Access);
    }

    public IEnumerable<QuickAction> GetQuickActions() =>
        [new("notifications.panel", "Notificaciones", "\uE91C", 60, OpenPanel)];

    private void Show(IslandNotification n)
    {
        if (_host is null) return;
        if (settings.Current.Notifications.IsMuted(n.AppId))
        {
            logger.LogDebug("Notificación de {App} silenciada por el usuario", n.AppName);
            return;
        }
        _history.Add(n);
        _current = new NotificationViewModel(n, settings.Current.Notifications.ShowContent,
            open: () => { source.TryOpenApp(n.AppId); RemoveCurrent(); },
            mute: () => Mute(n),
            close: RemoveCurrent);
        _host.Publish(new IslandActivity(CurrentKey, ModuleId, NotificationPriority, _current));
        _host.RequestAttention(CurrentKey, Attention);
        _hide.Stop();
        _hide.Start();
    }

    /// <summary>Se va sola a los 8 s, salvo que esté abierta y el usuario la esté mirando.</summary>
    private void HideCurrentIfIdle()
    {
        if (_current is { IsBeingRead: true }) return; // vuelve a probar en 8 s
        RemoveCurrent();
    }

    private void RemoveCurrent()
    {
        _hide.Stop();
        _current = null;
        _host?.Remove(CurrentKey);
    }

    private void Mute(IslandNotification n)
    {
        settings.Update(s =>
        {
            if (!s.Notifications.IsMuted(n.AppId)) s.Notifications.MutedApps.Add(new IgnoredApp(n.AppId, n.AppName));
        });
        logger.LogInformation("App silenciada: {App}", n.AppName);
        RemoveCurrent();
    }

    private void OpenPanel()
    {
        if (_host is null || _panel is null) return;
        _panel.Refresh(_history.Items, source.Access, settings.Current.Notifications.ShowContent, DateTimeOffset.Now);
        _host.Publish(new IslandActivity(PanelKey, PanelModuleId, ActivityPriority.Panel, _panel));
    }

    private void OpenFromPanel(IslandNotification n)
    {
        source.TryOpenApp(n.AppId);
        _host?.Remove(PanelKey);
    }

    private void ClearHistory()
    {
        _history.Clear();
        OpenPanel();
    }

    private async Task RequestAccessAsync()
    {
        var access = await source.RequestAccessAsync();
        if (access == NotificationAccess.Allowed) await source.StartAsync();
        OpenPanel();
    }

    private static void OpenWindowsPrivacy() =>
        Process.Start(new ProcessStartInfo("ms-settings:privacy-notifications") { UseShellExecute = true });

    public void Dispose()
    {
        _hide.Stop();
        _history.Clear();
    }
}

/// <summary>La notificación que se está mostrando.</summary>
public sealed class NotificationViewModel : ObservableObject, IIslandAware
{
    public NotificationViewModel(IslandNotification n, bool showContent, Action open, Action mute, Action close)
    {
        AppName = n.AppName;
        // Sin contenido: solo se dice de qué app es (para compartir pantalla sin exponer mensajes).
        Title = showContent ? n.Title : $"Nueva notificación de {n.AppName}";
        Body = showContent ? n.Body : "";
        Time = n.At.LocalDateTime.ToString("HH:mm");
        MuteLabel = $"Silenciar {n.AppName}";
        OpenCommand = new RelayCommand(open);
        MuteCommand = new RelayCommand(mute);
        CloseCommand = new RelayCommand(close);
    }

    public string AppName { get; }
    public string Title { get; }
    public string Body { get; }
    public bool HasBody => Body.Length > 0;
    public string Time { get; }
    public string MuteLabel { get; }
    public ICommand OpenCommand { get; }
    public ICommand MuteCommand { get; }
    public ICommand CloseCommand { get; }

    /// <summary>True mientras está expandida a la vista (no se oculta sola).</summary>
    public bool IsBeingRead { get; private set; }

    public void OnIslandStateChanged(IslandState state, bool isShown) =>
        IsBeingRead = isShown && state == IslandState.Expanded;
}

public sealed class NotificationItemViewModel(IslandNotification n, bool showContent, ICommand open)
{
    public string AppName => n.AppName;
    public string Title => showContent ? n.Title : "Notificación";
    public string Time => n.At.LocalDateTime.ToString("HH:mm");
    public ICommand OpenCommand => open;
}

/// <summary>Panel "Notificaciones": las últimas recibidas, o qué falta para que funcionen.</summary>
public sealed class NotificationsPanelViewModel : ObservableObject
{
    private readonly Action<IslandNotification> _open;
    private string _status = "";
    private bool _ready, _canRequest, _canOpenSettings;

    public NotificationsPanelViewModel(Action<IslandNotification> open, Action clear, Func<Task> requestAccess, Action openPrivacy)
    {
        _open = open;
        ClearCommand = new RelayCommand(clear);
        RequestAccessCommand = new AsyncCommand<object>(_ => requestAccess());
        OpenPrivacyCommand = new RelayCommand(openPrivacy);
    }

    public ObservableCollection<NotificationItemViewModel> Items { get; } = [];
    public bool HasItems => Items.Count > 0;
    public bool ShowEmpty => _ready && Items.Count == 0;
    public bool IsReady { get => _ready; private set => SetProperty(ref _ready, value); }
    public bool IsNotReady => !_ready;
    public string StatusText { get => _status; private set => SetProperty(ref _status, value); }
    public bool CanRequestAccess { get => _canRequest; private set => SetProperty(ref _canRequest, value); }
    public bool CanOpenPrivacy { get => _canOpenSettings; private set => SetProperty(ref _canOpenSettings, value); }
    public ICommand ClearCommand { get; }
    public ICommand RequestAccessCommand { get; }
    public ICommand OpenPrivacyCommand { get; }

    public void Refresh(IReadOnlyList<IslandNotification> items, NotificationAccess access, bool showContent, DateTimeOffset now)
    {
        IsReady = access == NotificationAccess.Allowed;
        StatusText = access switch
        {
            NotificationAccess.NoPackageIdentity => "Para leer notificaciones, Windows exige registrar la identidad de la app (tools\\register-identity.ps1).",
            NotificationAccess.Unspecified => "Falta tu permiso para que la isla lea las notificaciones.",
            NotificationAccess.Denied => "El permiso está negado. Se activa en Configuración de Windows → Privacidad → Notificaciones.",
            NotificationAccess.Unavailable => "Windows no permite leer notificaciones en este momento.",
            _ => "",
        };
        CanRequestAccess = access == NotificationAccess.Unspecified;
        CanOpenPrivacy = access == NotificationAccess.Denied;

        Items.Clear();
        foreach (var n in items) Items.Add(new NotificationItemViewModel(n, showContent, new RelayCommand(() => _open(n))));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(ShowEmpty));
        OnPropertyChanged(nameof(IsNotReady));
    }
}
