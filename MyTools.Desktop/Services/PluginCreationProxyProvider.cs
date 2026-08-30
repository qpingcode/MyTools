using MyTools.AI;
using MyTools.Common.Config.Interfaces;

namespace MyTools.Desktop.Services;

public sealed class PluginCreationProxyProvider(IConfigurationRegistry registry)
    : IPluginCreationProxyProvider
{
    internal const string ProxySettingPath = GeneralSettings.UpdateProxyUrl;

    public PluginCreationProxySettings GetProxySettings()
    {
        var configured = registry.FindSetting(ProxySettingPath)?.GetValue<string>();
        var proxyUri = UpdateService.ParseProxyUri(configured);
        return new PluginCreationProxySettings(
            proxyUri,
            proxyUri is null
                ? $"MyTools Settings ({ProxySettingPath}, direct)"
                : $"MyTools Settings ({ProxySettingPath})");
    }
}
