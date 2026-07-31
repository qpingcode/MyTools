using System.Globalization;
using System.Windows;
using MyTools.Desktop.Models;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;

namespace MyTools.Desktop.Services
{
    public class LanguageService
    {
        AppConfigService appConfigService;
        public LanguageService(AppConfigService appConfigService)
        {
            this.appConfigService = appConfigService;
            string savedLanguage = appConfigService.AppConfig.Language;
            if (string.IsNullOrEmpty(savedLanguage))
            {
                throw new ArgumentNullException(savedLanguage);
            }
            CurrentCulture = new CultureInfo(savedLanguage);
            ApplyCulture(CurrentCulture);
        }

        public CultureInfo CurrentCulture { get; private set; }

        public List<CultureInfo> SupportedCultures { get; } =
        [
            new CultureInfo("zh-CN"),
            new CultureInfo("en-US")
        ];

        private CultureInfo GetDefaultCulture()
        {
            var systemCulture = CultureInfo.CurrentUICulture;

            foreach (var culture in SupportedCultures)
            {
                if (systemCulture.TwoLetterISOLanguageName == culture.TwoLetterISOLanguageName)
                {
                    return culture;
                }
            }

            return new CultureInfo("en-US");
        }

        public void ChangeLanguage(string languageCode)
        {
            var newCulture = new CultureInfo(languageCode);
            ChangeLanguage(newCulture);
        }

        private void ChangeLanguage(CultureInfo culture)
        {
            if (culture.Equals(CurrentCulture))
                return;

            CurrentCulture = culture;
            appConfigService.SetLanguage(culture.Name);
            ApplyCulture(culture);

            ResourceDictionaryChanged?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyCulture(CultureInfo culture)
        {
            Thread.CurrentThread.CurrentCulture = culture;
            Thread.CurrentThread.CurrentUICulture = culture;

            RefreshResourceDictionaries();
        }

        private void RefreshResourceDictionaries()
        {
            var oldDictionaries = new List<ResourceDictionary>();
            var newDictionary = new ResourceDictionary();

            foreach (ResourceDictionary dictionary in Application.Current.Resources.MergedDictionaries)
            {
                if (dictionary.Source != null && dictionary.Source.OriginalString.Contains("/Resources/Strings."))
                {
                    oldDictionaries.Add(dictionary);
                }
            }

            foreach (var oldDictionary in oldDictionaries)
            {
                Application.Current.Resources.MergedDictionaries.Remove(oldDictionary);
            }

            string langCode = CurrentCulture.Name;
            Uri resourceUri = new Uri($"pack://application:,,,/MyTools.Desktop;component/Resources/Strings.{langCode}.xaml", UriKind.Absolute);

            try
            {
                newDictionary.Source = resourceUri;
                Application.Current.Resources.MergedDictionaries.Add(newDictionary);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"加载语言资源失败: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);

                if (langCode != "en-US")
                {
                    Uri defaultUri = new Uri($"pack://application:,,,/MyTools.Desktop;component/Resources/Strings.en-US.xaml", UriKind.Absolute);
                    newDictionary.Source = defaultUri;
                    Application.Current.Resources.MergedDictionaries.Add(newDictionary);
                }
            }
        }

        public static string GetCaption(string key, string? fallback)
        {
            var resources = Application.Current.Resources;
            return resources[key] as string ?? fallback ?? key;
        }
        
        public static string GetCaption(string key, string fallback, params object?[] args)
        {
            var resources = Application.Current.Resources;
            var resourceString = resources[key] as string ?? fallback;
            return string.Format(resourceString, args);
        }

        public event EventHandler? ResourceDictionaryChanged;
    }
}