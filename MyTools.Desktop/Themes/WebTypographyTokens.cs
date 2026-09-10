using System.Collections.Frozen;

namespace MyTools.Desktop.Themes;

/// <summary>
/// Theme-independent typography tokens injected into every Node plugin WebView.
/// Values intentionally mirror Themes/Typography.xaml.
/// </summary>
public static class WebTypographyTokens
{
    public static readonly FrozenDictionary<string, string> All = new Dictionary<string, string>
    {
        ["--mt-font-family-ui"] = "\"Segoe UI Variable Text\", \"Segoe UI\", \"Microsoft YaHei UI\", \"Microsoft YaHei\", sans-serif",
        ["--mt-font-family-mono"] = "\"Cascadia Mono\", \"Cascadia Code\", Consolas, monospace",
        ["--mt-font-size-caption"] = "11px",
        ["--mt-font-size-small"] = "12px",
        ["--mt-font-size-body"] = "14px",
        ["--mt-font-size-subheading"] = "16px",
        ["--mt-font-size-heading-2"] = "18px",
        ["--mt-font-size-heading-1"] = "24px",
        ["--mt-font-size-display"] = "32px",
        ["--mt-line-height-caption"] = "1.4",
        ["--mt-line-height-small"] = "1.4",
        ["--mt-line-height-body"] = "1.5",
        ["--mt-line-height-subheading"] = "1.4",
        ["--mt-line-height-heading-2"] = "1.3",
        ["--mt-line-height-heading-1"] = "1.25",
        ["--mt-line-height-display"] = "1.2",
    }.ToFrozenDictionary();
}
