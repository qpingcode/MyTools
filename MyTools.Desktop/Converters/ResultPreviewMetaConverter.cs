using System.Globalization;
using System.Windows.Data;
using MyTools.Common;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Plugins;

namespace MyTools.Desktop.Converters;

public sealed class ResultPreviewMetaConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ResultItem item)
        {
            return string.Empty;
        }

        var localization = ServiceLocator.GetService<ILocalizationService>();
        if (item.PreviewContentType == PreviewContentType.Image)
        {
            var size = ClipboardItemMeta.FormatByteSize(item.Content.Length);
            if (!ClipboardItemMeta.TryParsePreviewDimensions(item.Title, out var dimensions))
            {
                return localization?.GetCaption("Search.Detail.ImageDiskSize", "{{size}}", new { size }) ?? size;
            }

            return localization?.GetCaption(
                       "Search.Detail.ImageMeta",
                       "{{dimensions}} · {{size}}",
                       new { dimensions, size })
                   ?? $"{dimensions} · {size}";
        }

        var count = item.ContentAsString.Length;
        return localization?.GetCaption(
                   "Search.Detail.CharacterCount",
                   "{{count}} characters",
                   new { count })
               ?? $"{count} characters";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
