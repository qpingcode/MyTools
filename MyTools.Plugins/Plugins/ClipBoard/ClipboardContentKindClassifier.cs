using System.Windows;

namespace MyTools.Plugins;

/// <summary>
/// Classifies a clipboard snapshot into a single list-icon kind.
/// HTML/RTF are treated as text shells. FileDrop wins over path text.
/// Mixed is text + bitmap without files (companion Office thumbnails may also match).
/// </summary>
public static class ClipboardContentKindClassifier
{
    public const string UnicodeText = "UnicodeText";
    public const string Text = "Text";
    public const string Bitmap = "Bitmap";
    public const string FileDrop = "FileDrop";
    public const string Html = "HTML Format";
    public const string Rtf = "Rich Text Format";

    public static ClipboardContentKind FromClipboard()
    {
        var formats = new List<string>();
        if (Clipboard.ContainsFileDropList())
        {
            formats.Add(FileDrop);
        }

        if (Clipboard.ContainsImage())
        {
            formats.Add(Bitmap);
        }

        if (Clipboard.ContainsText())
        {
            formats.Add(UnicodeText);
        }

        if (Clipboard.ContainsData(DataFormats.Html))
        {
            formats.Add(Html);
        }

        if (Clipboard.ContainsData(DataFormats.Rtf))
        {
            formats.Add(Rtf);
        }

        var text = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
        return Classify(formats, text);
    }

    public static ClipboardContentKind Classify(IEnumerable<string> formats, string? unicodeText = "")
    {
        var set = new HashSet<string>(formats, StringComparer.OrdinalIgnoreCase);
        var hasFiles = set.Contains(FileDrop);
        var hasImage = set.Contains(Bitmap);
        var hasRichText = set.Contains(Html) || set.Contains(Rtf);
        var hasPlainTextFormat = set.Contains(UnicodeText) || set.Contains(Text);
        var hasSubstantialText = !string.IsNullOrWhiteSpace(unicodeText);

        if (hasFiles)
        {
            return ClipboardContentKind.File;
        }

        if (hasImage && hasSubstantialText)
        {
            return ClipboardContentKind.Mixed;
        }

        if (hasImage)
        {
            return ClipboardContentKind.Image;
        }

        if (hasSubstantialText || hasPlainTextFormat || hasRichText)
        {
            return ClipboardContentKind.Text;
        }

        return ClipboardContentKind.Other;
    }
}
