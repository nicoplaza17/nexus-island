using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Media;
using NexusIsland.Modules.Media.Properties;
using NexusIsland.Core.Settings;

namespace NexusIsland.Modules.Media;

/// <summary>
/// Módulo Multimedia: escucha al servicio multimedia y publica una actividad en la isla
/// mientras hay una app reproduciendo (o pausada) que se pueda controlar.
/// </summary>
public sealed class MediaModule : IIslandModule
{
    public const string ModuleId = "media";
    private const string ActivityKey = "media.session";
    private static readonly TimeSpan TrackChangeAttention = TimeSpan.FromSeconds(3);

    private readonly IMediaSessionService _service;
    private readonly ILogger<MediaModule> _logger;
    private IActivityHost? _host;
    private SynchronizationContext? _uiContext;
    private MediaViewModel? _viewModel;
    private bool _published;
    private bool _initializing;
    private string? _lastApp;
    private MediaPlaybackStatus _lastStatus;
    private System.Windows.Threading.DispatcherTimer? _removeGrace;
    private static readonly TimeSpan RemoveGrace = TimeSpan.FromSeconds(1.5);

    private readonly SettingsStore _settings;

    private readonly IAudioLevelMeter _meter;

    public MediaModule(IMediaSessionService service, SettingsStore settings, IAudioLevelMeter meter, ILogger<MediaModule> logger)
    {
        _service = service;
        _settings = settings;
        _meter = meter;
        _logger = logger;
    }

    /// <summary>NEXUS_BARS=off apaga las barritas animadas sin tocar la configuración (para medir el costo).</summary>
    private bool BarsEnabled() =>
        _settings.Current.Media.AnimatedBars
        && !string.Equals(Environment.GetEnvironmentVariable("NEXUS_BARS"), "off", StringComparison.OrdinalIgnoreCase);

    public string Id => ModuleId;

    public string DisplayName => Strings.Module_Name;

    public Uri? TemplatesUri { get; } =
        new("pack://application:,,,/NexusIsland.Modules.Media;component/MediaTemplates.xaml", UriKind.Absolute);

    public async Task StartAsync(IActivityHost host, CancellationToken cancellationToken)
    {
        _host = host;
        _uiContext = SynchronizationContext.Current
            ?? throw new InvalidOperationException("MediaModule debe iniciarse en el hilo de la interfaz.");
        _viewModel = new MediaViewModel(_service, _logger, _meter, BarsEnabled);
        _settings.Changed += _ => _uiContext.Post(_ => _viewModel?.RefreshBarsSetting(), null);
        _removeGrace = new System.Windows.Threading.DispatcherTimer { Interval = RemoveGrace };
        _removeGrace.Tick += (_, _) =>
        {
            _removeGrace.Stop();
            if (!_published) return;
            _host.Remove(ActivityKey);
            _published = false;
            _viewModel.Stop();
            _logger.LogInformation("Sin sesión multimedia: la isla vuelve al reloj");
        };

        // Mientras se inicializa, los avisos que lleguen se aplican sin notificación temporal
        // (que es para "cambió la canción", no para "arrancó la app").
        _initializing = true;
        _service.Changed += OnServiceChanged;
        await _service.InitializeAsync(cancellationToken);
        _initializing = false;
        Apply(_service.Current, isInitial: true);
    }

    // El servicio avisa desde un hilo secundario: se pasa al hilo de la interfaz.
    private void OnServiceChanged(MediaSnapshot? snapshot) =>
        _uiContext?.Post(_ => Apply(snapshot, isInitial: _initializing), null);

    private void Apply(MediaSnapshot? snapshot, bool isInitial)
    {
        if (_host is null || _viewModel is null) return;

        if (snapshot is null)
        {
            // Las apps a veces cierran y reabren la sesión en un instante (cambio de pista, de pestaña):
            // se espera un momento antes de volver al reloj, para no parpadear.
            if (_published) _removeGrace?.Start();
            _lastApp = null;
            return;
        }

        _removeGrace?.Stop();
        var isNewTrack = _viewModel.TrackKey != snapshot.TrackKey;
        if (!isNewTrack && _lastStatus != snapshot.Status)
            _logger.LogDebug("Estado de reproducción: {Previous} → {Status}", _lastStatus, snapshot.Status);
        _lastStatus = snapshot.Status;
        _viewModel.Update(snapshot);

        if (snapshot.SourceAppId != _lastApp)
        {
            _lastApp = snapshot.SourceAppId;
            _logger.LogInformation("Sesión multimedia: {App} ({Status})", snapshot.SourceAppName, snapshot.Status);
        }
        if (isNewTrack)
            _logger.LogDebug("Pista: \"{Title}\" — {Artist} [{Status}, carátula: {Art}]", snapshot.Title, snapshot.Artist,
                snapshot.Status, snapshot.Thumbnail is null ? "no" : $"{snapshot.Thumbnail.Length} bytes");

        if (!_published)
        {
            _host.Publish(new IslandActivity(ActivityKey, ModuleId, ActivityPriority.Ambient, _viewModel));
            _published = true;
        }

        // Notificación temporal al cambiar de canción (no al arrancar la app ni con la música pausada).
        if (isNewTrack && !isInitial && snapshot.IsPlaying && _settings.Current.Media.NotifyOnTrackChange)
            _host.RequestAttention(ActivityKey, TrackChangeAttention);
    }

    public void Dispose()
    {
        _service.Changed -= OnServiceChanged;
        _removeGrace?.Stop();
        _viewModel?.Stop();
    }
}
