using System.Globalization;
using System.Windows.Data;

namespace MyTools.Desktop.Converters;

public class DoubleToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double doubleValue)
        {
            return double.IsNaN(doubleValue) ? "" : doubleValue.ToString("F2", culture);
        }
        return "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string strValue && double.TryParse(strValue, out double result))
        {
            return result;
        }
        return 0.0;
    }
}