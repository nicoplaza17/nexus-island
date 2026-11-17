using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using NexusIsland.Core.Activities;
using NexusIsland.Core.Island;
using NexusIsland.Core.Media;
using NexusIsland.Core.Mvvm;

namespace NexusIsland.Modules.Media;

/// <summary>
/// Datos y comandos que muestran las plantillas multimedia. Vive en el hilo de la interfaz.
/// </summary>
public sealed class MediaViewModel : ObservableObject, IIslandAware
{
    // Glifos de Segoe Fluent Icons / Segoe MDL2 Assets.
    private const string GlyphPlay = "\uE768";
    private const string GlyphPause = "\uE769";

    private readonly IMediaSessionService _service;
    private readonly ILogger _logger;
    // 1 vez por segundo: el texto de tiempo cambia cada segundo y cada actualización redibuja la isla.
    private readonly DispatcherTimer _progressTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(1) };

    private MediaSnapshot? _snapshot;
    private byte[]? _artworkBytes;

    private string _title = "", _artist = "", _appName = "", _positionText = "0:00", _durationText = "0:00";
    private ImageSource? _artwork;
    private bool _isPlaying, _hasTimeline, _canPlayPause, _canNext, _canPrevious, _canSeek;
    private double _progress;

    public MediaViewModel(IMediaSessionService service, ILogger logger)
    {
        _service = service;
        _logger = logger;
        _progressTimer.Tick += (_, _) => UpdatePosition();

        PlayPauseCommand = new AsyncCommand<object>(_ => TogglePlayPauseAsync(), _ => CanPlayPause, OnCommandError);
        NextCommand = new AsyncCommand<object>(_ => _service.NextAsync(), _ => CanNext, OnCommandError);
        PreviousCommand = new AsyncCommand<object>(_ => _service.PreviousAsync(), _ => CanPrevious, OnCommandError);
        SeekCommand = new AsyncCommand<double>(SeekAsync, _ => CanSeek, OnCommandError);
    }

    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string Artist { get => _artist; private set => SetProperty(ref _artist, value); }
    public string AppName { get => _appName; private set => SetProperty(ref _appName, value); }

    public ImageSource? Artwork
    {
        get => _artwork;
        private set
        {
            if (SetProperty(ref _artwork, value))
            {
                OnPropertyChanged(nameof(HasArtwork));
                OnPropertyChanged(nameof(HasNoArtwork));
            }
        }
    }

    public bool HasArtwork => _artwork is not null;
    public bool HasNoArtwork => _artwork is null;

    public bool IsPlaying
    {
        get => _isPlaying;
        private set
        {
            if (SetProperty(ref _isPlaying, value))
            {
                OnPropertyChanged(nameof(IsNotPlaying));
                OnPropertyChanged(nameof(PlayPauseGlyph));
                OnPropertyChanged(nameof(PlayPauseLabel));
            }
        }
    }

    public bool IsNotPlaying => !_isPlaying;
    public string PlayPauseGlyph => _isPlaying ? GlyphPause : GlyphPlay;
    public string PlayPauseLabel => _isPlaying ? "Pausar" : "Reproducir";

    public bool HasTimeline { get => _hasTimeline; private set => SetProperty(ref _hasTimeline, value); }
    public double Progress { get => _progress; private set => SetProperty(ref _progress, value); }
    public string PositionText { get => _positionText; private set => SetProperty(ref _positionText, value); }
    public string DurationText { get => _durationText; private set => SetProperty(ref _durationText, value); }

    public bool CanPlayPause { get => _canPlayPause; private set { if (SetProperty(ref _canPlayPause, value)) PlayPauseCommand.RaiseCanExecuteChanged(); } }
    public bool CanNext { get => _canNext; private set { if (SetProperty(ref _canNext, value)) NextCommand.RaiseCanExecuteChanged(); } }
    public bool CanPrevious { get => _canPrevious; private set { if (SetProperty(ref _canPrevious, value)) PreviousCommand.RaiseCanExecuteChanged(); } }
    public bool CanSeek { get => _canSeek; private set { if (SetProperty(ref _canSeek, value)) SeekCommand.RaiseCanExecuteChanged(); } }

    public AsyncCommand<object> PlayPauseCommand { get; }
    public AsyncCommand<object> NextCommand { get; }
    public AsyncCommand<object> PreviousCommand { get; }

    /// <summary>Parámetro: fracción entre 0 y 1 de la línea de tiempo.</summary>
    public AsyncCommand<double> SeekCommand { get; }

    public string? TrackKey => _snapshot?.TrackKey;

    public void Update(MediaSnapshot snapshot)
    {
        _snapshot = snapshot;
        Title = snapshot.Title;
        Artist = snapshot.Artist;
        AppName = snapshot.SourceAppName;
        IsPlaying = snapshot.IsPlaying;
        CanPlayPause = snapshot.Capabilities.HasFlag(MediaCapabilities.PlayPause);
        CanNext = snapshot.Capabilities.HasFlag(MediaCapabilities.Next);
        CanPrevious = snapshot.Capabilities.HasFlag(MediaCapabilities.Previous);
        CanSeek = snapshot.Capabilities.HasFlag(MediaCapabilities.Seek) && snapshot.Timeline is not null;
        HasTimeline = snapshot.Timeline is not null;

        if (!ReferenceEquals(snapshot.Thumbnail, _artworkBytes))
        {
            _artworkBytes = snapshot.Thumbnail;
            Artwork = DecodeArtwork(snapshot.Thumbnail);
        }

        DurationText = snapshot.Timeline is { } t ? PlaybackPosition.Format(t.Duration) : "";
        UpdatePosition();
    }

    /// <summary>La barra de progreso solo se actualiza si la isla está expandida mostrando esta actividad.</summary>
    public void OnIslandStateChanged(IslandState state, bool isShown)
    {
        if (isShown && state == IslandState.Expanded)
        {
            UpdatePosition();
            _progressTimer.Start();
        }
        else
        {
            _progressTimer.Stop();
        }
    }

    public void Stop() => _progressTimer.Stop();

    private void UpdatePosition()
    {
        if (_snapshot?.Timeline is not { } timeline) return;
        var position = PlaybackPosition.Estimate(_snapshot, DateTimeOffset.Now);
        Progress = PlaybackPosition.Progress(timeline, position);
        PositionText = PlaybackPosition.Format(position - timeline.Start);
    }

    private async Task TogglePlayPauseAsync()
    {
        var wasPlaying = IsPlaying;
        if (await _service.TogglePlayPauseAsync())
            IsPlaying = !wasPlaying; // respuesta inmediata; el estado real llega enseguida y lo confirma
    }

    private async Task SeekAsync(double fraction)
    {
        if (_snapshot?.Timeline is not { } timeline) return;
        var target = timeline.Start + TimeSpan.FromTicks((long)(timeline.Duration.Ticks * Math.Clamp(fraction, 0, 1)));
        await _service.SeekAsync(target);
    }

    private void OnCommandError(Exception ex) => _logger.LogWarning(ex, "Error ejecutando un comando multimedia");

    private ImageSource? DecodeArtwork(byte[]? bytes)
    {
        if (bytes is null || bytes.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 160; // suficiente para 64 px a 250 % de escala, y ahorra memoria
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Carátula con formato no soportado");
            return null;
        }
    }
}
