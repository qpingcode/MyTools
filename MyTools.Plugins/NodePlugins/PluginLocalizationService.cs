using MyTools.Common.Localization;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// 插件级别的 <see cref="ILocalizationService"/> 适配器。
/// 从指定插件的 messages 字典（由 plugin.json 的 i18n catalog + locale 文件加载）查询翻译，
/// 而非宿主级的 .resx 资源。这样 <see cref="LocalizedMessage.Resolve(ILocalizationService)"/>
/// 就能直接解析插件自己的资源字符串。
/// </summary>
public sealed class PluginLocalizationService : ILocalizationService
{
    private readonly IReadOnlyDictionary<string, string> messages;
    private readonly string currentLocale;

    /// <param name="messages">已加载的插件 messages 字典。</param>
    /// <param name="currentLocale">当前 locale（如 "zh-CN"）。</param>
    public PluginLocalizationService(IReadOnlyDictionary<string, string> messages, string currentLocale)
    {
        this.messages = messages;
        this.currentLocale = currentLocale;
    }

    public string CurrentLocale => currentLocale;

    public event EventHandler<LocaleChangedEventArgs>? LocaleChanged
    {
        add { }
        remove { }
    }

    public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null)
    {
        var resource = messages.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : defaultValue;
        return LocalizedMessage.Format(resource ?? key, LocalizedMessage.ToDictionary(values), System.Globalization.CultureInfo.CurrentCulture);
    }
}

