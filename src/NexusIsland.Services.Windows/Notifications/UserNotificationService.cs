using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Notifications;
using Windows.UI.Notifications;
using Windows.UI.Notifications.Management;

namespace NexusIsland.Services.Windows.Notifications;

/// <summary>
/// Lee las notificaciones de otras apps con UserNotificationListener (API oficial de Windows).
///
/// Requisitos y limitaciones reales:
///  - La app necesita IDENTIDAD DE PAQUETE (ver packaging\identity) y la capacidad userNotificationListener.
///  - El usuario tiene que dar permiso (Windows muestra su diálogo; se cambia en Privacidad → Notificaciones).
///  - Se puede LEER (app, título, texto, hora), pero NO tocar los botones de la notificación ni evitar
///    que Windows muestre su propio cartel.
///  - Solo notificaciones tipo "toast" (las de la esquina de la pantalla).
/// El contenido nunca se escribe en el log.
/// </summary>
public sealed class UserNotificationService : INotificationSource
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ILogger<UserNotificationService> _logger;
    private readonly NotificationTracker _tracker = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private UserNotificationListener? _listener;
    private Timer? _poll;
    private Timer? _debounce;
    private bool _started;

    public UserNotificationService(ILogger<UserNotificationService> logger) => _logger = logger;

    public event Action<IslandNotification>? Arrived;

    public NotificationAccess Access
    {
        get
        {
            if (!PackageIdentity.HasIdentity) return NotificationAccess.NoPackageIdentity;
            try { return Map(UserNotificationListener.Current.GetAccessStatus()); }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "UserNotificationListener no disponible");
                return NotificationAccess.Unavailable;
            }
        }
    }

    public async Task<NotificationAccess> RequestAccessAsync()
    {
        if (!PackageIdentity.HasIdentity) return NotificationAccess.NoPackageIdentity;
        try
        {
            var status = Map(await UserNotificationListener.Current.RequestAccessAsync());
            _logger.LogInformation("Acceso a notificaciones: {Status}", status);
            return status;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "No se pudo pedir acceso a las notificaciones");
            return NotificationAccess.Unavailable;
        }
    }

    public async Task StartAsync()
    {
        if (_started) return;
        var access = Access;
        if (access != NotificationAccess.Allowed)
        {
            _logger.LogInformation("Notificaciones: no se escuchan ({Access})", access);
            return;
        }

        _listener = UserNotificationListener.Current;
        var existing = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
        _tracker.Seed(existing.Select(n => n.Id)); // las que ya estaban no se muestran como nuevas
        _debounce = new Timer(_ => _ = CheckAsync(), null, Timeout.Infinite, Timeout.Infinite);

        try
        {
            _listener.NotificationChanged += OnNotificationChanged;
            _logger.LogInformation("Escuchando notificaciones de Windows (por eventos; {Count} ya en el centro)", existing.Count);
        }
        catch (Exception ex)
        {
            // En algunas configuraciones el evento no está disponible para apps de escritorio: se consulta cada 2 s.
            _logger.LogInformation(ex, "Evento de notificaciones no disponible: se consulta cada {Seconds} s", PollInterval.TotalSeconds);
            _poll = new Timer(_ => _ = CheckAsync(), null, PollInterval, PollInterval);
        }
        _started = true;
    }

    private void OnNotificationChanged(UserNotificationListener sender, UserNotificationChangedEventArgs args)
    {
        if (args.ChangeKind == UserNotificationChangedKind.Added)
            _debounce?.Change(TimeSpan.FromMilliseconds(150), Timeout.InfiniteTimeSpan);
    }

    private async Task CheckAsync()
    {
        if (_listener is null) return;
        await _gate.WaitAsync();
        try
        {
            var current = await _listener.GetNotificationsAsync(NotificationKinds.Toast);
            var fresh = _tracker.Update(current.Select(n => n.Id).ToList());
            foreach (var n in current.Where(n => fresh.Contains(n.Id)).OrderBy(n => n.CreationTime))
            {
                var mapped = MapNotification(n);
                if (mapped is null) continue;
                _logger.LogDebug("Notificación de {App}", mapped.AppName); // nunca el contenido
                Arrived?.Invoke(mapped);
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudieron leer las notificaciones");
        }
        finally
        {
            _gate.Release();
        }
    }

    private IslandNotification? MapNotification(UserNotification n)
    {
        try
        {
            var appName = n.AppInfo?.DisplayInfo?.DisplayName ?? "Notificación";
            var appId = n.AppInfo?.AppUserModelId ?? n.AppInfo?.Id ?? "";
            var binding = n.Notification?.Visual?.GetBinding(KnownNotificationBindings.ToastGeneric);
            var texts = binding?.GetTextElements().Select(t => t.Text?.Trim()).Where(t => !string.IsNullOrEmpty(t)).ToList() ?? [];
            var title = texts.Count > 0 ? texts[0]! : appName;
            var body = string.Join(" · ", texts.Skip(1));
            return new IslandNotification(n.Id, appId, appName, title, body, n.CreationTime);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Notificación con formato no soportado");
            return null;
        }
    }

    public bool TryOpenApp(string appId)
    {
        if (string.IsNullOrWhiteSpace(appId)) return false;
        try
        {
            // "shell:AppsFolder\<AUMID>" abre la app (sirve para apps de la Store y la mayoría de las de escritorio).
            Process.Start(new ProcessStartInfo("explorer.exe", $"shell:AppsFolder\\{appId}") { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "No se pudo abrir la app de la notificación");
            return false;
        }
    }

    private static NotificationAccess Map(UserNotificationListenerAccessStatus s) => s switch
    {
        UserNotificationListenerAccessStatus.Allowed => NotificationAccess.Allowed,
        UserNotificationListenerAccessStatus.Denied => NotificationAccess.Denied,
        _ => NotificationAccess.Unspecified,
    };

    public void Dispose()
    {
        if (_listener is not null && _started && _poll is null)
        {
            try { _listener.NotificationChanged -= OnNotificationChanged; } catch { /* ya no disponible */ }
        }
        _poll?.Dispose();
        _debounce?.Dispose();
    }
}
