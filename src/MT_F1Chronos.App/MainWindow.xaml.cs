using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using MT_F1Chronos.App.Services;
using Application = System.Windows.Application;

namespace MT_F1Chronos.App;

public partial class MainWindow : Window
{
    private readonly AppController _controller;
    private readonly NotifyIcon _trayIcon;
    private HwndSource? _hwndSource;

    public MainWindow(AppController controller)
    {
        _controller = controller;

        WindowState = WindowState.Minimized;
        ShowInTaskbar = false;
        Width = 0;
        Height = 0;

        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Visible = true,
            Text = "MT_F1Chronos",
        };

        var menu = new ContextMenuStrip();
        menu.Items.Add("Nouveau nom de chrono (Ctrl+Shift+N)", null, (_, _) => _controller.RequestNamePrompt(force: true));
        menu.Items.Add("Repositionner l'overlay", null, (_, _) => _controller.PositionOverlay());
        menu.Items.Add("-");
        menu.Items.Add("Quitter", null, (_, _) => Application.Current.Shutdown());

        _trayIcon.ContextMenuStrip = menu;
        _trayIcon.DoubleClick += (_, _) => _controller.RequestNamePrompt(force: true);

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwndSource = (HwndSource)PresentationSource.FromVisual(this)!;
        _hwndSource.AddHook(WndProc);
        HotKeyHelper.Register(_hwndSource.Handle);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == HotKeyHelper.WmHotKey && wParam == (IntPtr)HotKeyHelper.NewSessionId)
        {
            _controller.RequestNamePrompt(force: true);
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        if (_hwndSource is not null)
        {
            HotKeyHelper.Unregister(_hwndSource.Handle);
            _hwndSource.RemoveHook(WndProc);
        }

        _trayIcon.Visible = false;
        _trayIcon.Dispose();
    }
}
