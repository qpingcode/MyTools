using System.Collections.Frozen;
using System.IO;
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
        ["--mt-accent-foreground"] = "#FFFFFF",
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
        ["--mt-accent-foreground"] = "#FFFFFF",
        ["--mt-selection"] = "#1A3F51B5",
        ["--mt-shadow"] = "#000000",
    }.ToFrozenDictionary();

    public static IReadOnlyDictionary<string, string> For(ThemeKind theme) =>
        theme == ThemeKind.Light ? Light : Dark;

    /// <summary>
    /// The file name used for a theme-specific HTML variant, e.g. "index.dark.html".
    /// </summary>
    public static string ThemeHtmlFileName(string baseFileName, ThemeKind theme)
    {
        var name = Path.GetFileNameWithoutExtension(baseFileName);
        var ext = Path.GetExtension(baseFileName);
        return $"{name}.{theme.ToWireString()}{ext}";
    }

    /// <summary>
    /// Reads an HTML file and injects an inline &lt;style&gt; with the theme's CSS
    /// variables right after &lt;head&gt;. The variables exist at first paint,
    /// eliminating theme flash. Also removes any previously injected style block
    /// (idempotent).
    /// </summary>
    public static string InjectThemeStyle(string html, ThemeKind theme)
    {
        var tokens = For(theme);
        var declarations = string.Join("\n        ",
            tokens.OrderBy(kv => kv.Key, StringComparer.Ordinal)
                  .Select(kv => $"{kv.Key}: {kv.Value};"));
        // Define variables on :root AND set concrete background/color on html/body.
        // The concrete values don't depend on the external style.css loading — they
        // are in this inline <style>, parsed with the HTML. This prevents the white
        // flash between first paint and style.css arrival.
        var bg = tokens.GetValueOrDefault("--mt-surface-bg", "#1E1E1E");
        var text = tokens.GetValueOrDefault("--mt-text", "#FFFFFF");
        var styleTag = $"<style id=\"mytools-theme-tokens\">\n" +
                       $"        :root {{\n        {declarations}\n        }}\n" +
                       $"    </style>";

        // Remove a previously injected theme <style> (idempotent).
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"<style\s+id=""mytools-theme-tokens""[\s\S]*?</style>\s*",
            "");

        // Insert right after <head ...>.
        html = System.Text.RegularExpressions.Regex.Replace(
            html,
            @"(<head[^>]*>)",
            $"$1\n    {styleTag}");

        return html;
    }
}
