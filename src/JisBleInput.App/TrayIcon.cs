using System.Drawing;
using Forms = System.Windows.Forms;

namespace JisBleInput.App;

internal sealed class TrayIcon : IDisposable
{
    private readonly Icon icon = AppIcon.CreateNotificationIcon();
    private readonly Forms.NotifyIcon notification;
    private bool disposed;
    internal Forms.ContextMenuStrip Menu { get; } = new();
    public bool IsVisible => !disposed && notification.Visible;

    public TrayIcon(Action restore, Action exit)
    {
        Menu.Items.Add("画面を開く", null, (_, _) => restore());
        Menu.Items.Add(new Forms.ToolStripSeparator());
        Menu.Items.Add("終了", null, (_, _) => exit());
        notification = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "JIS BLE Input（ダブルクリックで開く）",
            ContextMenuStrip = Menu,
            Visible = true
        };
        notification.DoubleClick += (_, _) => restore();
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        notification.Visible = false;
        notification.Dispose();
        Menu.Dispose();
        icon.Dispose();
    }
}
