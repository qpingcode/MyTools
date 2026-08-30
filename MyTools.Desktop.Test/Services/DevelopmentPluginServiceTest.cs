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
    public void CreateNpmStartInfo_InheritsProcessEnvironmentAndPrependsNpmDirectoryToPath()
    {
        const string key = "MYTOOLS_NPM_ENVIRONMENT_TEST";
        var previous = Environment.GetEnvironmentVariable(key);
        try
        {
            Environment.SetEnvironmentVariable(key, "available");

            var npmDirectory = Path.Combine(TestContext.CurrentContext.WorkDirectory, "bundled node runtime");
            var info = DevelopmentPluginService.CreateNpmStartInfo(
                Path.Combine(npmDirectory, "npm.cmd"),
                TestContext.CurrentContext.WorkDirectory,
                "install",
                false);
            var pathDirectories = info.Environment["PATH"]!.Split(Path.PathSeparator);

            Assert.Multiple(() =>
            {
                Assert.That(info.Environment[key], Is.EqualTo("available"));
                Assert.That(pathDirectories[0], Is.EqualTo(npmDirectory).IgnoreCase);
                Assert.That(
                    string.Join(Path.PathSeparator, pathDirectories.Skip(1)),
                    Is.EqualTo(DevelopmentPluginService.BuildCurrentPath()));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable(key, previous);
        }
    }

    [Test]
    public void CreateNpmStartInfo_OverridesStaleProcessProxyFromCurrentSettings()
    {
        const string staleProxy = "http://127.0.0.1:7890";
        var configuredProxy = new Uri("http://127.0.0.1:8890");
        var previous = Environment.GetEnvironmentVariable("HTTP_PROXY");
        try
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", staleProxy);

            var info = DevelopmentPluginService.CreateNpmStartInfo(
                "npm.cmd",
                TestContext.CurrentContext.WorkDirectory,
                "install",
                false,
                configuredProxy,
                overrideEnvironmentProxy: true);

            Assert.Multiple(() =>
            {
                Assert.That(info.Environment["HTTP_PROXY"], Is.EqualTo(configuredProxy.AbsoluteUri));
                Assert.That(info.Environment["HTTPS_PROXY"], Is.EqualTo(configuredProxy.AbsoluteUri));
                Assert.That(info.Environment["ALL_PROXY"], Is.EqualTo(configuredProxy.AbsoluteUri));
            });
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTP_PROXY", previous);
        }
    }

    [Test]
    public void CreateNpmStartInfo_RemovesStaleProcessProxyWhenSettingsUseDirectConnection()
    {
        var previous = Environment.GetEnvironmentVariable("HTTPS_PROXY");
        try
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", "http://127.0.0.1:7890");

            var info = DevelopmentPluginService.CreateNpmStartInfo(
                "npm.cmd",
                TestContext.CurrentContext.WorkDirectory,
                "install",
                false,
                proxyUri: null,
                overrideEnvironmentProxy: true);

            Assert.That(info.Environment.ContainsKey("HTTPS_PROXY"), Is.False);
        }
        finally
        {
            Environment.SetEnvironmentVariable("HTTPS_PROXY", previous);
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
    public void WatchMutexName_IsStableAndPluginSpecific()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                DevelopmentPluginService.WatchMutexName("Color-Converter"),
                Is.EqualTo(DevelopmentPluginService.WatchMutexName("color-converter")));
            Assert.That(
                DevelopmentPluginService.WatchMutexName("color-converter"),
                Is.Not.EqualTo(DevelopmentPluginService.WatchMutexName("another-plugin")));
        });
    }

    [Test]
    public void CreateWatchStartInfo_ExitsWhenPluginMutexIsAlreadyHeld()
    {
        var pluginId = "watch-test-" + Guid.NewGuid().ToString("N");
        using var mutex = new Mutex(true, DevelopmentPluginService.WatchMutexName(pluginId));
        using var process = new Process
        {
            StartInfo = DevelopmentPluginService.CreateWatchStartInfo(
                Path.Combine(TestContext.CurrentContext.WorkDirectory, "unused npm.cmd"),
                TestContext.CurrentContext.WorkDirectory,
                pluginId,
                visible: false)
        };

        try
        {
            process.Start();
            Assert.That(process.WaitForExit(10_000), Is.True);
            var error = process.StandardError.ReadToEnd();

            Assert.Multiple(() =>
            {
                Assert.That(process.ExitCode, Is.EqualTo(73));
                Assert.That(error, Does.Contain("already running").IgnoreCase);
            });
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }

    [Test]
    public void CreateWatchStartInfo_TeesWatchOutputToPluginLog()
    {
        var directory = Path.Combine(TestContext.CurrentContext.WorkDirectory, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var npm = Path.Combine(directory, "fake npm.cmd");
        var log = Path.Combine(directory, "watch.log");
        File.WriteAllText(npm, "@echo off\r\necho watch-output\r\n");
        try
        {
            using var process = new Process
            {
                StartInfo = DevelopmentPluginService.CreateWatchStartInfo(
                    npm,
                    directory,
                    "tee-test-" + Guid.NewGuid().ToString("N"),
                    visible: false,
                    logPath: log)
            };

            process.Start();
            Assert.That(process.WaitForExit(10_000), Is.True);

            Assert.Multiple(() =>
            {
                Assert.That(process.ExitCode, Is.Zero);
                Assert.That(File.ReadAllText(log), Does.Contain("watch-output"));
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Test]
    public void SanitizeLogLine_RedactsCommonCredentials()
    {
        var sanitized = DevelopmentPluginService.SanitizeLogLine(
            "Authorization: Bearer abc123 api_key=secret-value password=hunter2");

        Assert.Multiple(() =>
        {
            Assert.That(sanitized, Does.Not.Contain("abc123"));
            Assert.That(sanitized, Does.Not.Contain("secret-value"));
            Assert.That(sanitized, Does.Not.Contain("hunter2"));
            Assert.That(sanitized, Does.Contain("[REDACTED]"));
        });
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

    [Test]
    public void ValidateDevelopmentPackage_AcceptsBuildOnlyWhenWatchIsNotRequired()
    {
        var directory = CreatePackageDirectory("""
            { "scripts": { "build": "node build.mjs" } }
            """, withNodeModules: true);
        try
        {
            Assert.DoesNotThrow(() =>
                DevelopmentPluginService.ValidateDevelopmentPackage(directory, requireDependencies: true, requireWatch: false));
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [TestCase("{ \"scripts\": { \"build\": \"node build.mjs\" } }", true, true, "watch script")]
    [TestCase("{ \"scripts\": { \"watch\": \"node build.mjs --watch\" } }", true, true, "build script")]
    [TestCase("{ \"scripts\": { \"build\": \"node build.mjs\", \"watch\": \"node build.mjs --watch\" } }", false, true, "Dependencies are not installed")]
    public void ValidateDevelopmentPackage_ReportsActionableErrors(
        string packageJson,
        bool withNodeModules,
        bool requireWatch,
        string expectedMessage)
    {
        var directory = CreatePackageDirectory(packageJson, withNodeModules);
        try
        {
            var exception = Assert.Throws<InvalidOperationException>(() =>
                DevelopmentPluginService.ValidateDevelopmentPackage(directory, true, requireWatch));
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
