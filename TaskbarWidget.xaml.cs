using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Threading;
using MediaOverlay.Services;
using MediaOverlay.ViewModels;
using Microsoft.Win32;

namespace MediaOverlay;

public partial class TaskbarWidget : Window
{
    // ── Win32 ────────────────────────────────────────────────────────────────
    [DllImport("user32.dll")] static extern int    GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] static extern int    SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
    [DllImport("user32.dll")] static extern bool   SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
                                                                int X, int Y, int cx, int cy, uint uFlags);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool   GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern int GetClassName(IntPtr hWnd, System.Text.StringBuilder lpClassName, int nMaxCount);
    [DllImport("user32.dll",  CharSet = CharSet.Auto)] static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);
    [DllImport("shell32.dll")]                        static extern uint   SHAppBarMessage(uint dwMessage, ref APPBARDATA pData);

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA
    {
        public uint   cbSize;
        public IntPtr hWnd;
        public uint   uCallbackMessage;
        public uint   uEdge;
        public RECT   rc;
        public int    lParam;
    }

    const uint ABM_GETSTATE   = 4;
    const int  ABS_AUTOHIDE   = 1;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINDOWPOS
    {
        public IntPtr hwnd, hwndInsertAfter;
        public int    x, y, cx, cy;
        public uint   flags;
    }

    const int  GWL_EXSTYLE          = -20;
    const int  WS_EX_NOACTIVATE     = 0x08000000;
    const int  WS_EX_TOOLWINDOW     = 0x00000080;
    const int  WM_WINDOWPOSCHANGING = 0x0046;
    const int  WM_WINDOWPOSCHANGED  = 0x0047;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOMOVE    = 0x0002;
    const uint SWP_NOSIZE    = 0x0001;
    const uint SWP_NOACTIVATE = 0x0010;
    const uint SWP_NOZORDER  = 0x0004;

    // ── Fields ───────────────────────────────────────────────────────────────
    private readonly MainWindow      _mainWindow;
    private          DispatcherTimer _heartbeat = null!;
    private          bool            _shouldBeVisible;
    private          bool            _inWndProc;

    // Fullscreen / peek state
    private bool   _isFullscreen;
    private bool   _isPeeking;
    private double _normalTop;          // WPF logical px — where widget rests normally

    // ── Constructor ──────────────────────────────────────────────────────────
    public TaskbarWidget(MainWindow mainWindow, MainViewModel viewModel)
    {
        InitializeComponent();
        _mainWindow = mainWindow;
        DataContext  = viewModel;
    }

    // ── Window lifecycle ─────────────────────────────────────────────────────

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);

        var hwnd   = new WindowInteropHelper(this).Handle;
        var source = HwndSource.FromHwnd(hwnd);
        source.AddHook(WndProc);

        int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        SnapToPosition();

        SystemEvents.UserPreferenceChanged  += OnSystemChange;
        SystemEvents.DisplaySettingsChanged += OnSystemChange;

        _heartbeat = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _heartbeat.Tick += OnHeartbeat;
        _heartbeat.Start();

        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged += OnViewModelPropertyChanged;
        }
        Dispatcher.BeginInvoke(UpdateMarquee, DispatcherPriority.Loaded);
    }

    protected override void OnClosed(EventArgs e)
    {
        _heartbeat?.Stop();
        SystemEvents.UserPreferenceChanged  -= OnSystemChange;
        SystemEvents.DisplaySettingsChanged -= OnSystemChange;
        if (DataContext is MainViewModel vm)
        {
            vm.PropertyChanged -= OnViewModelPropertyChanged;
        }
        base.OnClosed(e);
    }

    // ── Scrolling title ───────────────────────────────────────────────────────

    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.SongTitle))
            Dispatcher.BeginInvoke(UpdateMarquee, DispatcherPriority.Loaded);
    }

    private void UpdateMarquee()
    {
        SongTitleTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        SongTitleTranslate.X = 0;

        SongTitleText.UpdateLayout();

        double textWidth = SongTitleText.ActualWidth;
        double containerWidth = SongTitleContainer.ActualWidth;

        if (textWidth <= containerWidth + 0.5) return;

        double scroll = textWidth - containerWidth + 10;
        double duration = scroll / 30.0; // 30 px/s
        const double pause = 1.2;

        var anim = new DoubleAnimationUsingKeyFrames { RepeatBehavior = RepeatBehavior.Forever };
        double t = 0;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        t += pause;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        t += duration;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-scroll, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        t += pause;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(-scroll, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));
        t += duration;
        anim.KeyFrames.Add(new LinearDoubleKeyFrame(0, KeyTime.FromTimeSpan(TimeSpan.FromSeconds(t))));

        SongTitleTranslate.BeginAnimation(TranslateTransform.XProperty, anim);
    }

    // ── Public API ───────────────────────────────────────────────────────────

    public new void Show()
    {
        _shouldBeVisible = true;
        base.Show();
        Dispatcher.BeginInvoke(SnapToPosition, DispatcherPriority.Loaded);
    }

    public new void Hide()
    {
        _shouldBeVisible = false;
        base.Hide();
    }

    public void ApplySettings() => SnapToPosition();

    public void Reassert()
    {
        if (!_shouldBeVisible) return;
        if (!IsVisible) base.Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        SnapToPosition();
    }

    // ── Heartbeat ────────────────────────────────────────────────────────────

    private void OnHeartbeat(object? sender, EventArgs e)
    {
        if (!_shouldBeVisible) return;

        // Z-order every tick (16 ms)
        if (!IsVisible) base.Show();
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero)
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);

        // Fullscreen + peek check every tick for instant response
        CheckFullscreenAndPeek();
    }

    // ── Fullscreen detection ─────────────────────────────────────────────────

    private void CheckFullscreenAndPeek()
    {
        bool fs = IsFullscreenActive();

        if (fs != _isFullscreen)
        {
            _isFullscreen = fs;

            if (!fs)
            {
                // Fullscreen exited → restore widget to normal position
                _isPeeking = false;
                SlideIn();
                return;
            }
            else
            {
                // Fullscreen entered → hide unless cursor is already touching the edge
                if (!CursorNearBottom())
                {
                    HideForFullscreen();
                    return;
                }
                // Cursor is already at the screen edge — skip straight to peek
                _isPeeking = true;
                SlideIn();
                return;
            }
        }

        if (!_isFullscreen) return;

        // Already in fullscreen — two-zone logic:
        // • Enter peek: cursor within 2px of screen bottom edge
        // • Exit peek:  cursor clearly above the widget (>30px above widget top)
        if (!_isPeeking && CursorNearBottom())
        {
            _isPeeking = true;
            SlideIn();
        }
        else if (_isPeeking && CursorClearlyAboveWidget())
        {
            _isPeeking = false;
            HideForFullscreen();
        }
    }

    /// <summary>Slides the widget off-screen and also hides the media control flyout.</summary>
    private void HideForFullscreen()
    {
        if (_mainWindow.IsVisible)
            _mainWindow.ToggleVisibility();   // hides with fade-out
        SlideOut();
    }

    private bool IsFullscreenActive()
    {
        var fgHwnd = GetForegroundWindow();
        if (fgHwnd == IntPtr.Zero) return false;

        // Ignore our own windows
        var ownHwnd  = new WindowInteropHelper(this).Handle;
        var mainHwnd = new WindowInteropHelper(_mainWindow).Handle;
        if (fgHwnd == ownHwnd || fgHwnd == mainHwnd) return false;

        System.Text.StringBuilder sb = new System.Text.StringBuilder(256);
        GetClassName(fgHwnd, sb, sb.Capacity);
        string className = sb.ToString();
        if (className == "WorkerW" || className == "Progman") return false;

        if (!GetWindowRect(fgHwnd, out RECT r)) return false;

        // Use physical pixel screen dimensions via WinForms
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null) return false;

        bool coversScreen = r.Left  <= screen.Bounds.Left
                         && r.Top   <= screen.Bounds.Top
                         && r.Right >= screen.Bounds.Right
                         && r.Bottom >= screen.Bounds.Bottom;

        if (!coversScreen) return false;

        // For auto-hide taskbars: if the taskbar is currently raised (e.g. Win key pressed),
        // its rect grows from a 2px sliver to full height — treat that as non-fullscreen.
        // Skip this check for always-visible taskbars because their height is always full.
        var abd = new APPBARDATA { cbSize = (uint)Marshal.SizeOf<APPBARDATA>() };
        bool taskbarIsAutoHide = (SHAppBarMessage(ABM_GETSTATE, ref abd) & ABS_AUTOHIDE) != 0;

        if (taskbarIsAutoHide)
        {
            var taskbarHwnd = FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero && GetWindowRect(taskbarHwnd, out RECT tb))
            {
                int tbHeight = tb.Bottom - tb.Top;
                if (tbHeight > 10) return false;  // auto-hide taskbar is currently raised
            }
        }

        return true;
    }

    /// <summary>Cursor is touching the bottom edge AND horizontally over the widget — enters peek.</summary>
    private bool CursorNearBottom()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null) return false;

        var pos = System.Windows.Forms.Cursor.Position;
        if (pos.Y < screen.Bounds.Bottom - 2) return false;  // not near bottom edge

        // Check horizontal overlap with the widget (physical pixels)
        double dpiScale = PresentationSource.FromVisual(this)
                              ?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
        double physLeft  = Left  * dpiScale;
        double physRight = (Left + ActualWidth) * dpiScale;

        return pos.X >= physLeft - 20 && pos.X <= physRight + 20;  // 20px horizontal margin
    }

    /// <summary>Cursor has moved clearly above the widget — exits peek so the widget slides back down.</summary>
    private bool CursorClearlyAboveWidget()
    {
        var screen = System.Windows.Forms.Screen.PrimaryScreen;
        if (screen is null) return false;
        double dpiScale = PresentationSource.FromVisual(this)
                              ?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;
        // Hide again only once the cursor is >30 logical px above the widget's top edge
        double physicalWidgetTop = _normalTop * dpiScale;
        return System.Windows.Forms.Cursor.Position.Y < physicalWidgetTop - 30;
    }


    // ── Slide animations ─────────────────────────────────────────────────────

    private void SlideIn()
    {
        if (!IsVisible)
        {
            // Ensure widget is on-screen position before showing
            base.Show();
            SetWindowPos(new WindowInteropHelper(this).Handle, HWND_TOPMOST,
                         0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        var anim = new DoubleAnimation(
            _normalTop,
            new Duration(TimeSpan.FromMilliseconds(220)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        BeginAnimation(TopProperty, anim);
    }

    private void SlideOut()
    {
        double screenH = SystemParameters.PrimaryScreenHeight;
        var anim = new DoubleAnimation(
            screenH + ActualHeight + 10,   // fully below the screen
            new Duration(TimeSpan.FromMilliseconds(200)))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        BeginAnimation(TopProperty, anim);
    }

    // ── Positioning ──────────────────────────────────────────────────────────

    private void SnapToPosition()
    {
        if (ActualWidth < 1 || ActualHeight < 1) return;

        var    settings = SettingsService.Instance;
        var    wa       = SystemParameters.WorkArea;
        double screenW  = SystemParameters.PrimaryScreenWidth;
        double screenH  = SystemParameters.PrimaryScreenHeight;
        double taskbarH = screenH - wa.Bottom;

        double top;
        if (taskbarH < 8)
            top = screenH - ActualHeight - 4;
        else
            top = wa.Bottom + (taskbarH - ActualHeight) / 2.0;

        double offsetX = settings.TaskbarWidgetOffsetX;
        double left = settings.TaskbarWidgetSide switch
        {
            TaskbarWidgetSide.Left   => wa.Left  + offsetX,
            TaskbarWidgetSide.Center => (screenW - ActualWidth) / 2.0,
            TaskbarWidgetSide.Right  => wa.Right - ActualWidth - offsetX,
            _                        => wa.Left  + offsetX,
        };

        left = Math.Clamp(left, 0, Math.Max(0, screenW - ActualWidth));
        top  = Math.Clamp(top,  0, Math.Max(0, screenH - ActualHeight));

        _normalTop = top;
        Left = left;

        // Only update Top directly when not in the middle of a fullscreen hide
        if (!_isFullscreen || _isPeeking)
        {
            BeginAnimation(TopProperty, null); // clear any running animation
            Top = _normalTop;
        }
    }

    // ── System events ────────────────────────────────────────────────────────

    private void OnSystemChange(object? sender, EventArgs e)
        => Dispatcher.BeginInvoke(SnapToPosition);

    // ── WndProc hook ─────────────────────────────────────────────────────────

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (!_shouldBeVisible) return IntPtr.Zero;

        if (msg == WM_WINDOWPOSCHANGING)
        {
            var pos = Marshal.PtrToStructure<WINDOWPOS>(lParam);
            if ((pos.flags & SWP_NOZORDER) == 0)
            {
                pos.hwndInsertAfter = HWND_TOPMOST;
                Marshal.StructureToPtr(pos, lParam, false);
            }
        }
        else if (msg == WM_WINDOWPOSCHANGED && !_inWndProc)
        {
            _inWndProc = true;
            SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            _inWndProc = false;
        }

        return IntPtr.Zero;
    }

    // ── Mouse events ─────────────────────────────────────────────────────────

    private void Widget_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mainWindow.ToggleVisibility();

        if (_mainWindow.IsVisible)
        {
            _mainWindow.UpdateLayout();
            double screenW = SystemParameters.PrimaryScreenWidth;

            // In fullscreen peek: float the flyout just above the widget, near the screen bottom.
            // Normally: anchor to the work-area bottom edge.
            double flyoutTop = (_isFullscreen && _isPeeking)
                ? _normalTop - _mainWindow.ActualHeight - 8
                : SystemParameters.WorkArea.Bottom - _mainWindow.ActualHeight - 8;

            flyoutTop = Math.Max(0, flyoutTop);   // don't go off top of screen

            _mainWindow.Left = Math.Max(0, Left - 12);
            _mainWindow.Top  = flyoutTop;

            if (_mainWindow.Left + _mainWindow.ActualWidth > screenW)
                _mainWindow.Left = screenW - _mainWindow.ActualWidth - 8;
        }
        else if (_isFullscreen && _isPeeking)
        {
            // Media control was dismissed while peeking in fullscreen — slide widget back down
            _isPeeking = false;
            HideForFullscreen();
        }
    }

    private void Widget_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        => WidgetBorder.Opacity = 1.0;

    private void Widget_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        => WidgetBorder.Opacity = 1.0;
}
