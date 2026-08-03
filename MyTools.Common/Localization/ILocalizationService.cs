namespace MyTools.Common.Localization;

/// <summary>
/// Resolves host and built-in plugin messages without depending on a UI framework.
/// </summary>
public interface ILocalizationService
{
    string CurrentLocale { get; }

    string GetCaption(
        string key,
        string defaultValue,
        object? values = null,
        string? translatorComment = null);

    event EventHandler<LocaleChangedEventArgs>? LocaleChanged;
}

public sealed class LocaleChangedEventArgs(string previousLocale, string currentLocale) : EventArgs
{
    public string PreviousLocale { get; } = previousLocale;
    public string CurrentLocale { get; } = currentLocale;
}

