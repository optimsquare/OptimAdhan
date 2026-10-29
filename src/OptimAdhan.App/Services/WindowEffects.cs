using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace OptimAdhan.App.Services;

/// <summary>
/// Windows 11 materials through the Desktop Window Manager. DWM composites the
/// blur on the GPU, so frosted glass costs the app nothing per frame. Every
/// call is a no-op where unsupported; callers keep an opaque fallback brush.
/// </summary>
public static class WindowEffects
{
    public enum Backdrop
    {
        None = 1,
        Mica = 2,
        Acrylic = 3,
        MicaAlt = 4,
    }

    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaWindowCornerPreference = 33;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaSystemBackdropType = 38;
    private const int DwmwcpRound = 2;
    private const int DwmwcpRoundSmall = 3;
    private const int WmNcActivate = 0x0086;

    /// <summary>System backdrops arrived in Windows 11 22H2 (build 22621).</summary>
    public static bool SupportsBackdrops { get; } = Environment.OSVersion.Version.Build >= 22621;

    /// <summary>
    /// False when the user turned off "Transparency effects" (Settings › Personalization › Colors)
    /// or the platform lacks backdrops. Glass surfaces then use their opaque fallback.
    /// </summary>
    public static bool TransparencyEnabled
    {
        get
        {
            if (!SupportsBackdrops)
            {
                return false;
            }

            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("EnableTransparency") is not int value || value != 0;
            }
            catch
            {
                return true;
            }
        }
    }

    /// <summary>Windows' "Animation effects" setting; when off, the app skips its transitions.</summary>
    public static bool AnimationsEnabled => SystemParameters.ClientAreaAnimation;

    public static void UseDarkFrame(IntPtr hwnd)
    {
        var on = 1;
        _ = DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int));
    }

    public static void RoundCorners(IntPtr hwnd, bool small = false)
    {
        var pref = small ? DwmwcpRoundSmall : DwmwcpRound;
        _ = DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref pref, sizeof(int));
    }

    /// <summary>Colours the thin DWM window border (Windows 11). Format is COLORREF (0x00BBGGRR).</summary>
    public static void SetBorderColor(IntPtr hwnd, Color color)
    {
        var colorRef = color.R | (color.G << 8) | (color.B << 16);
        _ = DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref colorRef, sizeof(int));
    }

    /// <summary>
    /// Applies a system backdrop behind the whole window. The WPF content must be
    /// drawn over a transparent composition background for it to show.
    /// Returns false when unavailable (the caller should paint an opaque fallback).
    /// </summary>
    public static bool TrySetBackdrop(Window window, Backdrop backdrop)
    {
        if (!TransparencyEnabled)
        {
            return false;
        }

        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        if (HwndSource.FromHwnd(hwnd) is not { } source)
        {
            return false;
        }

        source.CompositionTarget.BackgroundColor = Colors.Transparent;
        var margins = new Margins { Left = -1, Right = -1, Top = -1, Bottom = -1 };
        if (DwmExtendFrameIntoClientArea(hwnd, ref margins) != 0)
        {
            return false;
        }

        var type = (int)backdrop;
        return DwmSetWindowAttribute(hwnd, DwmwaSystemBackdropType, ref type, sizeof(int)) == 0;
    }

    /// <summary>
    /// Keeps DWM drawing the window as active. System backdrops otherwise fall back
    /// to a flat colour on windows that never take focus, such as the widget.
    /// (Verified on Windows 11: real acrylic with this, flat colour without it.)
    /// </summary>
    public static void KeepVisuallyActive(Window window)
    {
        var hwnd = new WindowInteropHelper(window).EnsureHandle();
        HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wParam, IntPtr lParam, ref bool handled) =>
        {
            if (msg == WmNcActivate)
            {
                handled = true;
                return DefWindowProc(h, msg, 1, lParam);
            }

            return IntPtr.Zero;
        });
        SendMessage(hwnd, WmNcActivate, 1, IntPtr.Zero);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Margins
    {
        public int Left;
        public int Right;
        public int Top;
        public int Bottom;
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    private static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("user32.dll", EntryPoint = "DefWindowProcW")]
    private static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, nint wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int msg, nint wParam, IntPtr lParam);
}
