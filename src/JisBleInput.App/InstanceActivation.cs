using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace JisBleInput.App;

internal static class InstanceActivation
{
    private static readonly uint Message = RegisterWindowMessage("JisBleInput.App.Activate");

    public static void Request() => PostMessage(new IntPtr(0xFFFF), Message, IntPtr.Zero, IntPtr.Zero);

    public static void Listen(MainWindow window)
    {
        window.SourceInitialized += (_, _) =>
        {
            var source = HwndSource.FromHwnd(new WindowInteropHelper(window).Handle);
            source?.AddHook((IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled) =>
            {
                if (Message != 0 && message == Message)
                {
                    window.RestoreFromTray();
                    handled = true;
                }
                return IntPtr.Zero;
            });
        };
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessage(string text);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
