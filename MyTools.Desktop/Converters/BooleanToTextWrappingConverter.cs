using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyTools.Desktop.Converters;

public class BooleanToTextWrappingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool isWordWrapEnabled)
        {
            return isWordWrapEnabled ? TextWrapping.Wrap : TextWrapping.NoWrap;
        }
        return TextWrapping.Wrap;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is TextWrapping textWrapping)
        {
            return textWrapping == TextWrapping.Wrap;
        }
        return true;
    }
}

