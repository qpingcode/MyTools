using MyTools.AI;
using NUnit.Framework;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
[NonParallelizable]
public sealed class PluginCreationAgentServiceTest
{
    private string? originalApiKey;

    [SetUp]
    public void SetUp()
    {
        originalApiKey = Environment.GetEnvironmentVariable(PluginCreationAgentService.ApiKeyEnvironmentVariable);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable(PluginCreationAgentService.ApiKeyEnvironmentVariable, originalApiKey);
    }

    [Test]
    public void AvailabilityRequiresDeepSeekApiKey()
    {
        Environment.SetEnvironmentVariable(PluginCreationAgentService.ApiKeyEnvironmentVariable, null);
        using var service = CreateService();

        var availability = service.GetAvailability();

        Assert.Multiple(() =>
        {
            Assert.That(availability.Available, Is.False);
            Assert.That(availability.Provider, Is.EqualTo("DeepSeek"));
            Assert.That(availability.RequiredEnvironmentVariable,
                Is.EqualTo(PluginCreationAgentService.ApiKeyEnvironmentVariable));
            Assert.That(availability.UnavailableReason, Does.Contain(PluginCreationAgentService.ApiKeyEnvironmentVariable));
        });
    }

    [Test]
    public void AvailabilityUsesDeepSeekWhenApiKeyExists()
    {
        Environment.SetEnvironmentVariable(PluginCreationAgentService.ApiKeyEnvironmentVariable, "test-key");
        using var service = CreateService();

        var availability = service.GetAvailability();

        Assert.That(availability.Available, Is.True);
        Assert.That(availability.Provider, Is.EqualTo("DeepSeek"));
    }

    [Test]
    public void ConstructorAcceptsDuplicatePluginIdsCaseInsensitively()
    {
        var root = TestContext.CurrentContext.WorkDirectory;
        var context = new PluginCreationContext(
            root,
            root,
            root,
            root,
            root,
            Path.Combine(root, "SKILL.md"),
            [
                new ExistingPlugin("deepseek-translator", "Translator"),
                new ExistingPlugin("DEEPSEEK-TRANSLATOR", "Anki Cards")
            ]);

        Assert.DoesNotThrow(() =>
        {
            using var service = new PluginCreationAgentService(context);
        });
    }

    [Test]
    public void BundledSkillReferencesAreCompleteAndContainSourceOnly()
    {
        var skillRoot = Path.Combine(AppContext.BaseDirectory, "skills", "create-plugin");
        var skillPath = Path.Combine(skillRoot, "SKILL.md");
        var referenceRoot = Path.Combine(skillRoot, "references", "Examples");
        Assert.That(File.Exists(skillPath), Is.True, skillPath);
        Assert.That(Directory.Exists(referenceRoot), Is.True, referenceRoot);

        var markdown = File.ReadAllText(skillPath);
        var links = Regex.Matches(markdown, @"\]\((references/[^)]+)\)")
            .Select(match => match.Groups[1].Value)
            .Distinct()
            .ToArray();
        var missing = links.Where(link =>
        {
            var target = Path.GetFullPath(Path.Combine(skillRoot, link.Replace('/', Path.DirectorySeparatorChar)));
            return !File.Exists(target) && !Directory.Exists(target);
        }).ToArray();
        var files = Directory.EnumerateFiles(referenceRoot, "*", SearchOption.AllDirectories).ToArray();
        var pluginSdkVersions = files
            .Where(path => Path.GetFileName(path).Equals("package.json", StringComparison.OrdinalIgnoreCase))
            .Select(path =>
            {
                using var document = JsonDocument.Parse(File.ReadAllText(path));
                return document.RootElement.TryGetProperty("dependencies", out var dependencies)
                    && dependencies.TryGetProperty("@qping/plugin-bus", out var version)
                        ? version.GetString()
                        : null;
            })
            .Where(version => version is not null)
            .ToArray();
        var packageTemplate = File.ReadAllText(Path.Combine(
            referenceRoot,
            "create-plugin",
            "src",
            "templates",
            "common",
            "package.json.mustache"));

        Assert.Multiple(() =>
        {
            Assert.That(links, Is.Not.Empty);
            Assert.That(missing, Is.Empty);
            Assert.That(files, Has.Length.GreaterThan(100));
            Assert.That(files.Any(path => path.Contains($"{Path.DirectorySeparatorChar}node_modules{Path.DirectorySeparatorChar}")), Is.False);
            Assert.That(files.Any(path => path.Contains($"{Path.DirectorySeparatorChar}dist{Path.DirectorySeparatorChar}")), Is.False);
            Assert.That(files.Any(path => path.Contains($"{Path.DirectorySeparatorChar}sdk-v3{Path.DirectorySeparatorChar}")), Is.False);
            Assert.That(files.Any(path => Path.GetFileName(path).Equals("package-lock.json", StringComparison.OrdinalIgnoreCase)), Is.False);
            Assert.That(pluginSdkVersions, Is.Not.Empty);
            Assert.That(pluginSdkVersions, Has.All.EqualTo("0.7.0"));
            Assert.That(packageTemplate, Does.Contain("\"@qping/plugin-bus\": \"0.7.0\""));
        });
    }

    private static PluginCreationAgentService CreateService()
    {
        var root = TestContext.CurrentContext.WorkDirectory;
        return new PluginCreationAgentService(new PluginCreationContext(
            root,
            root,
            root,
            root,
            root,
            Path.Combine(root, "SKILL.md"),
            []));
    }
}
