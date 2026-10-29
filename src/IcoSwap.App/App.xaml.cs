using Microsoft.UI.Xaml;

namespace IcoSwap.App;

public partial class App : Application
{
    private Window? _window;

    public App()
    {
        InitializeComponent();
        UnhandledException += (_, e) =>
        {
            try
            {
                File.AppendAllText(Path.Combine(Core.AppPaths.Root, "error.log"), $"{DateTime.Now:s} {e.Exception}\n\n");
            }
            catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        _window = new MainWindow();
        _window.Activate();
    }
}
