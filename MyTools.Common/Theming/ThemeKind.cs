namespace MyTools.Common.Theming;

/// <summary>
/// The visual color theme. Stable identifier; never localized.
/// </summary>
public enum ThemeKind
{
    Light,
    Dark
}

public static class ThemeKindExtensions
{
    /// <summary>
    /// Wire format used in persistence, WebView2 payloads and Node RPC.
    /// Always lowercase: "light" or "dark".
    /// </summary>
    public static string ToWireString(this ThemeKind kind) => kind == ThemeKind.Light ? "light" : "dark";

    /// <summary>
    /// Parses a wire value back into <see cref="ThemeKind"/>.
    /// Any non-"light" value (null, empty, unknown) falls back to <see cref="ThemeKind.Dark"/>,
    /// matching the default theme. Never throws.
    /// </summary>
    public static ThemeKind Parse(string? value) =>
        string.Equals(value, "light", StringComparison.OrdinalIgnoreCase) ? ThemeKind.Light : ThemeKind.Dark;
}
