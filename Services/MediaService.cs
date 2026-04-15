using System.Diagnostics;
using System.IO;
using System.Windows.Media.Imaging;
using Windows.Media;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace MediaOverlay.Services;

/// <summary>
/// Reads the currently playing media from any Windows app using the
/// System Media Transport Controls (SMTC) session manager.
/// </summary>
public class MediaService : IDisposable
{
    /// <summary>Raised on a thread-pool thread whenever the media state changes.</summary>
    public event Action<MediaInfo>? MediaChanged;

    /// <summary>Raised every ~1 s with the current playback position. Only fires while playing.</summary>
    public event Action<TimelineInfo>? TimelineChanged;

    /// <summary>When true, switching to a new media source automatically pauses all other sessions.</summary>
    public bool AutoPauseOthers { get; set; }

    private GlobalSystemMediaTransportControlsSessionManager? _manager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private bool _isPlaying;
    private System.Threading.Timer? _timelineTimer;

    // ── Init ─────────────────────────────────────────────────────────────────

    public async Task InitializeAsync()
    {
        _manager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
        _manager.CurrentSessionChanged += OnCurrentSessionChanged;
        UpdateSession(_manager.GetCurrentSession());

        // Poll timeline position every second (SMTC has no position-change event)
        _timelineTimer = new System.Threading.Timer(
            _ => PollTimelineAsync(), null,
            dueTime: 0, period: 1000);
    }

    // ── Session tracking ─────────────────────────────────────────────────────

    private void OnCurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
        => UpdateSession(sender.GetCurrentSession());

    private void UpdateSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged    -= OnPlaybackInfoChanged;
        }

        _currentSession = session;

        if (_currentSession is null)
        {
            _isPlaying = false;
            MediaChanged?.Invoke(MediaInfo.Empty);
            return;
        }

        _currentSession.MediaPropertiesChanged += OnMediaPropertiesChanged;
        _currentSession.PlaybackInfoChanged    += OnPlaybackInfoChanged;
        _ = FetchAndRaiseAsync();

        if (AutoPauseOthers)
            _ = PauseOtherSessionsAsync(_currentSession);
    }

    private async void OnMediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args) => await FetchAndRaiseAsync();

    private void OnPlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args) => _ = FetchAndRaiseAsync();

    // ── Fetch & raise ────────────────────────────────────────────────────────

    private async Task FetchAndRaiseAsync()
    {
        if (_currentSession is null) return;
        try
        {
            var props    = await _currentSession.TryGetMediaPropertiesAsync();
            var playback = _currentSession.GetPlaybackInfo();

            bool isPlaying      = playback?.PlaybackStatus ==
                GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            bool isShuffleActive = playback?.IsShuffleActive ?? false;
            var  repeatMode      = playback?.AutoRepeatMode  ?? MediaPlaybackAutoRepeatMode.None;

            _isPlaying = isPlaying;

            BitmapImage? albumArt = null;
            if (props?.Thumbnail is not null)
                albumArt = await ConvertThumbnailAsync(props.Thumbnail);

            var aumid = _currentSession?.SourceAppUserModelId;
            MediaChanged?.Invoke(new MediaInfo(
                Title:           props?.Title  ?? string.Empty,
                Artist:          props?.Artist ?? string.Empty,
                AlbumArt:        albumArt,
                IsPlaying:       isPlaying,
                SourceApp:       GetFriendlyAppName(aumid),
                ProcessName:     GetProcessName(aumid),
                IsShuffleActive: isShuffleActive,
                RepeatMode:      repeatMode));

            // Immediately push fresh timeline data so the ViewModel doesn't wait
            // up to 1 s for the next timer tick after a track / state change.
            if (isPlaying) _ = PollTimelineAsync();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaService] FetchAndRaiseAsync error: {ex.Message}");
        }
    }

    // ── Timeline polling ─────────────────────────────────────────────────────

    private Task PollTimelineAsync()
    {
        if (_currentSession is null || !_isPlaying) return Task.CompletedTask;
        try
        {
            var tl = _currentSession.GetTimelineProperties();
            if (tl is null) return Task.CompletedTask;

            var duration = tl.EndTime - tl.StartTime;
            bool canSeek = tl.MaxSeekTime > TimeSpan.Zero && duration > TimeSpan.Zero;

            TimelineChanged?.Invoke(new TimelineInfo(
                Position:  tl.Position,
                Duration:  duration,
                CanSeek:   canSeek,
                IsPlaying: _isPlaying));
        }
        catch { /* session may vanish mid-poll */ }
        return Task.CompletedTask;
    }

    // ── Pause other sessions ─────────────────────────────────────────────────

    private async Task PauseOtherSessionsAsync(
        GlobalSystemMediaTransportControlsSession keepSession)
    {
        if (_manager is null) return;
        foreach (var session in _manager.GetSessions())
        {
            if (session == keepSession) continue;
            try
            {
                var pb = session.GetPlaybackInfo();
                if (pb?.PlaybackStatus ==
                    GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                    await session.TryPauseAsync();
            }
            catch { }
        }
    }

    // ── Album art ────────────────────────────────────────────────────────────

    private static async Task<BitmapImage?> ConvertThumbnailAsync(
        IRandomAccessStreamReference thumbnailRef)
    {
        try
        {
            using var winrtStream = await thumbnailRef.OpenReadAsync();
            var memStream = new MemoryStream();
            using var netStream = winrtStream.AsStreamForRead();
            await netStream.CopyToAsync(memStream);
            memStream.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption  = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = memStream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[MediaService] Thumbnail error: {ex.Message}");
            return null;
        }
    }

    // ── App name helpers ──────────────────────────────────────────────────────

    private static string GetFriendlyAppName(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return string.Empty;
        string name = StripAumid(aumid);
        return name switch
        {
            "MicrosoftEdge" => "Edge",
            "msedge"        => "Edge",
            "chrome"        => "Chrome",
            "firefox"       => "Firefox",
            "Spotify"       => "Spotify",
            "AIMP"          => "AIMP",
            "vlc"           => "VLC",
            _               => name
        };
    }

    private static string GetProcessName(string? aumid)
    {
        if (string.IsNullOrWhiteSpace(aumid)) return string.Empty;
        return StripAumid(aumid).ToLowerInvariant() switch
        {
            "microsoftedge" => "msedge",
            var n           => n
        };
    }

    private static string StripAumid(string aumid)
    {
        int bang = aumid.IndexOf('!');
        string name = bang >= 0 ? aumid[(bang + 1)..] : aumid;

        int slash = Math.Max(name.LastIndexOf('\\'), name.LastIndexOf('/'));
        if (slash >= 0) name = name[(slash + 1)..];

        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            name = name[..^4];

        return name;
    }

    // ── Playback commands ────────────────────────────────────────────────────

    public void PlayPause() => _ = _currentSession?.TryTogglePlayPauseAsync();
    public void Next()      => _ = _currentSession?.TrySkipNextAsync();
    public void Previous()  => _ = _currentSession?.TrySkipPreviousAsync();

    public void ToggleShuffle()
    {
        var pb = _currentSession?.GetPlaybackInfo();
        _ = _currentSession?.TryChangeShuffleActiveAsync(!(pb?.IsShuffleActive ?? false));
    }

    public void CycleRepeat()
    {
        var pb   = _currentSession?.GetPlaybackInfo();
        var cur  = pb?.AutoRepeatMode ?? MediaPlaybackAutoRepeatMode.None;
        var next = cur switch
        {
            MediaPlaybackAutoRepeatMode.None  => MediaPlaybackAutoRepeatMode.List,
            MediaPlaybackAutoRepeatMode.List  => MediaPlaybackAutoRepeatMode.Track,
            MediaPlaybackAutoRepeatMode.Track => MediaPlaybackAutoRepeatMode.None,
            _                                 => MediaPlaybackAutoRepeatMode.None
        };
        _ = _currentSession?.TryChangeAutoRepeatModeAsync(next);
    }

    /// <summary>Seek to an absolute position within the current track.</summary>
    public void SeekTo(TimeSpan position)
        => _ = _currentSession?.TryChangePlaybackPositionAsync(position.Ticks);

    // ── Dispose ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _timelineTimer?.Dispose();

        if (_manager is not null)
            _manager.CurrentSessionChanged -= OnCurrentSessionChanged;

        if (_currentSession is not null)
        {
            _currentSession.MediaPropertiesChanged -= OnMediaPropertiesChanged;
            _currentSession.PlaybackInfoChanged    -= OnPlaybackInfoChanged;
        }
    }
}

// ── Data records ─────────────────────────────────────────────────────────────

public record MediaInfo(
    string                      Title,
    string                      Artist,
    BitmapImage?                AlbumArt,
    bool                        IsPlaying,
    string                      SourceApp,
    string                      ProcessName,
    bool                        IsShuffleActive,
    MediaPlaybackAutoRepeatMode RepeatMode)
{
    public static readonly MediaInfo Empty =
        new(string.Empty, string.Empty, null, false,
            string.Empty, string.Empty, false,
            MediaPlaybackAutoRepeatMode.None);
}

public record TimelineInfo(TimeSpan Position, TimeSpan Duration, bool CanSeek, bool IsPlaying);
