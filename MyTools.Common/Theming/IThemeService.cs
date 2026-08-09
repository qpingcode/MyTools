namespace MyTools.Common.Theming;

/// <summary>
/// Owns the current application theme without depending on a UI framework.
/// The host implementation is the single source of truth for the theme state;
/// WPF, WebView2 detail pages and Node RPC all read from it.
/// </summary>
public interface IThemeService
{
    ThemeKind CurrentTheme { get; }

    /// <summary>
    /// Raised when the theme changes at runtime. Subscribers refresh their visuals.
    /// Not raised during initial construction, and not raised when the theme is
    /// set to the same value as the current one (idempotent).
    /// </summary>
    event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    /// <summary>
    /// Sets the current theme, persists it, and raises <see cref="ThemeChanged"/>
    /// when the value actually changes. Idempotent: setting the current theme
    /// again is a no-op.
    /// </summary>
    void SetTheme(ThemeKind theme);
}

public sealed class ThemeChangedEventArgs(ThemeKind previousTheme, ThemeKind currentTheme) : EventArgs
{
    public ThemeKind PreviousTheme { get; } = previousTheme;
    public ThemeKind CurrentTheme { get; } = currentTheme;
}
