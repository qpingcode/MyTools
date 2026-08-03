using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;

namespace MyTools.Desktop.Localization;

[MarkupExtensionReturnType(typeof(string))]
public sealed class LocExtension(string key) : MarkupExtension
{
    [ConstructorArgument("key")]
    public string Key { get; } = key;

    public string DefaultValue { get; set; } = string.Empty;

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget
            {
                TargetObject: not null
            } target && target.TargetObject.GetType().FullName == "System.Windows.SharedDp")
        {
            return this;
        }

        var localizationService = ServiceLocator.GetRequiredService<ILocalizationService>();
        var source = new LocalizedBinding(localizationService, Key, DefaultValue);
        return new Binding(nameof(LocalizedBinding.Value))
        {
            Source = source,
            Mode = BindingMode.OneWay
        }.ProvideValue(serviceProvider);
    }

    private sealed class LocalizedBinding : INotifyPropertyChanged
    {
        private readonly ILocalizationService localizationService;
        private readonly string key;
        private readonly string defaultValue;

        public LocalizedBinding(ILocalizationService localizationService, string key, string defaultValue)
        {
            this.localizationService = localizationService;
            this.key = key;
            this.defaultValue = defaultValue;
            WeakEventManager<ILocalizationService, LocaleChangedEventArgs>.AddHandler(
                localizationService,
                nameof(ILocalizationService.LocaleChanged),
                OnLocaleChanged);
        }

        public string Value => localizationService.GetCaption(
            key,
            string.IsNullOrEmpty(defaultValue) ? key : defaultValue);

        private void OnLocaleChanged(object? sender, LocaleChangedEventArgs e)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}

