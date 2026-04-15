using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using MediaOverlay.Services;
using MediaOverlay.ViewModels;

namespace MediaOverlay;

public partial class SettingsWindow : Window
{
    private readonly MainWindow      _mainWindow;
    private readonly TaskbarWidget?  _taskbarWidget;
    private readonly SettingsService _settings = SettingsService.Instance;
    private bool _loading = true;  // suppress events during initial load

    public SettingsWindow(MainWindow mainWindow, TaskbarWidget? taskbarWidget)
    {
        InitializeComponent();
        _mainWindow    = mainWindow;
        _taskbarWidget = taskbarWidget;

        PopulateControls();
        LoadFromSettings();
        _loading = false;
    }

    // ── Populate combo-box items ──────────────────────────────────────────────

    private void PopulateControls()
    {
        CmbAutoHide.Items.Add(new ComboBoxItem { Content = "Never",      Tag = 0  });
        CmbAutoHide.Items.Add(new ComboBoxItem { Content = "3 seconds",  Tag = 3  });
        CmbAutoHide.Items.Add(new ComboBoxItem { Content = "5 seconds",  Tag = 5  });
        CmbAutoHide.Items.Add(new ComboBoxItem { Content = "10 seconds", Tag = 10 });

        CmbFlyoutPosition.Items.Add(new ComboBoxItem { Content = "↙  Bottom Right", Tag = WidgetPosition.BottomRight });
        CmbFlyoutPosition.Items.Add(new ComboBoxItem { Content = "↘  Bottom Left",  Tag = WidgetPosition.BottomLeft  });
        CmbFlyoutPosition.Items.Add(new ComboBoxItem { Content = "↗  Top Right",    Tag = WidgetPosition.TopRight    });
        CmbFlyoutPosition.Items.Add(new ComboBoxItem { Content = "↖  Top Left",     Tag = WidgetPosition.TopLeft     });

        CmbWidgetSide.Items.Add(new ComboBoxItem { Content = "◀  Left",   Tag = TaskbarWidgetSide.Left   });
        CmbWidgetSide.Items.Add(new ComboBoxItem { Content = "●  Center", Tag = TaskbarWidgetSide.Center });
        CmbWidgetSide.Items.Add(new ComboBoxItem { Content = "▶  Right",  Tag = TaskbarWidgetSide.Right  });
    }

    // ── Load settings → UI ───────────────────────────────────────────────────

    private void LoadFromSettings()
    {
        ChkStartWithWindows.IsChecked = _settings.StartWithWindows;
        ChkShowInTaskbar.IsChecked    = _settings.ShowInTaskbar;
        ChkCompact.IsChecked          = _settings.CompactMode;
        ChkTaskbarWidget.IsChecked    = _settings.TaskbarWidgetEnabled;
        ChkAutoPause.IsChecked        = _settings.AutoPauseOthers;

        // Auto-hide
        foreach (ComboBoxItem item in CmbAutoHide.Items)
            if ((int)item.Tag == _settings.AutoHideDuration) { CmbAutoHide.SelectedItem = item; break; }

        // Flyout position
        foreach (ComboBoxItem item in CmbFlyoutPosition.Items)
            if ((WidgetPosition)item.Tag == _settings.FlyoutPosition) { CmbFlyoutPosition.SelectedItem = item; break; }

        // Widget side
        foreach (ComboBoxItem item in CmbWidgetSide.Items)
            if ((TaskbarWidgetSide)item.Tag == _settings.TaskbarWidgetSide) { CmbWidgetSide.SelectedItem = item; break; }
    }

    // ── Apply settings live ──────────────────────────────────────────────────

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        ApplyAndSave();
    }

    private void ApplyAndSave()
    {
        // Read UI → settings
        _settings.StartWithWindows     = ChkStartWithWindows.IsChecked == true;
        _settings.ShowInTaskbar        = ChkShowInTaskbar.IsChecked    == true;
        _settings.CompactMode          = ChkCompact.IsChecked          == true;
        _settings.TaskbarWidgetEnabled = ChkTaskbarWidget.IsChecked    == true;
        _settings.AutoPauseOthers      = ChkAutoPause.IsChecked        == true;

        if (CmbAutoHide.SelectedItem is ComboBoxItem ah)
            _settings.AutoHideDuration = (int)ah.Tag;

        if (CmbFlyoutPosition.SelectedItem is ComboBoxItem fp)
            _settings.FlyoutPosition = (WidgetPosition)fp.Tag;

        if (CmbWidgetSide.SelectedItem is ComboBoxItem ws)
            _settings.TaskbarWidgetSide = (TaskbarWidgetSide)ws.Tag;

        // Apply live ──────────────────────────────────────────────────────
        _settings.Save();
        _settings.ApplyStartWithWindows();

        // MainWindow
        _mainWindow.ShowInTaskbar = _settings.ShowInTaskbar;
        _mainWindow.SetAutoHideDuration(_settings.AutoHideDuration);
        _mainWindow.SetWidgetPosition(_settings.FlyoutPosition);

        if (_mainWindow.DataContext is MainViewModel vm)
        {
            vm.IsCompact       = _settings.CompactMode;
            vm.AutoPauseOthers = _settings.AutoPauseOthers;
        }

        // Taskbar widget
        if (_taskbarWidget is not null)
        {
            if (_settings.TaskbarWidgetEnabled)
            {
                _taskbarWidget.Show();
                _taskbarWidget.ApplySettings();
            }
            else
            {
                _taskbarWidget.Hide();
            }
        }
    }

    // ── Close ────────────────────────────────────────────────────────────────

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}
