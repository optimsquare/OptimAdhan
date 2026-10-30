using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OptimAdhan.App.Services;

/// <summary>Monitor geometry for the full-screen dashboard (physical pixels, so mixed-DPI setups work).</summary>
public static class MonitorPlacement
{
    private const int MonitorDefaultToNearest = 2;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpFrameChanged = 0x0020;

    /// <summary>The handle of the monitor showing most of the window.</summary>
    public static IntPtr MonitorOf(Window window) =>
        MonitorFromWindow(new WindowInteropHelper(window).Handle, MonitorDefaultToNearest);

    /// <summary>Sizes the window to cover a whole monitor, taskbar included.</summary>
    public static void Cover(Window window, IntPtr monitor)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (hwnd == IntPtr.Zero || !GetMonitorInfo(monitor, ref info))
        {
            return;
        }

        var r = info.Monitor;
        SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top, SwpNoZOrder | SwpNoActivate | SwpFrameChanged);
    }

    /// <summary>True when a saved title-bar area would land on a connected monitor.</summary>
    public static bool IsOnScreen(double left, double top, double width)
    {
        var probe = new Rect(left + 40, top, Math.Max(width - 80, 40), 40);
        var virtualScreen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
            SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        return virtualScreen.IntersectsWith(probe);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect Work;
        public uint Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hwnd, IntPtr insertAfter, int x, int y, int cx, int cy, uint flags);
}
