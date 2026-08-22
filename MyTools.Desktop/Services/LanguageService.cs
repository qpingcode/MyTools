using System.Globalization;
using System.Resources;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;

namespace MyTools.Desktop.Services;

public class LanguageService : ILocalizationService
{
    private const string DefaultLocale = GeneralSettings.DefaultLanguage;
    private static readonly ResourceManager ResourceManager = new(
        "MyTools.Desktop.Localization.HostStrings",
        typeof(LanguageService).Assembly);
    private readonly IConfigurationStorage storage;

    public LanguageService(IConfigurationStorage storage)
    {
        this.storage = storage;
        var savedLanguage = storage.Retrieve(GeneralSettings.LanguagePath);
        CurrentCulture = TryGetSupportedCulture(savedLanguage) ?? GetDefaultCulture();
        ApplyCulture(CurrentCulture);
    }

    public CultureInfo CurrentCulture { get; private set; }
    public string CurrentLocale => CurrentCulture.Name;

    public IReadOnlyList<CultureInfo> SupportedCultures { get; } =
    [
        new("zh-CN"),
        new("fr-FR"),
        new(DefaultLocale)
    ];

    /// <summary>
    /// 语言名称用该语言自己的写法，不随界面语言翻译，方便用户认出自己的语言。
    /// </summary>
    public static string GetNativeDisplayName(CultureInfo culture)
    {
        ArgumentNullException.ThrowIfNull(culture);
        return GetNativeDisplayName(culture.Name);
    }

    public static string GetNativeDisplayName(string locale)
    {
        return locale switch
        {
            "zh-CN" => "中文",
            "fr-FR" => "Français",
            "en-US" => "English",
            _ => CultureInfo.GetCultureInfo(locale).NativeName
        };
    }

    private CultureInfo GetDefaultCulture()
    {
        var systemCulture = CultureInfo.CurrentUICulture;
        return SupportedCultures.FirstOrDefault(culture =>
                   systemCulture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
               ?? new CultureInfo(DefaultLocale);
    }

    public void ChangeLanguage(string languageCode)
    {
        var culture = TryGetSupportedCulture(languageCode)
            ?? throw new ArgumentException($"Unsupported locale: {languageCode}", nameof(languageCode));
        if (string.Equals(culture.Name, CurrentCulture.Name, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var previousLocale = CurrentCulture.Name;
        CurrentCulture = culture;
        storage.Store(GeneralSettings.LanguagePath, culture.Name);
        ApplyCulture(culture);
        LocaleChanged?.Invoke(this, new LocaleChangedEventArgs(previousLocale, culture.Name));
        ResourceDictionaryChanged?.Invoke(this, EventArgs.Empty);
    }

    public bool SetLanguageForNextStartup(string languageCode)
    {
        var culture = TryGetSupportedCulture(languageCode)
            ?? throw new ArgumentException($"Unsupported locale: {languageCode}", nameof(languageCode));
        if (string.Equals(culture.Name, CurrentCulture.Name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        storage.Store(GeneralSettings.LanguagePath, culture.Name);
        return true;
    }

    public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null)
    {
        var resource = ResourceManager.GetString(key, CurrentCulture)
            ?? ResourceManager.GetString(key, CultureInfo.GetCultureInfo(DefaultLocale))
            ?? defaultValue
            ?? key;
        return LocalizedMessage.Format(resource, LocalizedMessage.ToDictionary(values), CurrentCulture);
    }

    private static void ApplyCulture(CultureInfo culture)
    {
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }

    private CultureInfo? TryGetSupportedCulture(string? locale)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return null;
        }

        return SupportedCultures.FirstOrDefault(culture =>
            string.Equals(culture.Name, locale, StringComparison.OrdinalIgnoreCase));
    }

    public static string GetCaption(string key, string? fallback)
    {
        var service = ServiceLocator.GetRequiredService<ILocalizationService>();
        return service.GetCaption(key, fallback ?? key);
    }

    public static string GetCaption(string key, string fallback, params object?[] args)
    {
        var service = ServiceLocator.GetRequiredService<ILocalizationService>();
        var resource = service.GetCaption(key, fallback);
        try
        {
            return string.Format(CultureInfo.CurrentCulture, resource, args);
        }
        catch (FormatException)
        {
            return resource;
        }
    }

    public event EventHandler<LocaleChangedEventArgs>? LocaleChanged;

    [Obsolete("Use LocaleChanged instead.")]
    public event EventHandler? ResourceDictionaryChanged;
}
