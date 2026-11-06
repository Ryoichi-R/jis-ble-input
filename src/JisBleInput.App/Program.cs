using System.Windows;
using JisBleInput.Core;

namespace JisBleInput.App;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        try
        {
            using var instance = new SingleInstanceLease("Local\\JisBleInput.App");
            if (!instance.IsOwner)
            {
                InstanceActivation.Request();
                return;
            }
            var application = new Application { ShutdownMode = ShutdownMode.OnLastWindowClose };
            var window = new MainWindow();
            application.SessionEnding += (_, _) => window.PrepareForSessionEnd();
            application.Exit += (_, _) => window.DisposeTrayIcon();
            InstanceActivation.Listen(window);
            application.Run(window);
        }
        catch (Exception error)
        {
            MessageBox.Show($"起動できませんでした。\n{error.GetType().Name} / 0x{error.HResult:X8}", "JIS BLE Input");
        }
    }
}
