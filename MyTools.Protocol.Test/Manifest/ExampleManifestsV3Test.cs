using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Manifest;

/// <summary>
/// Validates that each migrated v3 example manifest parses and satisfies the v3 manifest rules.
/// As more examples are migrated to plugin.v3.json they are picked up automatically.
/// </summary>
[TestFixture]
public class ExampleManifestsV3Test
{
    private static IEnumerable<string> FindV3Manifests()
    {
        var root = TestContext.CurrentContext.TestDirectory;
        // Walk up to the repo root, then into MyTools.Plugins/Examples.
        var dir = root;
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "MyTools.Plugins", "Examples");
            if (Directory.Exists(candidate))
            {
                return Directory.GetFiles(candidate, "plugin.v3.json", SearchOption.AllDirectories);
            }
            dir = Path.GetDirectoryName(dir);
        }
        return [];
    }

    [Test]
    public void AllV3ExampleManifests_ShouldBeValid()
    {
        var manifests = FindV3Manifests().ToList();
        Assert.That(manifests, Is.Not.Empty, "no plugin.v3.json manifests found");

        foreach (var path in manifests)
        {
            var json = File.ReadAllText(path);
            var m = JsonSerializer.Deserialize<PluginManifestV3>(json, ProtocolJsonOptions.Default)!;
            var result = PluginManifestV3Validator.Validate(m);

            Assert.That(result.IsValid, Is.True,
                $"manifest {Path.GetFileName(Path.GetDirectoryName(path))}/plugin.v3.json is invalid: {result.Error?.Message}");
        }
    }

    [Test]
    public void SettingsV3Manifest_ShouldDeclareConfigurationWrite()
    {
        var manifests = FindV3Manifests().ToList();
        var settingsPath = manifests.FirstOrDefault(p => p.Contains("settings"));
        Assume.That(settingsPath, Is.Not.Null, "settings plugin.v3.json not migrated yet");

        var m = JsonSerializer.Deserialize<PluginManifestV3>(File.ReadAllText(settingsPath!), ProtocolJsonOptions.Default)!;
        Assert.That(m.Entries[0].Capabilities, Contains.Item("configuration.write"));
    }
}
