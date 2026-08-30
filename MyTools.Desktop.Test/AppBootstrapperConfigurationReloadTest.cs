using System.Text.Json.Nodes;
using MyTools.Desktop;
using MyTools.Plugins.NodePlugins;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Desktop.Test;

[TestFixture]
public class AppBootstrapperConfigurationReloadTest
{
    [Test]
    public void FindChangedPluginConfigurations_DetectsAddedSetting()
    {
        var previous = AppBootstrapper.CaptureNodePluginConfigurations(
        [
            Manifest("demo", new PluginConfigurationSettingV3
            {
                Key = "Endpoint",
                Type = PluginConfigurationTypes.String,
                DefaultValue = JsonValue.Create("")
            })
        ]);
        var current = AppBootstrapper.CaptureNodePluginConfigurations(
        [
            Manifest(
                "demo",
                new PluginConfigurationSettingV3
                {
                    Key = "Endpoint",
                    Type = PluginConfigurationTypes.String,
                    DefaultValue = JsonValue.Create("")
                },
                new PluginConfigurationSettingV3
                {
                    Key = "Timeout",
                    Type = PluginConfigurationTypes.Int,
                    DefaultValue = JsonValue.Create(30)
                })
        ]);

        Assert.That(
            AppBootstrapper.FindChangedPluginConfigurations(previous, current),
            Is.EquivalentTo(new[] { "demo" }));
    }

    private static NodePluginManifest Manifest(
        string pluginId,
        params PluginConfigurationSettingV3[] configuration) =>
        new()
        {
            Id = pluginId,
            Configuration = configuration
        };
}
