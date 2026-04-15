using System.Diagnostics;
using NAudio.CoreAudioApi;

namespace MediaOverlay.Services;

/// <summary>
/// Controls audio volume via WASAPI.
///
/// When a media source is active (<see cref="SetTargetApp"/> is called with a
/// process name), the slider controls only that app's per-session volume so
/// other apps (e.g. Discord) are not affected.  When no target is set it falls
/// back to the system master volume.
/// </summary>
public class VolumeService : IDisposable
{
    private readonly MMDeviceEnumerator _enumerator;
    private MMDevice? _device;
    private bool _disposed;

    // Lowercase Win32 process name to target, or null for master volume.
    private string? _targetProcessName;

    /// <summary>
    /// Raised when the master volume changes externally (hardware keys, tray, etc.).
    /// Only fires in master-volume mode; per-app changes are not propagated here.
    /// Fires on a background thread — callers must marshal to the UI thread.
    /// </summary>
    public event Action<float, bool>? VolumeChanged;

    public VolumeService()
    {
        _enumerator = new MMDeviceEnumerator();
        RefreshDevice();
    }

    // ── Target app ───────────────────────────────────────────────────────────

    /// <summary>
    /// Sets the process whose audio session the slider should control.
    /// Pass <c>null</c> or empty to revert to master volume.
    /// </summary>
    /// <returns>
    /// The current volume (0–100) and mute state for the new target so the
    /// caller can refresh the UI without an extra round-trip.
    /// </returns>
    public (float volume, bool muted) SetTargetApp(string? processName)
    {
        _targetProcessName = string.IsNullOrWhiteSpace(processName)
            ? null
            : processName.ToLowerInvariant();

        return (Volume, IsMuted);
    }

    // ── Volume ───────────────────────────────────────────────────────────────

    /// <summary>Gets or sets volume as 0–100 for the active target (app or master).</summary>
    public float Volume
    {
        get
        {
            try
            {
                var session = FindAppSession();
                if (session is not null)
                    return session.SimpleAudioVolume.Volume * 100f;

                return (_device?.AudioEndpointVolume.MasterVolumeLevelScalar ?? 0f) * 100f;
            }
            catch { return 0f; }
        }
        set
        {
            try
            {
                float clamped = Math.Clamp(value / 100f, 0f, 1f);

                var session = FindAppSession();
                if (session is not null)
                {
                    session.SimpleAudioVolume.Volume = clamped;
                    return;
                }

                if (_device is not null)
                    _device.AudioEndpointVolume.MasterVolumeLevelScalar = clamped;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VolumeService] Set volume error: {ex.Message}");
            }
        }
    }

    /// <summary>Gets or sets mute state for the active target (app or master).</summary>
    public bool IsMuted
    {
        get
        {
            try
            {
                var session = FindAppSession();
                if (session is not null)
                    return session.SimpleAudioVolume.Mute;

                return _device?.AudioEndpointVolume.Mute ?? false;
            }
            catch { return false; }
        }
        set
        {
            try
            {
                var session = FindAppSession();
                if (session is not null)
                {
                    session.SimpleAudioVolume.Mute = value;
                    return;
                }

                if (_device is not null)
                    _device.AudioEndpointVolume.Mute = value;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[VolumeService] Set mute error: {ex.Message}");
            }
        }
    }

    // ── Internals ────────────────────────────────────────────────────────────

    private void RefreshDevice()
    {
        _device?.Dispose();
        _device = _enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        _device.AudioEndpointVolume.OnVolumeNotification += OnVolumeNotification;
    }

    /// <summary>
    /// Master-volume change handler.  Only raised to the UI when we're in
    /// master-volume mode; per-app mode has its own independent volume so we
    /// don't want the master-volume notification to stomp the slider.
    /// </summary>
    private void OnVolumeNotification(AudioVolumeNotificationData data)
    {
        if (_targetProcessName is null)
            VolumeChanged?.Invoke(data.MasterVolume, data.Muted);
    }

    /// <summary>
    /// Finds the first active (or any) WASAPI audio session for
    /// <see cref="_targetProcessName"/>.  Returns <c>null</c> when the target
    /// is not set or no matching session exists yet.
    /// </summary>
    private AudioSessionControl? FindAppSession()
    {
        if (_targetProcessName is null || _device is null) return null;

        try
        {
            var sessions = _device.AudioSessionManager.Sessions;
            AudioSessionControl? fallback = null;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];
                try
                {
                    int pid = (int)session.GetProcessID;
                    if (pid == 0) continue; // system/idle session

                    var proc = Process.GetProcessById(pid);
                    if (!proc.ProcessName.Equals(_targetProcessName, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Prefer the session that has audio activity (volume > 0)
                    if (session.SimpleAudioVolume.Volume > 0f)
                        return session;

                    fallback ??= session; // keep as backup if none are audibly active
                }
                catch
                {
                    // Process may have exited between GetProcessID and GetProcessById
                }
            }

            return fallback;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[VolumeService] FindAppSession error: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_device is not null)
            _device.AudioEndpointVolume.OnVolumeNotification -= OnVolumeNotification;
        _device?.Dispose();
        _enumerator.Dispose();
    }
}
