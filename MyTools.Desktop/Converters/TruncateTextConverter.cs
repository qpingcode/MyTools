using System.Globalization;
using System.Windows.Data;

namespace MyTools.Desktop.Converters;

public class TruncateTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string text)
            return string.Empty;

        if (parameter is not string maxLengthStr || !int.TryParse(maxLengthStr, out int maxLength))
            maxLength = 100;

        if (text.Length <= maxLength)
            return text;

        return text.Substring(0, maxLength) + "...";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
} 