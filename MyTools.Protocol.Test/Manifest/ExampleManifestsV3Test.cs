using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using MyTools.Protocol.Errors;
using MyTools.Protocol.Manifest;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Manifest;

/// <summary>
/// Treats every source plugin.json under Examples as a test case and verifies that each manifest
/// is complete, valid, and stable across a serialization round-trip.
/// </summary>
[TestFixture]
public class ExampleManifestsV3Test
{
    private static IReadOnlyList<string> FindExampleManifests()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "MyTools.Plugins", "Examples");
            if (Directory.Exists(candidate))
            {
                return Directory.EnumerateDirectories(candidate)
                    .Select(pluginDirectory => Path.Combine(pluginDirectory, "plugin.json"))
                    .Where(File.Exists)
                    .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            directory = directory.Parent;
        }

        return [];
    }

    private static IEnumerable<TestCaseData> ExampleManifestCases()
    {
        var manifests = FindExampleManifests();
        if (manifests.Count == 0)
        {
            throw new InvalidOperationException("no plugin.json manifests found");
        }

        foreach (var path in manifests)
        {
            var pluginName = Path.GetFileName(Path.GetDirectoryName(path));
            yield return new TestCaseData(path).SetName($"ExampleManifest_{pluginName}_ShouldBeValidAndRoundTrip");
        }
    }

    [TestCaseSource(nameof(ExampleManifestCases))]
    public void ExampleManifest_ShouldBeValidAndRoundTrip(string path)
    {
        var json = File.ReadAllText(path);
        var manifest = JsonSerializer.Deserialize<PluginManifestV3>(json, ProtocolJsonOptions.Default);
        Assert.That(manifest, Is.Not.Null, $"could not deserialize {path}");

        Assert.Multiple(() =>
        {
            Assert.That(manifest!.Id, Is.Not.Empty, "id is required");
            Assert.That(manifest.Version, Is.Not.Empty, "version is required");
            Assert.That(Version.TryParse(manifest.Version, out _), Is.True, "version must be valid");
            Assert.That(manifest.ProtocolVersion, Is.Not.Empty, "protocolVersion is required");
            Assert.That(manifest.Icon, Is.Not.Null.And.Not.Empty, "icon is required");
            Assert.That(manifest.I18n, Is.Not.Null, "i18n is required");
            Assert.That(manifest.Entry, Is.Not.Empty, "entry is required");
        });

        var i18n = manifest!.I18n!;
        Assert.Multiple(() =>
        {
            Assert.That(i18n.DefaultLocale, Is.Not.Empty, "i18n.defaultLocale is required");
            Assert.That(i18n.Catalog, Is.Not.Null.And.Not.Empty, "i18n.catalog is required");
            Assert.That(i18n.LocalesPath, Is.Not.Null.And.Not.Empty, "i18n.localesPath is required");
            Assert.That(i18n.SupportedLocales, Is.Not.Empty, "i18n.supportedLocales is required");
            Assert.That(i18n.SupportedLocales, Does.Contain(i18n.DefaultLocale),
                "i18n.supportedLocales must contain the default locale");
            Assert.That(File.Exists(Path.Combine(Path.GetDirectoryName(path)!, i18n.Catalog!)), Is.True,
                "i18n catalog file is missing");
        });

        foreach (var locale in i18n.SupportedLocales)
        {
            var localePath = Path.Combine(Path.GetDirectoryName(path)!, i18n.LocalesPath!, $"{locale}.json");
            Assert.That(File.Exists(localePath), Is.True, $"locale file is missing: {locale}");
        }

        Assert.Multiple(() =>
        {
            Assert.That(manifest.Entry, Is.Not.Empty, $"plugin '{manifest.Id}' entry path is required");
            Assert.That(manifest.Name, Is.Not.Null, $"plugin '{manifest.Id}' name is required");
            Assert.That(manifest.Name?.Key, Is.Not.Empty, $"plugin '{manifest.Id}' name.key is required");
            Assert.That(manifest.Name?.DefaultValue, Is.Not.Empty,
                $"plugin '{manifest.Id}' name.defaultValue is required");
        });

        var validation = PluginManifestV3Validator.Validate(manifest);
        Assert.That(validation.IsValid, Is.True,
            $"manifest {Path.GetFileName(Path.GetDirectoryName(path))}/plugin.json is invalid: {validation.Error?.Message}");

        var normalized = JsonSerializer.Serialize(manifest, ProtocolJsonOptions.Default);
        var roundTrip = JsonSerializer.Deserialize<PluginManifestV3>(normalized, ProtocolJsonOptions.Default);
        Assert.That(roundTrip, Is.Not.Null);
        Assert.That(JsonSerializer.Serialize(roundTrip, ProtocolJsonOptions.Default), Is.EqualTo(normalized),
            "runtime manifest fields changed during serialization round-trip");
    }
}
