using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace MT_F1Chronos.App.Native;

public static class ClickThroughHelper
{
    private const int GwlExStyle = -20;
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hwnd, int index, int newStyle);

    public static void EnableClickThrough(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, style | WsExTransparent | WsExToolWindow | WsExNoActivate);
    }

    public static void DisableClickThrough(Window window)
    {
        var handle = new WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
            return;

        var style = GetWindowLong(handle, GwlExStyle);
        SetWindowLong(handle, GwlExStyle, (style | WsExToolWindow) & ~WsExTransparent & ~WsExNoActivate);
    }
}
