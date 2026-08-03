using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;

namespace MyTools.Plugins;

internal static class ActionText
{
    public static string Get(string key, string defaultValue, object? values = null) =>
        ServiceLocator.GetRequiredService<ILocalizationService>().GetCaption(key, defaultValue, values);
}

