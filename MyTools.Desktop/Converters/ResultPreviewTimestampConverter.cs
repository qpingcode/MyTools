using System.Globalization;
using System.Windows.Data;
using MyTools.Common;
using MyTools.Plugins;

namespace MyTools.Desktop.Converters;

public sealed class ResultPreviewTimestampConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ResultItem item)
        {
            return string.Empty;
        }

        var formatCulture = Equals(culture, CultureInfo.InvariantCulture)
            ? CultureInfo.CurrentCulture
            : culture;
        return ClipboardItemMeta.FormatLocalTimestamp(item.CreatedAt, formatCulture);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
