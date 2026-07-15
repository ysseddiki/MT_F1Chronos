using System.Runtime.InteropServices;

namespace MT_F1Chronos.App;

/// <summary>
/// Keeps a WPF window above other windows (including borderless games).
/// Exclusive fullscreen games can still cover the overlay — use Borderless/Windowed.
/// </summary>
public static class TopMostHelper
{
    private static readonly IntPtr HwndTopmost = new(-1);

    private const uint SwpNomove = 0x0002;
    private const uint SwpNosize = 0x0001;
    private const uint SwpNoactivate = 0x0010;
    private const uint SwpShowwindow = 0x0040;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint uFlags);

    public static void Assert(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return;

        SetWindowPos(
            hwnd,
            HwndTopmost,
            0,
            0,
            0,
            0,
            SwpNomove | SwpNosize | SwpNoactivate | SwpShowwindow);
    }
}
