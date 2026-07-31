using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyTools.Desktop.Converters;

public class ImagePathConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string path)
        {
            // 检查是否为图片路径
            if (path.StartsWith("image://", StringComparison.OrdinalIgnoreCase))
            {
                return Visibility.Visible;
            }
            
            // 检查文件扩展名
            var extension = System.IO.Path.GetExtension(path).ToLowerInvariant();
            var imageExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".tiff", ".webp" };
            if (imageExtensions.Contains(extension))
            {
                return Visibility.Visible;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
} 