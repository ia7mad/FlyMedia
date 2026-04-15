using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace MediaOverlay.Services;

/// <summary>Position options for the taskbar widget along the bottom edge.</summary>
public enum TaskbarWidgetSide { Left, Center, Right }

/// <summary>
/// Manages persistent application settings stored as JSON in %APPDATA%/FlyMedia.
/// Call <see cref="Load"/> at startup and <see cref="Save"/> after any change.
/// </summary>
public class SettingsService
{
    private static readonly string _folder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlyMedia");
    private static readonly string _path = Path.Combine(_folder, "settings.json");

    // ── Singleton ─────────────────────────────────────────────────────────────
    public static SettingsService Instance { get; } = new();

    // ── Persisted properties ──────────────────────────────────────────────────

    public int AutoHideDuration { get; set; } = 5;

    public WidgetPosition FlyoutPosition { get; set; } = WidgetPosition.BottomRight;

    public bool IsWidgetMode { get; set; }

    public WidgetPosition WidgetCorner { get; set; } = WidgetPosition.BottomRight;

    public bool CompactMode { get; set; }

    public bool AutoPauseOthers { get; set; }

    public bool TaskbarWidgetEnabled { get; set; } = true;

    public TaskbarWidgetSide TaskbarWidgetSide { get; set; } = TaskbarWidgetSide.Left;

    public double TaskbarWidgetOffsetX { get; set; } = 8;

    public bool StartWithWindows { get; set; }

    public bool ShowInTaskbar { get; set; }

    // ── Load / Save ───────────────────────────────────────────────────────────

    public void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var data = JsonSerializer.Deserialize<SettingsData>(json);
            if (data is null) return;

            AutoHideDuration      = data.AutoHideDuration;
            FlyoutPosition        = data.FlyoutPosition;
            IsWidgetMode          = data.IsWidgetMode;
            WidgetCorner          = data.WidgetCorner;
            CompactMode           = data.CompactMode;
            AutoPauseOthers       = data.AutoPauseOthers;
            TaskbarWidgetEnabled  = data.TaskbarWidgetEnabled;
            TaskbarWidgetSide     = data.TaskbarWidgetSide;
            TaskbarWidgetOffsetX  = data.TaskbarWidgetOffsetX;
            StartWithWindows      = data.StartWithWindows;
            ShowInTaskbar         = data.ShowInTaskbar;
        }
        catch { /* corrupt file — use defaults */ }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(_folder);
            var data = new SettingsData
            {
                AutoHideDuration      = AutoHideDuration,
                FlyoutPosition        = FlyoutPosition,
                IsWidgetMode          = IsWidgetMode,
                WidgetCorner          = WidgetCorner,
                CompactMode           = CompactMode,
                AutoPauseOthers       = AutoPauseOthers,
                TaskbarWidgetEnabled  = TaskbarWidgetEnabled,
                TaskbarWidgetSide     = TaskbarWidgetSide,
                TaskbarWidgetOffsetX  = TaskbarWidgetOffsetX,
                StartWithWindows      = StartWithWindows,
                ShowInTaskbar         = ShowInTaskbar,
            };
            var json = JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_path, json);
        }
        catch { /* best effort */ }
    }

    // ── Start with Windows (registry) ────────────────────────────────────────

    public void ApplyStartWithWindows()
    {
        const string keyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        const string valueName = "FlyMedia";

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true);
            if (key is null) return;

            if (StartWithWindows)
            {
                var exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath))
                    key.SetValue(valueName, $"\"{exePath}\"");
            }
            else
            {
                key.DeleteValue(valueName, throwOnMissingValue: false);
            }
        }
        catch { /* best effort */ }
    }

    // ── Serialization DTO ────────────────────────────────────────────────────

    private class SettingsData
    {
        public int AutoHideDuration { get; set; } = 5;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WidgetPosition FlyoutPosition { get; set; } = WidgetPosition.BottomRight;
        public bool IsWidgetMode { get; set; }
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public WidgetPosition WidgetCorner { get; set; } = WidgetPosition.BottomRight;
        public bool CompactMode { get; set; }
        public bool AutoPauseOthers { get; set; }
        public bool TaskbarWidgetEnabled { get; set; } = true;
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TaskbarWidgetSide TaskbarWidgetSide { get; set; } = TaskbarWidgetSide.Left;
        public double TaskbarWidgetOffsetX { get; set; } = 8;
        public bool StartWithWindows { get; set; }
        public bool ShowInTaskbar { get; set; }
    }
}
