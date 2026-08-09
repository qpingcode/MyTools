using System.Collections.Frozen;
using MyTools.Common.Theming;

namespace MyTools.Desktop.Themes;

/// <summary>
/// Maps a <see cref="ThemeKind"/> to the CSS custom properties injected into WebView2
/// plugin detail pages. This is the Web-side counterpart of <c>Themes/Light.xaml</c> /
/// <c>Themes/Dark.xaml</c> and must stay in sync with them (§15 decision 5).
///
/// Values are inlined into the bootstrap script before the first frame, so the page
/// renders with the correct theme from the start — eliminating the flash of the
/// default background that would otherwise appear before <c>initialize-detail</c>
/// arrives (see design §10).
/// </summary>
public static class WebThemeTokens
{
    /// <summary>
    /// The semantic CSS variable names exposed to plugins. Keys here mirror the
    /// token brush keys in the xaml (minus the "Brush" suffix and with the
    /// <c>--mt-</c> prefix).
    /// </summary>
    public static readonly FrozenDictionary<string, string> Light = new Dictionary<string, string>
    {
        ["--mt-surface-bg"] = "#F5F5F5",
        ["--mt-surface"] = "#FFFFFF",
        ["--mt-surface-alt"] = "#ECECEC",
        ["--mt-surface-hover"] = "#E5E7EB",
        ["--mt-text"] = "#1E1E1E",
        ["--mt-text-muted"] = "#333333",
        ["--mt-text-tertiary"] = "#666666",
        ["--mt-text-disabled"] = "#999999",
        ["--mt-border"] = "#DDDDDD",
        ["--mt-border-subtle"] = "#CCCCCC",
        ["--mt-accent"] = "#3F51B5",
        ["--mt-accent-hover"] = "#303F9F",
        ["--mt-accent-pressed"] = "#1A237E",
        ["--mt-selection"] = "#1A3F51B5",
        ["--mt-shadow"] = "#000000",
    }.ToFrozenDictionary();

    public static readonly FrozenDictionary<string, string> Dark = new Dictionary<string, string>
    {
        ["--mt-surface-bg"] = "#1E1E1E",
        ["--mt-surface"] = "#292929",
        ["--mt-surface-alt"] = "#333333",
        ["--mt-surface-hover"] = "#3A3A3A",
        ["--mt-text"] = "#FFFFFF",
        ["--mt-text-muted"] = "#CCCCCC",
        ["--mt-text-tertiary"] = "#AAAAAA",
        ["--mt-text-disabled"] = "#666666",
        ["--mt-border"] = "#404040",
        ["--mt-border-subtle"] = "#555555",
        ["--mt-accent"] = "#3F51B5",
        ["--mt-accent-hover"] = "#303F9F",
        ["--mt-accent-pressed"] = "#1A237E",
        ["--mt-selection"] = "#1A3F51B5",
        ["--mt-shadow"] = "#000000",
    }.ToFrozenDictionary();

    public static IReadOnlyDictionary<string, string> For(ThemeKind theme) =>
        theme == ThemeKind.Light ? Light : Dark;
}
