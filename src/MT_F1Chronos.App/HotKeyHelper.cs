using System.Runtime.InteropServices;
using System.Windows.Input;

namespace MT_F1Chronos.App;

public static class HotKeyHelper
{
    public const int WmHotKey = 0x0312;
    public const int NewSessionId = 9001;

    [DllImport("user32.dll")]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;

    public static void Register(IntPtr handle)
    {
        var vk = (uint)KeyInterop.VirtualKeyFromKey(Key.N);
        RegisterHotKey(handle, NewSessionId, ModControl | ModShift, vk);
    }

    public static void Unregister(IntPtr handle) => UnregisterHotKey(handle, NewSessionId);
}
