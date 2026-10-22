using System.Runtime.InteropServices;
using System.Windows;

namespace StuderBrowser;

internal static class NativeMethods
{
    private const uint GwHwndNext = 2;
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x20;
    private const int DwmwaCloaked = 14;

    [DllImport("user32.dll")]
    public static extern bool ReleaseCapture();

    public static Point CursorPosition() => GetCursorPos(out var point) ? new Point(point.X, point.Y) : default;

    public static IEnumerable<IntPtr> WindowsBelowContaining(IntPtr window, Point screenPoint)
    {
        for (var hwnd = GetWindow(window, GwHwndNext); hwnd != IntPtr.Zero; hwnd = GetWindow(hwnd, GwHwndNext))
        {
            var isClickTarget = IsWindowVisible(hwnd) && !IsIconic(hwnd) && !IsCloaked(hwnd)
                && (GetWindowLongPtr(hwnd, GwlExStyle) & WsExTransparent) == 0;
            if (isClickTarget && GetWindowRect(hwnd, out var rect)
                && screenPoint.X >= rect.Left && screenPoint.X < rect.Right
                && screenPoint.Y >= rect.Top && screenPoint.Y < rect.Bottom)
            {
                yield return hwnd;
            }
        }
    }

    private static bool IsCloaked(IntPtr hwnd) =>
        DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) == 0 && cloaked != 0;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindow(IntPtr hwnd, uint command);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [DllImport("user32.dll")]
    private static extern nint GetWindowLongPtr(IntPtr hwnd, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);
}
