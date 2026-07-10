using System.Windows;
using MT_F1Chronos.App.Services;
using Application = System.Windows.Application;

namespace MT_F1Chronos.App;

public partial class App : Application
{
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _controller = new AppController();
        MainWindow = _controller.CreateOverlay();
        _controller.Start();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }
}
