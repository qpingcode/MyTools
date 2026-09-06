using System.Text.Json;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using MyTools.Common;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Localization;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins.Applications;

[TestFixture]
public class ApplicationsPluginTests
{
    [Test]
    [Apartment(ApartmentState.STA)]
    public async Task ShortcutsAndExecutableAreDeduplicatedByTargetAndArguments()
    {
        var root = Path.Combine(Path.GetTempPath(), "ApplicationsDuplicates-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var plugin = new ApplicationsPlugin(NullLogger<ApplicationsPlugin>.Instance, cache, Mock.Of<ILocalizationService>());
        try
        {
            var executable = Path.Combine(root, "Example.exe");
            await File.WriteAllTextAsync(executable, "");
            CreateShortcut(Path.Combine(root, "Example Desktop.lnk"), executable, "");
            CreateShortcut(Path.Combine(root, "Example Start Menu.lnk"), executable, "");
            CreateShortcut(Path.Combine(root, "Example Profile.lnk"), executable, "--profile Other");
            var scopes = JsonSerializer.SerializeToElement(new[] { new { Path = root } });
            await plugin.RescanAsync(scopes);
            var results = (await plugin.SearchAsync("Example", CancellationToken.None)).Items.ToArray();
            Assert.That(results, Has.Length.EqualTo(2));
            Assert.That(results.Select(item => item.ResultKey).Distinct().Count(), Is.EqualTo(2));
            var normal = results.Single(item => !item.Title.Contains("Profile"));
            Assert.That(normal.SubTitle, Does.EndWith(".lnk"));
            var alias = (await plugin.SearchAsync("Start Menu", CancellationToken.None)).Items.Single();
            Assert.That(alias.ResultKey, Is.EqualTo(normal.ResultKey));
            File.Delete(normal.SubTitle!);
            var fallback = (await plugin.SearchAsync("Example", CancellationToken.None)).Items
                .Single(item => item.ResultKey == normal.ResultKey);
            Assert.That(File.Exists(fallback.SubTitle), Is.True);
            await plugin.RescanAsync(scopes);
            Assert.That((await plugin.SearchAsync("Example", CancellationToken.None)).Items.Count(), Is.EqualTo(2));
        }
        finally { Directory.Delete(root, true); }
    }

    private static void CreateShortcut(string path, string target, string arguments)
    {
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            dynamic shortcut = shell.CreateShortcut(path);
            try
            {
                shortcut.TargetPath = target;
                shortcut.Arguments = arguments;
                shortcut.Save();
            }
            finally { Marshal.FinalReleaseComObject(shortcut); }
        }
        finally { Marshal.FinalReleaseComObject(shell); }
    }

    [Test]
    public async Task StartupReadsSavedScopesAndConfigurationChangesRemoveResults()
    {
        var root = Path.Combine(Path.GetTempPath(), "ApplicationsStartup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var plugin = new ApplicationsPlugin(NullLogger<ApplicationsPlugin>.Instance, cache, Mock.Of<ILocalizationService>());
        var registry = new Mock<IConfigurationRegistry>();
        var category = new ConfigurationCategory { Key = "applications", PluginId = plugin.PluginId };
        ConfigurationSetting? setting = null;
        registry.Setup(r => r.AddCategory(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<MyTools.Common.Plugins.PluginId?>())).Returns(category);
        registry.Setup(r => r.AddSetting(category, "SearchScopes", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<JsonElement>(), It.IsAny<IRegistrySerializer>(), It.IsAny<SettingOptions>(), It.IsAny<SettingValueTypes?>()))
            .Returns((ConfigurationCategory c, string name, string title, string description, JsonElement defaults,
                IRegistrySerializer serializer, SettingOptions options, SettingValueTypes? type) =>
            {
                setting = new ConfigurationSetting
                {
                    Key = ApplicationsPlugin.SearchScopesKey, Name = name, Serializer = serializer,
                    DefaultValue = defaults, ValueType = type!.Value, PluginId = plugin.PluginId
                };
                setting.InitValueWithoutNotify(JsonSerializer.SerializeToElement(new[] { new { Path = root } }));
                category.AddSetting(setting);
                return setting;
            });
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "SavedScope.exe"), "");
            plugin.RegisterSettings(registry.Object);
            Assert.That(setting!.UiHint, Is.EqualTo("directory-list"));
            await plugin.InitializeAsync();
            Assert.That((await plugin.SearchAsync("SavedScope", CancellationToken.None)).Items.Count(), Is.EqualTo(1));
            var empty = JsonSerializer.SerializeToElement(Array.Empty<object>());
            registry.Raise(r => r.ConfigurationChanged += null,
                new ConfigurationChangedEventArgs(setting, setting.CurrentValue, empty));
            Assert.That((await plugin.SearchAsync("SavedScope", CancellationToken.None)).Items, Is.Empty);
        }
        finally { Directory.Delete(root, true); }
    }

    [Test]
    public void DefaultsAndHomeExpansionMatchRequestedScopes()
    {
        var defaults = ApplicationsPlugin.DefaultScopes();
        Assert.That(defaults.EnumerateArray().Select(row => row.GetProperty("Path").GetString()), Is.EqualTo(new[]
        {
            @"~\AppData\Roaming\Microsoft\Windows\Start Menu", @"~\Desktop", @"C:\ProgramData\Microsoft\Windows\Start Menu"
        }));
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Assert.That(ApplicationsPlugin.ReadScopes(defaults), Does.Contain(Path.Combine(home, "Desktop")));
        var scopes = ApplicationsPlugin.ReadScopes(JsonSerializer.SerializeToElement(new[]
        {
            new { Path = @"~\Desktop" }, new { Path = Path.Combine(home, "Desktop") + "\\" },
            new { Path = "" }, new { Path = "relative" }
        }));
        Assert.That(scopes, Has.Count.EqualTo(1));
    }

    [Test]
    public async Task ScanRecursesFiltersFilesAndReplacesRemovedScopes()
    {
        var root = Path.Combine(Path.GetTempPath(), "ApplicationsTests-" + Guid.NewGuid().ToString("N"));
        var child = Path.Combine(root, "nested");
        Directory.CreateDirectory(child);
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var plugin = new ApplicationsPlugin(NullLogger<ApplicationsPlugin>.Instance, cache, Mock.Of<ILocalizationService>());
        try
        {
            var exe = Path.Combine(child, "Example App.exe");
            await File.WriteAllTextAsync(exe, "");
            await File.WriteAllTextAsync(Path.Combine(root, "Example App.appref-ms"), "");
            await File.WriteAllTextAsync(Path.Combine(root, "Example App.txt"), "");
            await File.WriteAllTextAsync(Path.Combine(root, "Example App.ps1"), "");
            await plugin.RescanAsync(JsonSerializer.SerializeToElement(new[] { new { Path = root }, new { Path = child } }));
            var result = await plugin.SearchAsync("EXAMPLE app", CancellationToken.None);
            Assert.That(result.Items.Count(), Is.EqualTo(2));
            Assert.That(result.Items.Select(item => item.ResultKey).Distinct().Count(), Is.EqualTo(2));
            Assert.That(result.Items.All(item => item.AllowedActions.First().Hotkey == Hotkey.Enter), Is.True);
            File.Delete(exe);
            Assert.That((await plugin.SearchAsync("example", CancellationToken.None)).Items.Count(), Is.EqualTo(1));
            await plugin.RescanAsync(JsonSerializer.SerializeToElement(Array.Empty<object>()));
            Assert.That((await plugin.SearchAsync("example", CancellationToken.None)).Items, Is.Empty);
        }
        finally
        {
            // This is the exact unique temporary directory created by this test.
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task MissingScopesAndCancelledSearchAreHandled()
    {
        using var cache = new MemoryCache(new MemoryCacheOptions());
        using var plugin = new ApplicationsPlugin(NullLogger<ApplicationsPlugin>.Instance, cache, Mock.Of<ILocalizationService>());
        await plugin.RescanAsync(JsonSerializer.SerializeToElement(new[] { new { Path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")) } }));
        Assert.That((await plugin.SearchAsync("", CancellationToken.None)).Items, Is.Empty);
        Assert.ThrowsAsync<OperationCanceledException>(async () => await plugin.SearchAsync("app", new CancellationToken(true)));
    }
}
