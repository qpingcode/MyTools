using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Desktop.Models;
using MyTools.Desktop.Services;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class CommandRunnerSettingsMigratorTest
{
    [Test]
    public void ParseLegacy_ShouldReadScriptsArrayAndTrailingComma()
    {
        const string json = """
            [
              {
                "name": "Ping",
                "command": "ping",
                "args": "127.0.0.1",
                "runAsAdmin": false,
                "isBashScript": true,
                "scripts": ["echo one", "echo two"],
                "workingDirectory": "C:\\\\tmp"
              },
            ]
            """;

        var commands = CommandRunnerSettingsMigrator.ParseLegacy(json);

        Assert.That(commands, Has.Count.EqualTo(1));
        Assert.That(commands[0].Name, Is.EqualTo("Ping"));
        Assert.That(commands[0].IsBashScript, Is.True);
        Assert.That(commands[0].Scripts, Is.EqualTo(new[] { "echo one", "echo two" }).AsCollection);
    }

    [Test]
    public void ToStoredElement_ShouldJoinScriptsWithNewlines()
    {
        var json = CommandRunnerSettingsMigrator.ToStoredElement(
        [
            new CommandConfig
            {
                Name = "Ping",
                IsBashScript = true,
                Scripts = ["echo one", "echo two"],
                RunAsAdmin = true
            }
        ]);

        Assert.That(json.GetArrayLength(), Is.EqualTo(1));
        Assert.That(json[0].GetProperty("name").GetString(), Is.EqualTo("Ping"));
        Assert.That(json[0].GetProperty("isBashScript").GetBoolean(), Is.True);
        Assert.That(json[0].GetProperty("runAsAdmin").GetBoolean(), Is.True);
        Assert.That(json[0].GetProperty("scripts").GetString(), Is.EqualTo("echo one\necho two"));
    }

    [Test]
    public void Migrate_ShouldCopyLegacyFileIntoSettingAndRename()
    {
        var storage = new MemoryStorage();
        var registry = new ConfigurationRegistry(storage);
        var category = registry.AddCategory("command-runner", "Custom Commands", "desc");
        registry.AddSetting(
            category,
            "Commands",
            "Commands",
            "",
            JsonSerializer.SerializeToElement(Array.Empty<object>()),
            new JsonElementSettingSerializer(),
            valueType: SettingValueTypes.Array);

        var tempDir = Path.Combine(Path.GetTempPath(), "MyTools-CommandRunner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var legacyPath = Path.Combine(tempDir, "CommandRunner.json");
        File.WriteAllText(legacyPath, """
            [
              { "name": "Notepad", "command": "notepad.exe", "args": "", "runAsAdmin": false }
            ]
            """);

        try
        {
            CommandRunnerSettingsMigrator.Migrate(registry, NullLogger.Instance, legacyPath);

            Assert.That(File.Exists(legacyPath), Is.False);
            Assert.That(File.Exists(legacyPath + ".bak"), Is.True);
            var stored = storage.Retrieve(CommandRunnerSettingsMigrator.SettingFullPath);
            Assert.That(stored, Is.Not.Null);
            using var document = JsonDocument.Parse(stored!);
            Assert.That(document.RootElement.GetArrayLength(), Is.EqualTo(1));
            Assert.That(document.RootElement[0].GetProperty("name").GetString(), Is.EqualTo("Notepad"));
            Assert.That(document.RootElement[0].GetProperty("command").GetString(), Is.EqualTo("notepad.exe"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void Migrate_ShouldNotOverwriteExistingSetting()
    {
        var storage = new MemoryStorage();
        var registry = new ConfigurationRegistry(storage);
        var category = registry.AddCategory("command-runner", "Custom Commands", "desc");
        var setting = registry.AddSetting(
            category,
            "Commands",
            "Commands",
            "",
            JsonSerializer.SerializeToElement(Array.Empty<object>()),
            new JsonElementSettingSerializer(),
            valueType: SettingValueTypes.Array);
        setting.CurrentValue = JsonSerializer.SerializeToElement(new[] { new { name = "KeepMe" } });
        registry.SaveChanges();

        var tempDir = Path.Combine(Path.GetTempPath(), "MyTools-CommandRunner-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var legacyPath = Path.Combine(tempDir, "CommandRunner.json");
        File.WriteAllText(legacyPath, """[{ "name": "ShouldNotImport" }]""");

        try
        {
            CommandRunnerSettingsMigrator.Migrate(registry, NullLogger.Instance, legacyPath);

            Assert.That(File.Exists(legacyPath), Is.True);
            using var document = JsonDocument.Parse(storage.Retrieve(CommandRunnerSettingsMigrator.SettingFullPath)!);
            Assert.That(document.RootElement[0].GetProperty("name").GetString(), Is.EqualTo("KeepMe"));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    private sealed class MemoryStorage : IConfigurationStorage
    {
        private readonly Dictionary<string, string> items = new(StringComparer.OrdinalIgnoreCase);

        public void Store(string name, string value) => items[name] = value;
        public string? Retrieve(string name) => items.TryGetValue(name, out var value) ? value : null;
        public bool Exists(string name) => items.ContainsKey(name);
        public void Delete(string name) => items.Remove(name);
        public void Clear() => items.Clear();
        public IEnumerable<string> GetAllNames() => items.Keys.ToList();
        public void Initialize() { }
        public void Dispose() { }
    }
}
