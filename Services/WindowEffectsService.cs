using System.Runtime.InteropServices;

namespace MediaOverlay.Services;

/// <summary>
/// Applies Windows blur / acrylic backdrop to a WPF window via Win32.
///
/// Works with AllowsTransparency="True" windows by using the undocumented
/// SetWindowCompositionAttribute API (supported on Windows 10 1703+ and Windows 11).
/// On Windows 11 also requests OS-level rounded corners via DwmSetWindowAttribute.
/// </summary>
public static class WindowEffectsService
{
    // ── P/Invoke declarations ────────────────────────────────────────────────

    [DllImport("user32.dll")]
    private static extern int SetWindowCompositionAttribute(
        IntPtr hwnd, ref WindowCompositionAttribData data);

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd, int attr, ref int attrValue, int attrSize);

    // ── Structs / Enums ──────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowCompositionAttribData
    {
        public WindowCompositionAttribute Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct AccentPolicy
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;   // 0xAABBGGRR  (ABGR, not ARGB)
        public int AnimationId;
    }

    private enum WindowCompositionAttribute
    {
        WCA_ACCENT_POLICY = 19
    }

    // ── Constants ────────────────────────────────────────────────────────────

    private const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;
    private const int DWMWA_WINDOW_CORNER_PREFERENCE  = 33;
    private const int DWMWCP_ROUND                    = 2;  // round all corners

    // ── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Enables the acrylic blur effect behind the window.
    /// <paramref name="tintAbgr"/> is the tint in 0xAABBGGRR format.
    /// A low alpha keeps the blur visible; XAML provides the main background tint.
    /// </summary>
    public static void ApplyAcrylic(IntPtr hwnd,
        int tintAbgr = unchecked((int)0x30181825))
    {
        var accent = new AccentPolicy
        {
            AccentState   = ACCENT_ENABLE_ACRYLICBLURBEHIND,
            AccentFlags   = 0,
            GradientColor = tintAbgr,
            AnimationId   = 0
        };

        int size    = Marshal.SizeOf(accent);
        IntPtr ptr  = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var attribData = new WindowCompositionAttribData
            {
                Attribute  = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data       = ptr,
                SizeOfData = size
            };
            SetWindowCompositionAttribute(hwnd, ref attribData);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }

        // Windows 11 (build ≥ 22000): round corners at the compositor level.
        // This is in addition to WPF CornerRadius — DWM handles the clip.
        if (Environment.OSVersion.Version.Build >= 22000)
        {
            int pref = DWMWCP_ROUND;
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE,
                                  ref pref, Marshal.SizeOf(pref));
        }
    }

    /// <summary>Removes the acrylic effect (restores default solid background).</summary>
    public static void RemoveAcrylic(IntPtr hwnd)
    {
        var accent = new AccentPolicy { AccentState = 0 };
        int size   = Marshal.SizeOf(accent);
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var attribData = new WindowCompositionAttribData
            {
                Attribute  = WindowCompositionAttribute.WCA_ACCENT_POLICY,
                Data       = ptr,
                SizeOfData = size
            };
            SetWindowCompositionAttribute(hwnd, ref attribData);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
