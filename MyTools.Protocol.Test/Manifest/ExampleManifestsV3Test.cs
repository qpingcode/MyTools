using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Manifest;

/// <summary>
/// Validates that each example plugin.json parses and satisfies the protocol 3.0 manifest rules.
/// </summary>
[TestFixture]
public class ExampleManifestsV3Test
{
    private static IEnumerable<string> FindExampleManifests()
    {
        var root = TestContext.CurrentContext.TestDirectory;
        var dir = root;
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            var candidate = Path.Combine(dir, "MyTools.Plugins", "Examples");
            if (Directory.Exists(candidate))
            {
                return Directory.GetFiles(candidate, "plugin.json", SearchOption.AllDirectories)
                    .Where(path =>
                    {
                        var relative = Path.GetRelativePath(candidate, path);
                        return !relative.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}")
                               && !relative.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")
                               && !relative.StartsWith("sdk-v3", StringComparison.OrdinalIgnoreCase)
                               && !relative.StartsWith("common", StringComparison.OrdinalIgnoreCase);
                    });
            }
            dir = Path.GetDirectoryName(dir);
        }
        return [];
    }

    [Test]
    public void AllExampleManifests_ShouldBeValid()
    {
        var manifests = FindExampleManifests().ToList();
        Assert.That(manifests, Is.Not.Empty, "no plugin.json manifests found");

        foreach (var path in manifests)
        {
            var json = File.ReadAllText(path);
            var m = JsonSerializer.Deserialize<PluginManifestV3>(json, ProtocolJsonOptions.Default)!;
            var result = PluginManifestV3Validator.Validate(m);

            Assert.That(result.IsValid, Is.True,
                $"manifest {Path.GetFileName(Path.GetDirectoryName(path))}/plugin.json is invalid: {result.Error?.Message}");
        }
    }

    [Test]
    public void SettingsManifest_ShouldDeclareExactHostCapabilities()
    {
        var manifests = FindExampleManifests().ToList();
        var settingsPath = manifests.FirstOrDefault(p => p.Contains("settings"));
        Assert.That(settingsPath, Is.Not.Null, "settings plugin.json not found");

        var m = JsonSerializer.Deserialize<PluginManifestV3>(File.ReadAllText(settingsPath!), ProtocolJsonOptions.Default)!;
        Assert.That(m.Entries[0].Capabilities, Is.EquivalentTo(new[]
        {
            "configuration.read", "configuration.write",
            "keymap.read", "keymap.write", "keymap.validate",
            "hotkeys.read", "hotkeys.write", "hotkeys.validate",
            "gestures.read", "gestures.write", "gestures.suspend", "gestures.resume",
            "action.capture",
            "commandRunner.read", "commandRunner.write",
            "path.pick", "path.validate",
            "restart"
        }));
    }
}
