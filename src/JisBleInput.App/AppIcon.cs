using System.Drawing;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace JisBleInput.App;

internal static class AppIcon
{
    private static Stream Open() => typeof(AppIcon).Assembly.GetManifestResourceStream("JisBleInput.AppIcon.ico")
        ?? throw new InvalidOperationException("アプリアイコンを読み込めませんでした。");

    public static Icon CreateNotificationIcon()
    {
        using var stream = Open();
        using var icon = new Icon(stream);
        return (Icon)icon.Clone();
    }

    public static ImageSource CreateWindowIcon()
    {
        using var stream = Open();
        var decoder = new IconBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        var image = decoder.Frames.OrderByDescending(frame => frame.PixelWidth).First();
        image.Freeze();
        return image;
    }
}
