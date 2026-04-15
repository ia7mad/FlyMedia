using System.Windows;
using MediaOverlay.Services;
using MediaOverlay.ViewModels;
using Velopack;
using Velopack.Sources;

namespace MediaOverlay;

public partial class App : System.Windows.Application
{
    private MainWindow?     _mainWindow;
    private TrayService?    _trayService;
    private TaskbarWidget?  _taskbarWidget;

    public TaskbarWidget? TaskbarWidgetInstance => _taskbarWidget;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Load persisted settings
        var settings = SettingsService.Instance;
        settings.Load();

        _mainWindow  = new MainWindow();
        _trayService = new TrayService(_mainWindow);

        // Apply window icon (safe — bad/missing ico won't crash the app)
        try
        {
            var iconUri = new Uri("pack://application:,,,/Assets/icon.ico");
            var stream  = GetResourceStream(iconUri)?.Stream;
            if (stream != null)
            {
                var frame = System.Windows.Media.Imaging.BitmapFrame.Create(
                    stream,
                    System.Windows.Media.Imaging.BitmapCreateOptions.None,
                    System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                _mainWindow.Icon = frame;
            }
        }
        catch { /* icon.ico missing or invalid — continue without icon */ }

        // Apply saved settings to main window
        _mainWindow.ShowInTaskbar = settings.ShowInTaskbar;
        _mainWindow.SetAutoHideDuration(settings.AutoHideDuration);
        _mainWindow.SetWidgetPosition(settings.FlyoutPosition);

        if (_mainWindow.DataContext is MainViewModel vm)
        {
            vm.IsCompact       = settings.CompactMode;
            vm.AutoPauseOthers = settings.AutoPauseOthers;

            // Taskbar widget
            _taskbarWidget = new TaskbarWidget(_mainWindow, vm);
            if (settings.TaskbarWidgetEnabled)
                _taskbarWidget.Show();

            // Re-assert widget Z-order whenever the flyout shows or hides
            _mainWindow.IsVisibleChanged += (_, _) =>
            {
                if (_taskbarWidget is not null)
                    _mainWindow.Dispatcher.BeginInvoke(
                        _taskbarWidget.Reassert,
                        System.Windows.Threading.DispatcherPriority.Loaded);
            };
        }

        // Widget mode (must come after widget position is set)
        if (settings.IsWidgetMode)
        {
            _mainWindow.SetWidgetPosition(settings.WidgetCorner);
            _mainWindow.ToggleWidgetMode();
        }

        // Check for updates silently in the background
        _ = CheckForUpdatesAsync();
    }

    private static async Task CheckForUpdatesAsync()
    {
        try
        {
            var source  = new GithubSource("https://github.com/ia7mad/FlyMedia", null, false);
            var manager = new UpdateManager(source);

            if (!manager.IsInstalled) return;

            var info = await manager.CheckForUpdatesAsync();
            if (info == null) return;

            await manager.DownloadUpdatesAsync(info);
            manager.ApplyUpdatesAndRestart(info);
        }
        catch { /* network unavailable or any update error — silently ignored */ }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _trayService?.Dispose();
        base.OnExit(e);
    }
}
