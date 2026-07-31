using System.Globalization;
using System.Windows.Controls;
using System.Windows.Data;

namespace MyTools.Desktop.Converters;

public class BooleanToScrollBarVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isWordWrapEnabled)
        {
            // 当启用换行时，隐藏横向滚动条；否则显示
            return isWordWrapEnabled ? ScrollBarVisibility.Hidden : ScrollBarVisibility.Auto;
        }
        return ScrollBarVisibility.Auto;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ScrollBarVisibility scrollBarVisibility)
        {
            return scrollBarVisibility == ScrollBarVisibility.Hidden;
        }
        return false;
    }
}
