using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media.Imaging;

namespace MyTools.Plugins;

public static class ClipboardItemMeta
{
    private static readonly Regex TitleDimensionRegex = new(
        @"\[(\d+)\s*[×*]\s*(\d+)\]",
        RegexOptions.Compiled);

    public static string FormatDimensionSubtitle(int width, int height) =>
        width > 0 && height > 0 ? $"[{width}×{height}]" : string.Empty;

    public static string FormatListTitle(string? summary, int width, int height)
    {
        var displaySummary = NormalizeImageSummary(summary);
        var dimensions = FormatDimensionSubtitle(width, height);
        if (string.IsNullOrEmpty(dimensions))
        {
            return displaySummary;
        }

        return string.IsNullOrEmpty(displaySummary)
            ? dimensions
            : $"{displaySummary} {dimensions}";
    }

    public static bool TryParsePreviewDimensions(string? title, out string dimensions)
    {
        dimensions = string.Empty;
        if (string.IsNullOrWhiteSpace(title))
        {
            return false;
        }

        var match = TitleDimensionRegex.Match(title);
        if (!match.Success)
        {
            return false;
        }

        dimensions = $"{match.Groups[1].Value} × {match.Groups[2].Value}";
        return true;
    }

    private static string NormalizeImageSummary(string? summary)
    {
        if (string.IsNullOrWhiteSpace(summary))
        {
            return string.Empty;
        }

        var trimmed = summary.Trim();
        return trimmed is "[Image]" or "Image" ? "Image" : trimmed;
    }

    public static string FormatByteSize(long bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        if (bytes < 1024 * 1024)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0.#} KB");
        }

        return string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024.0 * 1024.0):0.#} MB");
    }

    public static string FormatLocalTimestamp(DateTime? timestamp, CultureInfo? culture = null)
    {
        if (timestamp is not { } value || value == default)
        {
            return string.Empty;
        }

        var local = value.Kind switch
        {
            DateTimeKind.Utc => value.ToLocalTime(),
            DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Utc).ToLocalTime(),
            _ => value
        };

        return local.ToString("g", culture ?? CultureInfo.CurrentCulture);
    }

    public static (int width, int height, int byteSize) ReadClipboardImageMeta()
    {
        if (!Clipboard.ContainsImage())
        {
            return (0, 0, 0);
        }

        var image = Clipboard.GetImage();
        if (image == null)
        {
            return (0, 0, 0);
        }

        using var ms = new MemoryStream();
        var encoder = new BmpBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        encoder.Save(ms);
        return (image.PixelWidth, image.PixelHeight, (int)ms.Length);
    }
}
