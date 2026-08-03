using System.Collections;
using System.Globalization;
using System.Reflection;
using System.Text.RegularExpressions;

namespace MyTools.Common.Localization;

/// <summary>
/// Framework-neutral description of a user-visible message.
/// </summary>
public sealed record LocalizedMessage(
    string Key,
    string DefaultValue,
    IReadOnlyDictionary<string, object?>? Values = null,
    string? TranslatorComment = null)
{
    public LocalizedMessage(string key, string defaultValue, object? values, string? translatorComment = null)
        : this(key, defaultValue, ToDictionary(values), translatorComment)
    {
    }

    public string Resolve(ILocalizationService localizationService) =>
        localizationService.GetCaption(Key, DefaultValue, Values, TranslatorComment);

    public string FormatFallback(CultureInfo? culture = null) =>
        Format(DefaultValue, Values, culture ?? CultureInfo.CurrentCulture);

    public override string ToString() => FormatFallback();

    public static string Format(
        string template,
        IReadOnlyDictionary<string, object?>? values,
        CultureInfo culture)
    {
        if (values == null || values.Count == 0 || string.IsNullOrEmpty(template))
        {
            return template;
        }

        try
        {
            return PlaceholderPattern.Replace(template, match =>
            {
                var name = match.Groups[1].Value;
                if (!values.TryGetValue(name, out var value))
                {
                    return match.Value;
                }

                return value is IFormattable formattable
                    ? formattable.ToString(null, culture) ?? string.Empty
                    : value?.ToString() ?? string.Empty;
            });
        }
        catch
        {
            return template;
        }
    }

    public static IReadOnlyDictionary<string, object?>? ToDictionary(object? values)
    {
        if (values == null)
        {
            return null;
        }

        if (values is IReadOnlyDictionary<string, object?> readOnlyDictionary)
        {
            return readOnlyDictionary;
        }

        if (values is IDictionary dictionary)
        {
            return dictionary.Keys.Cast<object>()
                .Where(key => key is string)
                .ToDictionary(key => (string)key, key => dictionary[key]);
        }

        return values.GetType()
            .GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => property.CanRead && property.GetIndexParameters().Length == 0)
            .ToDictionary(property => property.Name, property => property.GetValue(values));
    }

    private static readonly Regex PlaceholderPattern = new(
        @"\{\{\s*([A-Za-z_][A-Za-z0-9_.-]*)\s*\}\}",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}

