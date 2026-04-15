using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace MediaOverlay.Services;

/// <summary>
/// Registers a global hotkey (Ctrl+Shift+M) that works even when the app
/// is in the background, using Win32 RegisterHotKey via P/Invoke.
/// </summary>
public class HotkeyService : IDisposable
{
    // ── Win32 P/Invoke ───────────────────────────────────────────────────────

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
        IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    // ── Constants ────────────────────────────────────────────────────────────

    private const int  WM_HOTKEY    = 0x0312;
    private const uint MOD_CONTROL  = 0x0002;
    private const uint MOD_SHIFT    = 0x0004;

    /// <summary>
    /// Prevents the hotkey from firing repeatedly while the keys are held.
    /// Without this flag the toggle would flicker on/off every ~30 ms.
    /// </summary>
    private const uint MOD_NOREPEAT = 0x4000;

    /// <summary>Virtual key code for the letter M.</summary>
    private const uint VK_M        = 0x4D;

    /// <summary>Arbitrary ID that links RegisterHotKey to WM_HOTKEY messages.</summary>
    private const int  HOTKEY_ID   = 9001;

    // ── Fields ───────────────────────────────────────────────────────────────

    private readonly HwndSource _source;
    private readonly IntPtr     _hwnd;
    private bool _disposed;

    /// <summary>Raised on the UI thread whenever Ctrl+Shift+M is pressed.</summary>
    public event Action? HotkeyPressed;

    // ── Constructor ──────────────────────────────────────────────────────────

    public HotkeyService(HwndSource source)
    {
        _source = source;
        _hwnd   = source.Handle;

        // Insert our message handler before WPF's own message pump
        _source.AddHook(WndProc);

        bool ok = RegisterHotKey(_hwnd, HOTKEY_ID,
                                 MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT,
                                 VK_M);

        if (!ok)
        {
            int err = Marshal.GetLastWin32Error();
            // 1409 = ERROR_HOTKEY_ALREADY_REGISTERED
            throw new InvalidOperationException(
                $"Could not register Ctrl+Shift+M (Win32 error {err}). " +
                "Another application may already be using this hotkey.");
        }
    }

    // ── Win32 message hook ───────────────────────────────────────────────────

    private IntPtr WndProc(
        IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
        {
            HotkeyPressed?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    // ── Disposal ─────────────────────────────────────────────────────────────

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _source.RemoveHook(WndProc);
        UnregisterHotKey(_hwnd, HOTKEY_ID);
    }
}
