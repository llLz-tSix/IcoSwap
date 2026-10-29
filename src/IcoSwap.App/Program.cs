using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;

namespace IcoSwap.App;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Режим администратора: выполняем задание без UI и выходим
        if (args.Length >= 2 && args[0] == "--elevated-job")
            return Core.Apply.ElevatedJob.Execute(args[1]);

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
