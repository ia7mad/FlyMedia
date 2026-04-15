using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;
using MediaOverlay.Services;
using MediaOverlay.ViewModels;

namespace MediaOverlay;

/// <summary>Widget corner positions available for the pinned mode.</summary>
public enum WidgetPosition { BottomRight, BottomLeft, TopRight, TopLeft }

public partial class MainWindow : Window
{
    private HotkeyService? _hotkeyService;
    private bool           _isWidgetMode;
    private WidgetPosition _widgetPosition = WidgetPosition.BottomRight;

    // Auto-hide: 0 = never, otherwise seconds until the overlay fades away.
    private int              _autoHideSecs = 5;
    private DispatcherTimer? _autoHideTimer;

    // ── Constructor ──────────────────────────────────────────────────────────

    public MainWindow()
    {
        InitializeComponent();
        PositionFloating();

        if (DataContext is MainViewModel vm)
            vm.Initialize();
    }

    // ── Scrolling title ───────────────────────────────────────────────────────

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (DataContext is MainViewModel vm)
            vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SongTitle))
            Dispatcher.BeginInvoke(UpdateMarquee, DispatcherPriority.Loaded);
    }

    private void UpdateMarquee()
    {
        // Stop any running animation and reset
        SongTitleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SongTitleTranslate.X = 0;

        // Force full layout pass so the StackPanel computes the true rendered width, accounting for Arabic/BiDi text
        SongTitleText.UpdateLayout();
        
        double textWidth      = SongTitleText.ActualWidth;
        double containerWidth = SongTitleContainer.ActualWidth;

        if (textWidth <= containerWidth + 0.5) return;  // fits — no scroll needed

        double scroll = textWidth - containerWidth + 10;
        double duration = scroll / 45.0;              // 45 px/s feels natural
        const double pause = 1.2;                     // seconds pause at each end

        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };

        double t = 0;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,       KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        t += pause;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,       KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        t += duration;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-scroll, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        t += pause;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-scroll, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        t += duration;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0,       KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));

        SongTitleTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    // ── Init ─────────────────────────────────────────────────────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        WindowEffectsService.ApplyAcrylic(hwnd);

        var source = HwndSource.FromHwnd(hwnd);
        _hotkeyService = new HotkeyService(source);
        _hotkeyService.HotkeyPressed += OnHotkeyPressed;
    }

    // ── Hotkey ───────────────────────────────────────────────────────────────

    private void OnHotkeyPressed()
    {
        if (_isWidgetMode) return;
        ToggleVisibility();
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            _autoHideTimer?.Stop();
            FadeOut(() => Hide());
        }
        else
        {
            Opacity = 0;
            Show();
            Activate();
            FadeIn();
            StartAutoHide();
            (DataContext as MainViewModel)?.RefreshDisplay();
        }
    }

    // ── Fade helpers ─────────────────────────────────────────────────────────

    private void FadeIn()
    {
        var anim = new DoubleAnimation(0, 1, new Duration(TimeSpan.FromMilliseconds(180)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(OpacityProperty, anim);
    }

    private void FadeOut(Action? onComplete = null)
    {
        var anim = new DoubleAnimation(1, 0, new Duration(TimeSpan.FromMilliseconds(130)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        if (onComplete is not null)
            anim.Completed += (_, _) => onComplete();
        BeginAnimation(OpacityProperty, anim);
    }

    // ── Auto-hide ────────────────────────────────────────────────────────────

    /// <summary>Sets the duration in seconds before the overlay hides itself (0 = never).</summary>
    public void SetAutoHideDuration(int seconds)
    {
        _autoHideSecs = seconds;
        _autoHideTimer?.Stop();
    }

    private void StartAutoHide()
    {
        if (_autoHideSecs <= 0 || _isWidgetMode) return;

        if (_autoHideTimer is null)
        {
            _autoHideTimer = new DispatcherTimer();
            _autoHideTimer.Tick += (_, _) =>
            {
                _autoHideTimer.Stop();
                FadeOut(() => Hide());
            };
        }

        _autoHideTimer.Interval = TimeSpan.FromSeconds(_autoHideSecs);
        _autoHideTimer.Stop();
        _autoHideTimer.Start();
    }

    private void PillBorder_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => _autoHideTimer?.Stop();

    private void PillBorder_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => StartAutoHide();

    // ── Widget mode ──────────────────────────────────────────────────────────

    public void ToggleWidgetMode()
    {
        _isWidgetMode = !_isWidgetMode;

        if (_isWidgetMode)
        {
            _autoHideTimer?.Stop();
            SnapToPosition();
            PillBorder.Cursor = System.Windows.Input.Cursors.Arrow;
            Show();
            Activate();
        }
        else
        {
            PositionFloating();
            PillBorder.Cursor = System.Windows.Input.Cursors.SizeAll;
        }
    }

    public bool IsWidgetMode => _isWidgetMode;

    // ── Widget position ───────────────────────────────────────────────────────

    /// <summary>Changes the corner the widget snaps to. Takes effect immediately if pinned.</summary>
    public void SetWidgetPosition(WidgetPosition position)
    {
        _widgetPosition = position;
        if (_isWidgetMode) SnapToPosition();
    }

    public WidgetPosition CurrentWidgetPosition => _widgetPosition;

    private void SnapToPosition()
    {
        var wa = SystemParameters.WorkArea;
        (Left, Top) = _widgetPosition switch
        {
            WidgetPosition.BottomRight => (wa.Right  - Width  - 12, wa.Bottom - Height - 12),
            WidgetPosition.BottomLeft  => (wa.Left   + 12,          wa.Bottom - Height - 12),
            WidgetPosition.TopRight    => (wa.Right  - Width  - 12, wa.Top    + 12),
            WidgetPosition.TopLeft     => (wa.Left   + 12,          wa.Top    + 12),
            _                          => (wa.Right  - Width  - 12, wa.Bottom - Height - 12),
        };
    }

    // ── Layout helpers ────────────────────────────────────────────────────────

    private void PositionFloating()
    {
        var wa = SystemParameters.WorkArea;
        Left = wa.Right - Width  - 20;
        Top  = wa.Top   + 20;
    }

    // ── Seek bar events ───────────────────────────────────────────────────────

    private void SeekSlider_DragStarted(object sender, DragStartedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.BeginSeek();
    }

    private void SeekSlider_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        if (DataContext is MainViewModel vm) vm.CommitSeek();
    }

    // ── Window behaviour ──────────────────────────────────────────────────────

    protected override void OnClosing(CancelEventArgs e)
    {
        e.Cancel = true;
        if (!_isWidgetMode) Hide();
    }

    private void Border_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_isWidgetMode && e.ButtonState == MouseButtonState.Pressed)
            DragMove();
    }

    protected override void OnClosed(EventArgs e)
    {
        _hotkeyService?.Dispose();
        _autoHideTimer?.Stop();
        base.OnClosed(e);
    }

    // ── Settings ──────────────────────────────────────────────────────────────

    private void SettingsButton_Click(object sender, RoutedEventArgs e)
    {
        var app = System.Windows.Application.Current as App;
        var settingsWin = new SettingsWindow(this, app?.TaskbarWidgetInstance);
        settingsWin.ShowDialog();
    }
}
