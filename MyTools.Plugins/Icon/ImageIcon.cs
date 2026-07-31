using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MyTools.Common;

namespace MyTools.Plugins;

public class ImageIcon(byte[] imageData) : Icon
{
    readonly Lazy<ImageSource> _lazyImage = new(() => CreateImage(imageData));

    private static ImageSource CreateImage(byte[] imageData)
    {
        var bitmap = new BitmapImage();
        using var ms = new MemoryStream(imageData);
        bitmap.BeginInit();
        bitmap.StreamSource = ms;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }

    public ImageSource Image => _lazyImage.Value;
}