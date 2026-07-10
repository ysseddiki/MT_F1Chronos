using System.Windows;
using MT_F1Chronos.App.Services;
using MT_F1Chronos.App.Windows;

namespace MT_F1Chronos.App;

public partial class App : Application
{
    private AppController? _controller;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _controller = new AppController();
        _controller.Start();

        MainWindow = new MainWindow(_controller);
        MainWindow.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _controller?.Dispose();
        base.OnExit(e);
    }
}
