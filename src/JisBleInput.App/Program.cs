using System.Windows;
using JisBleInput.Core;

namespace JisBleInput.App;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        var running = false;
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
            var stoppingAfterError = false;
            application.DispatcherUnhandledException += async (_, args) =>
            {
                args.Handled = true;
                if (stoppingAfterError) return;
                stoppingAfterError = true;
                try
                {
                    try { await window.StopAfterUnexpectedErrorAsync(); }
                    catch { /* Best-effort cleanup before shutting down. */ }
                    ReportFailure(args.Exception, running: true);
                }
                finally { application.Shutdown(1); }
            };
            application.SessionEnding += (_, _) => window.PrepareForSessionEnd();
            application.Exit += (_, _) => window.DisposeTrayIcon();
            InstanceActivation.Listen(window);
            running = true;
            application.Run(window);
        }
        catch (Exception error)
        {
            ReportFailure(error, running);
        }
    }

    private static void ReportFailure(Exception error, bool running)
    {
        var message = running ? "実行中にエラーが発生したため終了します。受信側の入力とキー状態を確認してください。\n次回つながらない場合は、受信PCのBluetooth接続を確認してください。"
            : "起動できませんでした。";
        MessageBox.Show($"{message}\n{error.GetType().Name} / 0x{error.HResult:X8}", "JIS BLE Input");
    }
}
