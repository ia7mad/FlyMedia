using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MediaOverlay.Services;
using Windows.Media;

namespace MediaOverlay.ViewModels;

public class MainViewModel : INotifyPropertyChanged
{
    private readonly MediaService  _mediaService  = new();
    private readonly VolumeService _volumeService = new();

    // ── Media info ───────────────────────────────────────────────────────────

    private string _songTitle = "No media playing";
    public string SongTitle
    {
        get => _songTitle;
        set { _songTitle = value; OnPropertyChanged(); }
    }

    private string _artist = string.Empty;
    public string Artist
    {
        get => _artist;
        set { _artist = value; OnPropertyChanged(); }
    }

    private BitmapImage? _albumArt;
    public BitmapImage? AlbumArt
    {
        get => _albumArt;
        set { _albumArt = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasAlbumArt)); }
    }

    public bool HasAlbumArt => _albumArt is not null;

    private bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set { _isPlaying = value; OnPropertyChanged(); }
    }

    private string _sourceApp = string.Empty;
    public string SourceApp
    {
        get => _sourceApp;
        set { _sourceApp = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasSourceApp)); }
    }

    public bool HasSourceApp => !string.IsNullOrWhiteSpace(_sourceApp);

    // ── Shuffle / Repeat ─────────────────────────────────────────────────────

    private bool _isShuffleActive;
    public bool IsShuffleActive
    {
        get => _isShuffleActive;
        set
        {
            _isShuffleActive = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShuffleForeground));
        }
    }

    private MediaPlaybackAutoRepeatMode _repeatMode = MediaPlaybackAutoRepeatMode.None;
    public MediaPlaybackAutoRepeatMode RepeatMode
    {
        get => _repeatMode;
        set
        {
            _repeatMode = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RepeatIcon));
            OnPropertyChanged(nameof(IsRepeatActive));
            OnPropertyChanged(nameof(RepeatForeground));
        }
    }

    public bool   IsRepeatActive => _repeatMode != MediaPlaybackAutoRepeatMode.None;
    public string RepeatIcon     => _repeatMode == MediaPlaybackAutoRepeatMode.Track
                                        ? "\uE8ED"   // RepeatOne
                                        : "\uE8EE";  // RepeatAll

    public System.Windows.Media.Brush ShuffleForeground => _isShuffleActive
        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA))
        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0x5B, 0x70));

    public System.Windows.Media.Brush RepeatForeground => IsRepeatActive
        ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x89, 0xB4, 0xFA))
        : new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x58, 0x5B, 0x70));

    private bool     _canSeek;
    private TimeSpan _duration = TimeSpan.Zero;
    private double   _seekPosition;
    private bool     _userSeeking;
    private double   _pendingSeekPos;
    private bool     _isInterpolating;

    // ── Position tracking via Stopwatch ─────────────────────────────────────
    // A Stopwatch is monotonic and keeps running regardless of what SMTC reports.
    // _positionAtLastSync is the media time when the stopwatch was last started/reset.
    // CurrentPosition = _positionAtLastSync + stopwatch.Elapsed (while playing).
    private readonly System.Diagnostics.Stopwatch _playStopwatch = new();
    private          TimeSpan                      _positionAtLastSync = TimeSpan.Zero;

    private TimeSpan CurrentPosition =>
        _positionAtLastSync + (_playStopwatch.IsRunning ? _playStopwatch.Elapsed : TimeSpan.Zero);

    // Seek protection — ignore stale SMTC position for 1.2 s after a seek command
    private DateTime _seekedAt = DateTime.MinValue;

    // Track-change detection
    private string _lastKnownTitle = string.Empty;

    private readonly DispatcherTimer _interpTimer;

    public bool CanSeek
    {
        get => _canSeek;
        set { _canSeek = value; OnPropertyChanged(); }
    }

    /// <summary>Seek position as a 0–1 fraction of the total duration.</summary>
    public double SeekPosition
    {
        get => _seekPosition;
        set
        {
            _seekPosition = value;
            OnPropertyChanged();

            // Only process user-initiated value changes (not interpolation / timeline sync)
            if (_isInterpolating) return;

            if (_userSeeking)
            {
                _pendingSeekPos = value;
            }
            else
            {
                // User clicked on the track bar (not dragging) — seek immediately
                if (_duration.TotalSeconds > 0)
                {
                    var target = TimeSpan.FromSeconds(value * _duration.TotalSeconds);
                    _seekedAt           = DateTime.Now;
                    _positionAtLastSync = target;
                    _playStopwatch.Restart();
                    _mediaService.SeekTo(target);
                }
            }
        }
    }

    private string _positionText = "0:00";
    public string PositionText
    {
        get => _positionText;
        set { _positionText = value; OnPropertyChanged(); }
    }

    private string _durationText = "0:00";
    public string DurationText
    {
        get => _durationText;
        set { _durationText = value; OnPropertyChanged(); }
    }

    private string _remainingText = "-0:00";
    public string RemainingText
    {
        get => _remainingText;
        set { _remainingText = value; OnPropertyChanged(); }
    }

    /// <summary>Forces an immediate UI refresh from the Stopwatch — call after re-showing the window.</summary>
    public void RefreshDisplay()
    {
        if (_duration.TotalSeconds <= 0) return;
        TimeSpan pos = CurrentPosition;
        if (pos > _duration) pos = _duration;
        _isInterpolating = true;
        _seekPosition    = pos.TotalSeconds / _duration.TotalSeconds;
        OnPropertyChanged(nameof(SeekPosition));
        _isInterpolating = false;
        PositionText  = FormatTime(pos);
        RemainingText = "-" + FormatTime(_duration - pos);
    }

    /// <summary>Called by the view when the user begins dragging the seek thumb.</summary>
    public void BeginSeek() => _userSeeking = true;

    /// <summary>Called by the view when the user releases the seek thumb — performs the seek.</summary>
    public void CommitSeek()
    {
        _userSeeking = false;
        if (_duration.TotalSeconds > 0)
        {
            var target = TimeSpan.FromSeconds(_pendingSeekPos * _duration.TotalSeconds);
            _seekedAt           = DateTime.Now;
            _positionAtLastSync = target;
            _playStopwatch.Restart();
            _mediaService.SeekTo(target);
        }
    }

    // ── Volume ───────────────────────────────────────────────────────────────

    private double _volume = 50;
    public double Volume
    {
        get => _volume;
        set
        {
            if (Math.Abs(_volume - value) < 0.5) return;
            _volume = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeIcon));
            _volumeService.Volume = (float)value;
        }
    }

    private bool _isMuted;
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            _isMuted = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(VolumeIcon));
            _volumeService.IsMuted = value;
        }
    }

    public string VolumeIcon => _isMuted || _volume < 1
        ? "\uE74F"
        : _volume < 34 ? "\uE993"
        : _volume < 67 ? "\uE994"
        : "\uE995";

    // ── Compact mode ─────────────────────────────────────────────────────────

    private bool _isCompact;
    public bool IsCompact
    {
        get => _isCompact;
        set { _isCompact = value; OnPropertyChanged(); }
    }

    // ── Auto-pause others ────────────────────────────────────────────────────

    public bool AutoPauseOthers
    {
        get => _mediaService.AutoPauseOthers;
        set { _mediaService.AutoPauseOthers = value; OnPropertyChanged(); }
    }

    // ── Commands ─────────────────────────────────────────────────────────────

    public ICommand PlayPauseCommand  { get; }
    public ICommand NextCommand       { get; }
    public ICommand PreviousCommand   { get; }
    public ICommand MuteToggleCommand { get; }
    public ICommand ShuffleCommand    { get; }
    public ICommand RepeatCommand     { get; }

    // ── Constructor ──────────────────────────────────────────────────────────

    public MainViewModel()
    {
        PlayPauseCommand  = new RelayCommand(() => _mediaService.PlayPause());
        NextCommand       = new RelayCommand(() => _mediaService.Next());
        PreviousCommand   = new RelayCommand(() => _mediaService.Previous());
        MuteToggleCommand = new RelayCommand(() => IsMuted = !IsMuted);
        ShuffleCommand    = new RelayCommand(() => _mediaService.ToggleShuffle());
        RepeatCommand     = new RelayCommand(() => _mediaService.CycleRepeat());

        _volume  = _volumeService.Volume;
        _isMuted = _volumeService.IsMuted;

        _interpTimer = new DispatcherTimer(DispatcherPriority.Normal)
                       { Interval = TimeSpan.FromMilliseconds(16) };
        _interpTimer.Tick += OnInterpolatedTick;
        _interpTimer.Start();
    }

    public async void Initialize()
    {
        _mediaService.MediaChanged    += OnMediaChanged;
        _mediaService.TimelineChanged += OnTimelineChanged;
        _volumeService.VolumeChanged  += OnVolumeChanged;
        await _mediaService.InitializeAsync();
    }

    // ── Event handlers ───────────────────────────────────────────────────────

    private void OnMediaChanged(MediaInfo info)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            bool trackChanged = info.Title != _lastKnownTitle;
            _lastKnownTitle = info.Title;

            SongTitle       = string.IsNullOrWhiteSpace(info.Title) ? "No media playing" : info.Title;
            Artist          = info.Artist;
            AlbumArt        = info.AlbumArt;
            IsPlaying       = info.IsPlaying;
            SourceApp       = info.SourceApp;
            IsShuffleActive = info.IsShuffleActive;
            RepeatMode      = info.RepeatMode;

            // Retarget volume slider to the new app's audio session
            var (vol, muted) = _volumeService.SetTargetApp(info.ProcessName);
            _volume  = vol;
            _isMuted = muted;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(VolumeIcon));

            // ── Stopwatch / seekbar management ───────────────────────────────
            if (trackChanged)
            {
                // New track — reset everything and start the clock immediately
                _seekedAt           = DateTime.MinValue; // clear seek protection
                _positionAtLastSync = TimeSpan.Zero;
                _duration           = TimeSpan.Zero;     // will be filled by OnTimelineChanged
                CanSeek             = false;             // will be updated by OnTimelineChanged
                DurationText        = "0:00";

                if (info.IsPlaying)
                    _playStopwatch.Restart();
                else
                    _playStopwatch.Reset();

                _isInterpolating = true;
                _seekPosition    = 0;
                OnPropertyChanged(nameof(SeekPosition));
                _isInterpolating = false;
                PositionText  = "0:00";
                RemainingText = "-0:00";
            }
            else if (info.IsPlaying && !_playStopwatch.IsRunning)
            {
                // Same track resumed — continue from where we paused
                _playStopwatch.Start();
            }
            else if (!info.IsPlaying && _playStopwatch.IsRunning)
            {
                // Same track paused — freeze the clock
                _positionAtLastSync = CurrentPosition;
                _playStopwatch.Reset();
            }

            if (!info.IsPlaying && !trackChanged)
            {
                // Update UI to the paused position
                var pausedPos = _positionAtLastSync;
                _isInterpolating = true;
                _seekPosition    = _duration.TotalSeconds > 0
                                   ? pausedPos.TotalSeconds / _duration.TotalSeconds : 0;
                OnPropertyChanged(nameof(SeekPosition));
                _isInterpolating = false;
                PositionText  = FormatTime(pausedPos);
                RemainingText = _duration > TimeSpan.Zero
                                ? "-" + FormatTime(_duration - pausedPos) : "-0:00";
            }
        });
    }

    private void OnTimelineChanged(TimelineInfo info)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            // Only trust non-zero duration data
            if (info.Duration > TimeSpan.Zero)
            {
                _duration    = info.Duration;
                DurationText = FormatTime(info.Duration);
                CanSeek      = info.CanSeek;

                // If the stopwatch was held back waiting for duration, start it now
                if (info.IsPlaying && !_playStopwatch.IsRunning && _positionAtLastSync == TimeSpan.Zero)
                    _playStopwatch.Restart();
            }

            bool seekProtected = (DateTime.Now - _seekedAt).TotalMilliseconds < 1200;
            if (seekProtected || _userSeeking || _duration <= TimeSpan.Zero) return;

            // Accept the SMTC position only if it's within ±3 s of the stopwatch estimate.
            // Positions outside this window are stale/zero data that SMTC sends between songs.
            TimeSpan diff = info.Position - CurrentPosition;
            if (diff < TimeSpan.FromSeconds(-3) || diff > TimeSpan.FromSeconds(3)) return;

            // Re-anchor the stopwatch to the confirmed SMTC position
            _positionAtLastSync = info.Position;
            _playStopwatch.Restart();

            _isInterpolating = true;
            _seekPosition    = info.Position.TotalSeconds / _duration.TotalSeconds;
            OnPropertyChanged(nameof(SeekPosition));
            _isInterpolating = false;

            PositionText  = FormatTime(info.Position);
            RemainingText = "-" + FormatTime(_duration - info.Position);
        });
    }

    private void OnInterpolatedTick(object? sender, EventArgs e)
    {
        if (!_isPlaying || _userSeeking || _duration.TotalSeconds <= 0) return;

        TimeSpan pos = CurrentPosition;
        if (pos > _duration) pos = _duration;

        _isInterpolating = true;
        _seekPosition    = pos.TotalSeconds / _duration.TotalSeconds;
        OnPropertyChanged(nameof(SeekPosition));
        _isInterpolating = false;

        PositionText  = FormatTime(pos);
        RemainingText = "-" + FormatTime(_duration - pos);
    }

    private void OnVolumeChanged(float masterVolume, bool muted)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _volume  = masterVolume * 100.0;
            _isMuted = muted;
            OnPropertyChanged(nameof(Volume));
            OnPropertyChanged(nameof(IsMuted));
            OnPropertyChanged(nameof(VolumeIcon));
        });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string FormatTime(TimeSpan t)
        => t.TotalHours >= 1
            ? t.ToString(@"h\:mm\:ss")
            : t.ToString(@"m\:ss");

    // ── INotifyPropertyChanged ────────────────────────────────────────────────

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

// ── RelayCommand ─────────────────────────────────────────────────────────────

public class RelayCommand : ICommand
{
    private readonly Action      _execute;
    private readonly Func<bool>? _canExecute;

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
    {
        _execute    = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke() ?? true;
    public void Execute(object? parameter)    => _execute();

#pragma warning disable CS0067
    public event EventHandler? CanExecuteChanged;
#pragma warning restore CS0067
}
