using System.Drawing;
using System.Windows.Forms;
using MediaOverlay.Services;
using MediaOverlay.ViewModels;

namespace MediaOverlay.Services;

/// <summary>
/// Manages the system-tray icon and its context menu.
/// Streamlined: Show/Hide, Pin Widget, Settings, Exit.
/// </summary>
public class TrayService : IDisposable
{
    private readonly NotifyIcon       _notifyIcon;
    private readonly MainWindow       _window;
    private          ToolStripMenuItem _widgetMenuItem = null!;
    private bool _disposed;

    public TrayService(MainWindow window)
    {
        _window = window;

        _notifyIcon = new NotifyIcon
        {
            Text    = "FlyMedia — Ctrl+Shift+M to show",
            Visible = true,
        };

        try   { _notifyIcon.Icon = new Icon("Assets/icon.ico"); }
        catch { _notifyIcon.Icon = SystemIcons.Application; }

        BuildContextMenu();

        _notifyIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
                ToggleWindow();
        };

        _notifyIcon.ShowBalloonTip(
            timeout:  3000,
            tipTitle: "FlyMedia is running",
            tipText:  "Press Ctrl+Shift+M to show · Right-click for options.",
            tipIcon:  ToolTipIcon.Info);
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    private void BuildContextMenu()
    {
        var menu = new ContextMenuStrip();

        // Show / Hide
        menu.Items.Add("Show / Hide  (Ctrl+Shift+M)", null, (_, _) => ToggleWindow());

        // Pin widget
        _widgetMenuItem = new ToolStripMenuItem("📌  Pin as Widget");
        _widgetMenuItem.Click += (_, _) => ToggleWidgetMode();
        menu.Items.Add(_widgetMenuItem);

        menu.Items.Add(new ToolStripSeparator());

        // ⚙ Settings
        menu.Items.Add("⚙  Settings", null, (_, _) => OpenSettings());

        menu.Items.Add(new ToolStripSeparator());

        // Exit
        menu.Items.Add("Exit", null, (_, _) => ExitApp());

        _notifyIcon.ContextMenuStrip = menu;
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private void ToggleWindow()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (_window.IsVisible)
                _window.Hide();
            else
            {
                _window.Show();
                _window.Activate();
            }
        });
    }

    private void ToggleWidgetMode()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _window.ToggleWidgetMode();

            bool isPinned = _window.IsWidgetMode;

            // Persist widget mode
            SettingsService.Instance.IsWidgetMode = isPinned;
            SettingsService.Instance.Save();

            _widgetMenuItem.Text = isPinned
                ? "📌  Unpin Widget"
                : "📌  Pin as Widget";
            _notifyIcon.Text = isPinned
                ? "FlyMedia — pinned widget"
                : "FlyMedia — Ctrl+Shift+M to show";
        });
    }

    private void OpenSettings()
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var app = Application.Current as App;
            var settingsWin = new SettingsWindow(_window, app?.TaskbarWidgetInstance);
            settingsWin.ShowDialog();
        });
    }

    private void ExitApp()
        => Application.Current.Dispatcher.Invoke(() => Application.Current.Shutdown());

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
    }
}
