using MyTools.Desktop.Services;
using NUnit.Framework;
using System.Diagnostics;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
[NonParallelizable]
public class DevelopmentPluginServiceTest
{
    private static readonly (string Id, string Name)[] ExistingPlugins =
    [
        ("settings", "Settings"),
        ("clipboard", "Clipboard History")
    ];

    [TestCase("SETTINGS", "Another name", "id")]
    [TestCase("new-plugin", "  clipboard history  ", "name")]
    public void ValidateAgainstExisting_DetectsCaseInsensitiveConflicts(
        string pluginId,
        string name,
        string expectedConflict)
    {
        var result = DevelopmentPluginService.ValidateAgainstExisting(name, pluginId, ExistingPlugins);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.False);
            Assert.That(result.Conflict, Is.EqualTo(expectedConflict));
        });
    }

    [Test]
    public void ValidateAgainstExisting_AcceptsUniquePlugin()
    {
        var result = DevelopmentPluginService.ValidateAgainstExisting(
            "Quick Actions", "quick-actions", ExistingPlugins);

        Assert.Multiple(() =>
        {
            Assert.That(result.IsValid, Is.True);
            Assert.That(result.Conflict, Is.Null);
        });
    }

    [Test]
    public async Task CreateNpmStartInfo_RunsCmdScriptThroughCommandInterpreter()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var script = Path.Combine(directory, "fake npm.cmd");
        File.WriteAllText(script, "@echo off\r\nnode fake-install.js\r\n");
        File.WriteAllText(Path.Combine(directory, "node.cmd"), "@echo off\r\necho bundled-node-%1\r\n");
        try
        {
            using var process = new Process
            {
                StartInfo = DevelopmentPluginService.CreateNpmStartInfo(
                    script, directory, "install", keepOpen: false)
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();

            Assert.Multiple(() =>
            {
                Assert.That(process.ExitCode, Is.Zero);
                Assert.That(output.Trim(), Is.EqualTo("bundled-node-fake-install.js"));
                Assert.That(process.StartInfo.FileName, Does.EndWith("cmd.exe").IgnoreCase);
                Assert.That(
                    process.StartInfo.Environment["PATH"]!.Split(Path.PathSeparator)[0],
                    Is.EqualTo(directory).IgnoreCase);
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void CreateNpmStartInfo_InheritsProcessEnvironmentAndBuildsCurrentPath()
    {
        const string key = "MYTOOLS_NPM_ENVIRONMENT_TEST";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "available");

            var info = DevelopmentPluginService.CreateNpmStartInfo(
                @"C:\Program Files\nodejs\npm.cmd", TestContext.CurrentContext.WorkDirectory, "install", false);

            Assert.Multiple(() =>
            {
                Assert.That(info.Environment[key], Is.EqualTo("available"));
                Assert.That(info.Environment["PATH"], Is.EqualTo(DevelopmentPluginService.BuildCurrentPath()));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Test]
    public void ResolveSystemNpm_RequiresBothNodeAndNpmOnPath()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var originalPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Process);
        try
        {
            Environment.SetEnvironmentVariable("PATH", directory, EnvironmentVariableTarget.Process);
            File.WriteAllText(Path.Combine(directory, "npm.cmd"), "@echo off\r\n");
            Assert.That(DevelopmentPluginService.ResolveSystemNpm(), Is.Null);

            File.WriteAllText(Path.Combine(directory, "node.exe"), "stub");
            Assert.That(
                DevelopmentPluginService.ResolveSystemNpm(),
                Is.EqualTo(Path.Combine(directory, "npm.cmd")));
        }
        finally
        {
            Environment.SetEnvironmentVariable("PATH", originalPath, EnvironmentVariableTarget.Process);
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void ValidateDevelopmentPackage_AcceptsCompleteInstalledProject()
    {
        var directory = CreatePackageDirectory("""
            { "scripts": { "build": "node build.mjs", "watch": "node build.mjs --watch" } }
            """, withNodeModules: true);
        try
        {
            Assert.DoesNotThrow(() => DevelopmentPluginService.ValidateDevelopmentPackage(directory, true));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestCase("{ \"scripts\": { \"build\": \"node build.mjs\" } }", true, "build and watch")]
    [TestCase("{ \"scripts\": { \"build\": \"node build.mjs\", \"watch\": \"node build.mjs --watch\" } }", false, "Dependencies are not installed")]
    public void ValidateDevelopmentPackage_ReportsActionableErrors(
        string packageJson,
        bool withNodeModules,
        string expectedMessage)
    {
        var directory = CreatePackageDirectory(packageJson, withNodeModules);
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                DevelopmentPluginService.ValidateDevelopmentPackage(directory, true));
            Assert.That(exception!.Message, Does.Contain(expectedMessage));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static string CreatePackageDirectory(string packageJson, bool withNodeModules)
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "package.json"), packageJson);
        if (withNodeModules) Directory.CreateDirectory(Path.Combine(directory, "node_modules"));
        return directory;
    }
}
