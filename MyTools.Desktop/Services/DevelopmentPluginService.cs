using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MyTools.AI;
using MyTools.Common.Config;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class DevelopmentPluginService : IDisposable
{
    private const string RefreshPipeName = "MyTools.DevelopmentPlugins.Refresh";
    private static readonly Regex ValidPluginId = new("^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$", RegexOptions.Compiled);
    private readonly ILogger<DevelopmentPluginService> logger;
    private readonly IReadOnlyList<IPlugin> builtInPlugins;
    private readonly NodePluginCatalog nodePluginCatalog;
    private readonly Dictionary<string, CancellationTokenSource> reloadDebounces = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Process> watchProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource refreshListenerCancellation = new();
    private readonly object sync = new();
    private Task? refreshListenerTask;
    private bool disposed;

    public DevelopmentPluginService(
        ILogger<DevelopmentPluginService> logger,
        IEnumerable<IPlugin> builtInPlugins,
        NodePluginCatalog nodePluginCatalog)
    {
        this.logger = logger;
        this.builtInPlugins = builtInPlugins.ToList();
        this.nodePluginCatalog = nodePluginCatalog;
    }

    public event EventHandler<DevelopmentPluginReloadRequestedEventArgs>? ReloadRequested;

    public string CodingRoot => Path.Combine(ConfigPath.Base, "coding");
    public string PluginsRoot => Path.Combine(ConfigPath.Base, "plugins");

    public void Initialize()
    {
        Directory.CreateDirectory(CodingRoot);
        DevelopmentPluginSession.Clear();
        refreshListenerTask = ListenForRefreshRequestsAsync(refreshListenerCancellation.Token);
    }

    public DevelopmentPluginJob Create(CreateDevelopmentPluginRequest request)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var pluginId = request.PluginId.Trim().ToLowerInvariant();
        if (!ValidPluginId.IsMatch(pluginId))
        {
            throw new InvalidOperationException("Plugin ID must use lowercase letters, numbers, dots or hyphens (maximum 64 characters).");
        }
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new InvalidOperationException("Plugin name is required.");
        }
        var validation = Validate(request.Name, pluginId);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Conflict == "id"
                ? "A plugin with this ID already exists."
                : "A plugin with this name already exists.");
        }
        if (request.Files.Count == 0)
        {
            throw new InvalidOperationException("The plugin template contains no files.");
        }

        var sourcePath = Path.GetFullPath(Path.Combine(CodingRoot, pluginId));
        var codingRoot = Path.GetFullPath(CodingRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!sourcePath.StartsWith(codingRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The plugin source directory is outside the coding root.");
        }

        var reuseFailedScaffold = Directory.Exists(sourcePath)
            && !DevelopmentPluginRegistrationStore.Load().Any(item =>
                string.Equals(item.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            && TemplateFilesMatch(sourcePath, request.Files);
        if (Directory.Exists(sourcePath) && !reuseFailedScaffold)
        {
            throw new InvalidOperationException($"The plugin source directory already exists: {sourcePath}");
        }

        if (!reuseFailedScaffold)
        {
            Directory.CreateDirectory(sourcePath);
            try
            {
                foreach (var (relativePath, content) in request.Files)
                {
                    var target = ResolveTemplatePath(sourcePath, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    File.WriteAllText(target, content);
                }
            }
            catch
            {
                Directory.Delete(sourcePath, true);
                throw;
            }
        }

        var distPath = Path.Combine(sourcePath, "dist");
        var registration = EnrichRegistration(new DevelopmentPluginRegistration(
            pluginId, request.Name.Trim(), request.PluginType,
            sourcePath, distPath));
        var registrations = DevelopmentPluginRegistrationStore.Load()
            .Where(item => !string.Equals(item.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            .Append(registration)
            .ToList();
        DevelopmentPluginRegistrationStore.Save(registrations);

        var job = new DevelopmentPluginJob(Guid.NewGuid().ToString("N"), pluginId, sourcePath);
        job.Complete(distPath);
        return job;
    }

    public DevelopmentPluginValidationResult Validate(string name, string pluginId)
    {
        var registrations = DevelopmentPluginRegistrationStore.Load();
        var existingPlugins = builtInPlugins.Select(plugin => (plugin.PluginId, plugin.Name))
            .Concat(nodePluginCatalog.Plugins.Select(plugin => (plugin.ParentId, plugin.Name)))
            .Concat(registrations.Select(plugin => (plugin.PluginId, plugin.Name)));
        return ValidateAgainstExisting(name, pluginId, existingPlugins);
    }

    public IReadOnlyList<(string Id, string Name)> GetKnownPlugins()
    {
        return builtInPlugins.Select(plugin => (plugin.PluginId, plugin.Name))
            .Concat(nodePluginCatalog.Plugins.Select(plugin => (plugin.ParentId, plugin.Name)))
            .Concat(DevelopmentPluginRegistrationStore.Load().Select(plugin => (plugin.PluginId, plugin.Name)))
            .Distinct()
            .ToArray();
    }

    public DevelopmentPluginRegistration RegisterAiPlugin(CreatedPluginArtifact artifact)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var registered = DevelopmentPluginRegistrationStore.Load();
        var current = registered.FirstOrDefault(item =>
            string.Equals(item.PluginId, artifact.PluginId, StringComparison.OrdinalIgnoreCase));
        if (artifact.IsUpdate && current is null)
            throw new InvalidOperationException("The selected development plugin is no longer registered.");
        if (!artifact.IsUpdate && current is not null)
            throw new InvalidOperationException("A plugin with this ID already exists.");
        var validation = artifact.IsUpdate
            ? ValidateAgainstExisting(
                artifact.Name,
                artifact.PluginId,
                GetKnownPlugins().Where(plugin =>
                    !string.Equals(plugin.Id, artifact.PluginId, StringComparison.OrdinalIgnoreCase)))
            : Validate(artifact.Name, artifact.PluginId);
        if (!validation.IsValid)
            throw new InvalidOperationException(validation.Conflict == "id"
                ? "A plugin with this ID already exists."
                : "A plugin with this name already exists.");

        var expectedPath = Path.GetFullPath(Path.Combine(CodingRoot, artifact.PluginId));
        if (!string.Equals(expectedPath, Path.GetFullPath(artifact.SourcePath), StringComparison.OrdinalIgnoreCase)
            || !File.Exists(Path.Combine(expectedPath, "plugin.json")))
        {
            throw new InvalidOperationException("The AI plugin source is outside the coding root or has no plugin.json.");
        }

        var registration = new DevelopmentPluginRegistration(
            artifact.PluginId,
            artifact.Name,
            artifact.PluginType,
            expectedPath,
            Path.Combine(expectedPath, "dist"))
        {
            Aliases = artifact.Aliases,
            HotKeys = artifact.HotKeys,
            TestSteps = []
        };
        DevelopmentPluginRegistrationStore.Save(registered
            .Where(item => !string.Equals(item.PluginId, artifact.PluginId, StringComparison.OrdinalIgnoreCase))
            .Append(registration));
        return registration;
    }

    public DevelopmentPluginRegistration GetAiEditableRegistration(string pluginId)
    {
        var registration = GetRegistrations().FirstOrDefault(item =>
            string.Equals(item.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The selected development plugin does not exist.");
        var expectedPath = Path.GetFullPath(Path.Combine(CodingRoot, registration.PluginId));
        if (!string.Equals(expectedPath, Path.GetFullPath(registration.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only plugins in the MyTools coding directory can be edited by AI.");
        return registration;
    }

    public void Delete(string pluginId)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        var normalizedId = pluginId.Trim().ToLowerInvariant();
        if (!ValidPluginId.IsMatch(normalizedId)) throw new InvalidOperationException("Invalid plugin ID.");
        var registrations = DevelopmentPluginRegistrationStore.Load();
        var registration = registrations.FirstOrDefault(item =>
            string.Equals(item.PluginId, normalizedId, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The development plugin does not exist.");
        var expectedPath = Path.GetFullPath(Path.Combine(CodingRoot, normalizedId));
        if (!string.Equals(expectedPath, Path.GetFullPath(registration.SourcePath), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Only plugin folders directly under the MyTools coding directory can be deleted.");

        StopWatchProcess(normalizedId);
        if (Directory.Exists(expectedPath)) Directory.Delete(expectedPath, true);
        DevelopmentPluginRegistrationStore.Save(registrations.Where(item =>
            !string.Equals(item.PluginId, normalizedId, StringComparison.OrdinalIgnoreCase)));
        DevelopmentPluginSession.Deactivate(normalizedId);
        lock (sync)
        {
            if (reloadDebounces.Remove(normalizedId, out var pending))
            {
                pending.Cancel();
                pending.Dispose();
            }
        }
        ReloadRequested?.Invoke(this, new DevelopmentPluginReloadRequestedEventArgs(normalizedId));
    }

    public async Task<PluginSetupResult> InstallAndStartWatchAsync(
        CreatedPluginArtifact artifact,
        Action<string, string?> reportProgress,
        CancellationToken cancellationToken)
    {
        EnsureRegisteredSourcePath(artifact.SourcePath);
        var systemNpm = ResolveSystemNpm();
        var bundledNpm = NodeRuntimeLocator.FindBundledNpm();
        var npmCommand = systemNpm ?? bundledNpm;
        if (npmCommand is null)
        {
            const string missingNpm = "No complete system Node/npm installation or MyTools bundled development runtime was found. Repair or reinstall MyTools, then retry.";
            reportProgress("setupFailed", missingNpm);
            return new PluginSetupResult(false, false, missingNpm);
        }
        reportProgress("installingDependencies", systemNpm is not null
            ? "npm install · system Node/npm"
            : "npm install · MyTools bundled Node/npm");
        var recentOutput = new Queue<string>();
        using var process = new Process
        {
            StartInfo = CreateNpmStartInfo(npmCommand, artifact.SourcePath, "install", keepOpen: false),
            EnableRaisingEvents = true
        };

        void CaptureOutput(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            var trimmed = line.Trim();
            lock (recentOutput)
            {
                recentOutput.Enqueue(trimmed);
                while (recentOutput.Count > 20) recentOutput.Dequeue();
            }
            reportProgress("installOutput", trimmed);
        }

        process.OutputDataReceived += (_, args) => CaptureOutput(args.Data);
        process.ErrorDataReceived += (_, args) => CaptureOutput(args.Data);
        try
        {
            if (!process.Start()) throw new InvalidOperationException("Unable to start npm install.");
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await process.WaitForExitAsync(linked.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                TryKillProcess(process);
                const string timeoutMessage = "npm install timed out after 5 minutes. Check the network or npm configuration, then try again.";
                reportProgress("setupFailed", timeoutMessage);
                return new PluginSetupResult(false, false, timeoutMessage);
            }

            if (process.ExitCode != 0)
            {
                string details;
                lock (recentOutput) details = string.Join(Environment.NewLine, recentOutput);
                var error = string.IsNullOrWhiteSpace(details)
                    ? $"npm install failed with exit code {process.ExitCode}. Check the network or npm configuration, then try again."
                    : $"npm install failed with exit code {process.ExitCode}. Resolve the npm or network error and retry.{Environment.NewLine}{details}";
                reportProgress("setupFailed", error);
                return new PluginSetupResult(false, false, error);
            }

            reportProgress("startingWatch", "npm run watch");
            var watchProcess = Process.Start(CreateNpmStartInfo(
                npmCommand, artifact.SourcePath, "run watch", keepOpen: true))
                ?? throw new InvalidOperationException("Unable to open the watch terminal.");
            TrackWatchProcess(artifact.PluginId, watchProcess);
            reportProgress("setupComplete", artifact.PluginId);
            return new PluginSetupResult(true, true, null);
        }
        catch (OperationCanceledException)
        {
            TryKillProcess(process);
            throw;
        }
        catch (Exception ex)
        {
            var error = $"Plugin setup failed: {ex.Message}. Check Node.js, npm, and the network, then try again.";
            reportProgress("setupFailed", error);
            return new PluginSetupResult(false, false, error);
        }
    }

    public async Task<DevelopmentPluginOperationResult> StartDebugAsync(
        string pluginId,
        CancellationToken cancellationToken)
    {
        var registration = GetAiEditableRegistration(pluginId);
        var npmCommand = ResolveNpmOrThrow();
        ValidateDevelopmentPackage(registration.SourcePath, requireDependencies: true);

        var buildError = await RunNpmCommandAsync(
            npmCommand, registration.SourcePath, "run build", TimeSpan.FromMinutes(2), cancellationToken);
        if (buildError is not null)
            throw new InvalidOperationException($"Plugin build failed.{Environment.NewLine}{buildError}");

        // Probe the watch command without a persistent terminal so immediate watch-only
        // failures (for example an invalid esbuild watch option) can be shown in the UI.
        var watchError = await ProbeWatchAsync(npmCommand, registration.SourcePath, cancellationToken);
        if (watchError is not null)
            throw new InvalidOperationException($"npm run watch failed.{Environment.NewLine}{watchError}");

        StopWatchProcess(registration.PluginId);
        var watchProcess = Process.Start(CreateNpmStartInfo(
            npmCommand, registration.SourcePath, "run watch", keepOpen: true))
            ?? throw new InvalidOperationException("Unable to open the watch terminal.");
        TrackWatchProcess(registration.PluginId, watchProcess);
        return new DevelopmentPluginOperationResult(true, registration.SourcePath);
    }

    public async Task<DevelopmentPluginOperationResult> PublishAsync(
        string pluginId,
        CancellationToken cancellationToken)
    {
        var registration = GetAiEditableRegistration(pluginId);
        StopWatchProcess(registration.PluginId);
        var npmCommand = ResolveNpmOrThrow();
        ValidateDevelopmentPackage(registration.SourcePath, requireDependencies: true);

        var buildError = await RunNpmCommandAsync(
            npmCommand, registration.SourcePath, "run build", TimeSpan.FromMinutes(2), cancellationToken);
        if (buildError is not null)
            throw new InvalidOperationException($"Plugin build failed; it was not installed.{Environment.NewLine}{buildError}");

        var distPath = Path.GetFullPath(registration.DistPath);
        var manifestPath = Path.Combine(distPath, "plugin.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("The build completed without producing dist/plugin.json.");
        using (var document = JsonDocument.Parse(File.ReadAllText(manifestPath)))
        {
            var manifestId = document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
            if (!string.Equals(manifestId, registration.PluginId, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The built plugin manifest ID does not match the selected plugin.");
        }

        Directory.CreateDirectory(PluginsRoot);
        var targetPath = ResolveFormalPluginPath(registration.PluginId);
        var stagingPath = Path.Combine(PluginsRoot, $".{registration.PluginId}.install-{Guid.NewGuid():N}");
        var backupPath = Path.Combine(PluginsRoot, $".{registration.PluginId}.backup-{Guid.NewGuid():N}");
        try
        {
            CopyDirectory(distPath, stagingPath);
            if (Directory.Exists(targetPath))
            {
                EnsureMatchingInstalledPlugin(targetPath, registration.PluginId);
                Directory.Move(targetPath, backupPath);
            }
            try
            {
                Directory.Move(stagingPath, targetPath);
            }
            catch
            {
                if (Directory.Exists(backupPath) && !Directory.Exists(targetPath))
                    Directory.Move(backupPath, targetPath);
                throw;
            }
            if (Directory.Exists(backupPath)) Directory.Delete(backupPath, true);
        }
        finally
        {
            if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, true);
        }

        DevelopmentPluginSession.Deactivate(registration.PluginId);
        ReloadRequested?.Invoke(this, new DevelopmentPluginReloadRequestedEventArgs(registration.PluginId));
        return new DevelopmentPluginOperationResult(true, targetPath);
    }

    private static string ResolveNpmOrThrow()
    {
        return ResolveSystemNpm() ?? NodeRuntimeLocator.FindBundledNpm()
            ?? throw new InvalidOperationException(
                "No complete system Node/npm installation or MyTools bundled development runtime was found. Repair or reinstall MyTools, then retry.");
    }

    internal static void ValidateDevelopmentPackage(string sourcePath, bool requireDependencies)
    {
        var packagePath = Path.Combine(sourcePath, "package.json");
        if (!File.Exists(packagePath)) throw new InvalidOperationException("package.json was not found.");
        using var document = JsonDocument.Parse(File.ReadAllText(packagePath));
        if (!document.RootElement.TryGetProperty("scripts", out var scripts)
            || scripts.ValueKind != JsonValueKind.Object
            || !scripts.TryGetProperty("build", out var build)
            || string.IsNullOrWhiteSpace(build.GetString())
            || !scripts.TryGetProperty("watch", out var watch)
            || string.IsNullOrWhiteSpace(watch.GetString()))
        {
            throw new InvalidOperationException("package.json must define both build and watch scripts.");
        }
        if (requireDependencies && !Directory.Exists(Path.Combine(sourcePath, "node_modules")))
            throw new InvalidOperationException("Dependencies are not installed. Run npm install in the plugin directory, then try again.");
    }

    private async Task<string?> ProbeWatchAsync(
        string npmCommand,
        string sourcePath,
        CancellationToken cancellationToken)
    {
        using var process = new Process
        {
            StartInfo = CreateNpmStartInfo(npmCommand, sourcePath, "run watch", keepOpen: false)
        };
        var output = new Queue<string>();
        void Capture(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (output)
            {
                output.Enqueue(line);
                while (output.Count > 20) output.Dequeue();
            }
        }
        process.OutputDataReceived += (_, args) => Capture(args.Data);
        process.ErrorDataReceived += (_, args) => Capture(args.Data);
        if (!process.Start()) return "Unable to start npm.";
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        var waitTask = process.WaitForExitAsync(cancellationToken);
        var delayTask = Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        var completedTask = await Task.WhenAny(waitTask, delayTask);
        if (completedTask == waitTask)
        {
            await waitTask;
            return process.ExitCode == 0
                ? "The watch command exited instead of continuing to watch."
                : FormatOutput(output, process.ExitCode);
        }
        cancellationToken.ThrowIfCancellationRequested();
        TryKillProcess(process);
        return null;
    }

    private static async Task<string?> RunNpmCommandAsync(
        string npmCommand,
        string sourcePath,
        string arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = CreateNpmStartInfo(npmCommand, sourcePath, arguments, false) };
        var output = new Queue<string>();
        void Capture(string? line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (output)
            {
                output.Enqueue(line);
                while (output.Count > 20) output.Dequeue();
            }
        }
        process.OutputDataReceived += (_, args) => Capture(args.Data);
        process.ErrorDataReceived += (_, args) => Capture(args.Data);
        if (!process.Start()) return "Unable to start npm.";
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        using var timeoutCancellation = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCancellation.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcess(process);
            return $"The command timed out after {timeout.TotalMinutes:0} minutes.";
        }
        return process.ExitCode == 0 ? null : FormatOutput(output, process.ExitCode);
    }

    private static string FormatOutput(Queue<string> output, int exitCode)
    {
        string[] tail;
        lock (output) tail = output.ToArray();
        return tail.Length == 0
            ? $"npm exited with code {exitCode}."
            : $"npm exited with code {exitCode}.{Environment.NewLine}{string.Join(Environment.NewLine, tail)}";
    }

    private string ResolveFormalPluginPath(string pluginId)
    {
        var root = Path.GetFullPath(PluginsRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var target = Path.GetFullPath(Path.Combine(PluginsRoot, pluginId));
        if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The formal plugin directory is outside the MyTools plugins root.");
        return target;
    }

    private static void EnsureMatchingInstalledPlugin(string targetPath, string pluginId)
    {
        var manifestPath = Path.Combine(targetPath, "plugin.json");
        if (!File.Exists(manifestPath))
            throw new InvalidOperationException("The target directory already exists but is not a valid installed plugin.");
        using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var installedId = document.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
        if (!string.Equals(installedId, pluginId, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The target directory belongs to a different plugin and will not be replaced.");
    }

    private static void CopyDirectory(string sourcePath, string targetPath)
    {
        Directory.CreateDirectory(targetPath);
        foreach (var file in Directory.EnumerateFiles(sourcePath))
            File.Copy(file, Path.Combine(targetPath, Path.GetFileName(file)), true);
        foreach (var directory in Directory.EnumerateDirectories(sourcePath))
            CopyDirectory(directory, Path.Combine(targetPath, Path.GetFileName(directory)));
    }

    private static void TryKillProcess(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(true);
        }
        catch
        {
            // Best effort cleanup after cancellation or timeout.
        }
    }

    internal static ProcessStartInfo CreateNpmStartInfo(
        string npmCommand,
        string workingDirectory,
        string arguments,
        bool keepOpen)
    {
        var commandInterpreter = Environment.GetEnvironmentVariable("ComSpec");
        if (string.IsNullOrWhiteSpace(commandInterpreter))
            commandInterpreter = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        var info = new ProcessStartInfo
        {
            FileName = commandInterpreter,
            Arguments = $"/d /s /{(keepOpen ? "k" : "c")} \"\"{npmCommand}\" {arguments}\"",
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = !keepOpen,
            RedirectStandardError = !keepOpen,
            CreateNoWindow = !keepOpen
        };
        ApplyCurrentWindowsEnvironment(info);
        return info;
    }

    internal static string? ResolveCommand(string command)
    {
        foreach (var directory in BuildCurrentPath().Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(directory.Trim().Trim('"'), command);
                if (File.Exists(candidate)) return Path.GetFullPath(candidate);
            }
            catch
            {
                // Ignore malformed PATH segments and continue searching the remaining scopes.
            }
        }
        return null;
    }

    internal static string? ResolveSystemNpm()
    {
        var npm = ResolveCommand(NodeRuntimeLocator.NpmPathFallback);
        if (npm is null) return null;
        var node = Path.Combine(Path.GetDirectoryName(npm)!, "node.exe");
        return File.Exists(node) ? npm : null;
    }

    internal static void ApplyCurrentWindowsEnvironment(ProcessStartInfo info)
    {
        foreach (var target in new[] { EnvironmentVariableTarget.Machine, EnvironmentVariableTarget.User })
        {
            foreach (System.Collections.DictionaryEntry item in Environment.GetEnvironmentVariables(target))
            {
                var key = item.Key?.ToString();
                if (!string.IsNullOrWhiteSpace(key) && item.Value is not null)
                    info.Environment[key] = item.Value.ToString()!;
            }
        }
        foreach (System.Collections.DictionaryEntry item in Environment.GetEnvironmentVariables(EnvironmentVariableTarget.Process))
        {
            var key = item.Key?.ToString();
            if (!string.IsNullOrWhiteSpace(key) && item.Value is not null)
                info.Environment[key] = item.Value.ToString()!;
        }
        info.Environment["PATH"] = BuildCurrentPath();
    }

    internal static string BuildCurrentPath()
    {
        var directories = new List<string>();
        foreach (var target in new[]
                 {
                     EnvironmentVariableTarget.Process,
                     EnvironmentVariableTarget.User,
                     EnvironmentVariableTarget.Machine
                 })
        {
            var path = Environment.GetEnvironmentVariable("PATH", target);
            if (string.IsNullOrWhiteSpace(path)) continue;
            directories.AddRange(Environment.ExpandEnvironmentVariables(path)
                .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return string.Join(Path.PathSeparator, directories.Distinct(StringComparer.OrdinalIgnoreCase));
    }

    private void TrackWatchProcess(string pluginId, Process process)
    {
        Process? previous = null;
        lock (sync)
        {
            if (watchProcesses.Remove(pluginId, out var existing)) previous = existing;
            watchProcesses[pluginId] = process;
        }
        if (previous is not null) TryKillProcess(previous);
        process.EnableRaisingEvents = true;
        process.Exited += (_, _) =>
        {
            lock (sync)
            {
                if (watchProcesses.TryGetValue(pluginId, out var tracked) && ReferenceEquals(tracked, process))
                    watchProcesses.Remove(pluginId);
            }
            process.Dispose();
        };
    }

    private void StopWatchProcess(string pluginId)
    {
        Process? process;
        lock (sync)
        {
            watchProcesses.Remove(pluginId, out process);
        }
        if (process is null) return;
        TryKillProcess(process);
        process.Dispose();
    }

    internal static DevelopmentPluginValidationResult ValidateAgainstExisting(
        string name,
        string pluginId,
        IEnumerable<(string Id, string Name)> existingPlugins)
    {
        var normalizedName = name.Trim();
        var normalizedId = pluginId.Trim();
        var existing = existingPlugins.ToList();
        if (existing.Any(plugin =>
                string.Equals(plugin.Id, normalizedId, StringComparison.OrdinalIgnoreCase)))
        {
            return new DevelopmentPluginValidationResult(false, "id");
        }

        return existing.Any(plugin =>
                string.Equals(plugin.Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase))
            ? new DevelopmentPluginValidationResult(false, "name")
            : new DevelopmentPluginValidationResult(true, null);
    }

    public IReadOnlyList<DevelopmentPluginRegistration> GetRegistrations()
    {
        var registrations = DevelopmentPluginRegistrationStore.Load();
        var existing = registrations.Where(item => Directory.Exists(item.SourcePath)).ToList();
        if (existing.Count != registrations.Count)
        {
            DevelopmentPluginRegistrationStore.Save(existing);
            ReloadRequested?.Invoke(this, new DevelopmentPluginReloadRequestedEventArgs(null));
        }
        return existing.Select(EnrichRegistration).ToArray();
    }

    public void RefreshAll() => ReloadRequested?.Invoke(this, new DevelopmentPluginReloadRequestedEventArgs(null));

    public static void OpenFolder(string sourcePath)
    {
        EnsureRegisteredSourcePath(sourcePath);
        Process.Start(new ProcessStartInfo("explorer.exe", sourcePath) { UseShellExecute = true });
    }

    public static void OpenVisualStudioCode(string sourcePath)
    {
        EnsureRegisteredSourcePath(sourcePath);
        Process.Start(new ProcessStartInfo("code", $"\"{sourcePath}\"") { UseShellExecute = true });
    }

    private async Task ListenForRefreshRequestsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    RefreshPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await pipe.WaitForConnectionAsync(cancellationToken);
                using var reader = new StreamReader(pipe);
                var pluginId = await reader.ReadLineAsync(cancellationToken);
                if (!string.IsNullOrWhiteSpace(pluginId)
                    && DevelopmentPluginRegistrationStore.Load().Any(item =>
                        string.Equals(item.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)))
                {
                    DevelopmentPluginSession.Activate(pluginId);
                    DebounceReload(pluginId);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Development plugin refresh listener failed; retrying.");
                try { await Task.Delay(500, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private static bool TemplateFilesMatch(string sourcePath, IReadOnlyDictionary<string, string> files)
    {
        try
        {
            return files.All(item =>
            {
                var path = ResolveTemplatePath(sourcePath, item.Key);
                return File.Exists(path) && string.Equals(File.ReadAllText(path), item.Value, StringComparison.Ordinal);
            });
        }
        catch
        {
            return false;
        }
    }

    private static DevelopmentPluginRegistration EnrichRegistration(DevelopmentPluginRegistration registration)
    {
        try
        {
            var manifestPath = Path.Combine(registration.SourcePath, "plugin.json");
            if (!File.Exists(manifestPath)) return registration;
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            if (!document.RootElement.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array)
                return registration;
            var aliases = entries.EnumerateArray()
                .Where(entry => entry.TryGetProperty("alias", out _))
                .SelectMany(entry => entry.GetProperty("alias").EnumerateArray())
                .Select(item => item.GetString()).OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var hotKeys = entries.EnumerateArray()
                .Where(entry => entry.TryGetProperty("hotKey", out var value) && value.ValueKind == JsonValueKind.String)
                .Select(entry => entry.GetProperty("hotKey").GetString()).OfType<string>()
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            return registration with
            {
                Aliases = aliases,
                HotKeys = hotKeys,
                TestSteps = []
            };
        }
        catch
        {
            return registration;
        }
    }

    private static string ResolveTemplatePath(string sourcePath, string relativePath)
    {
        var target = Path.GetFullPath(Path.Combine(sourcePath, relativePath.Replace('/', Path.DirectorySeparatorChar)));
        var sourceRoot = sourcePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(sourceRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Template path escapes the source directory: {relativePath}");
        }
        return target;
    }

    private void DebounceReload(string pluginId)
    {
        CancellationTokenSource cts;
        lock (sync)
        {
            if (reloadDebounces.Remove(pluginId, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }
            cts = new CancellationTokenSource();
            reloadDebounces[pluginId] = cts;
        }
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(500, cts.Token);
                ReloadRequested?.Invoke(this, new DevelopmentPluginReloadRequestedEventArgs(pluginId));
            }
            catch (OperationCanceledException) { }
        });
    }

    private static void EnsureRegisteredSourcePath(string sourcePath)
    {
        var fullPath = Path.GetFullPath(sourcePath);
        if (!DevelopmentPluginRegistrationStore.Load().Any(item =>
                string.Equals(Path.GetFullPath(item.SourcePath), fullPath, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException("The requested path is not a registered development plugin source directory.");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        refreshListenerCancellation.Cancel();
        Process[] processes;
        lock (sync)
        {
            foreach (var cts in reloadDebounces.Values) { cts.Cancel(); cts.Dispose(); }
            reloadDebounces.Clear();
            processes = watchProcesses.Values.ToArray();
            watchProcesses.Clear();
        }
        foreach (var process in processes) TryKillProcess(process);
        foreach (var process in processes) process.Dispose();
        refreshListenerCancellation.Dispose();
    }
}

public sealed class DevelopmentPluginReloadRequestedEventArgs(string? pluginId) : EventArgs
{
    public string? PluginId { get; } = pluginId;
}

public sealed class CreateDevelopmentPluginRequest
{
    public string Name { get; init; } = "";
    public string PluginId { get; init; } = "";
    public string PluginType { get; init; } = "standard";
    public Dictionary<string, string> Files { get; init; } = [];
}

public sealed record DevelopmentPluginValidationResult(bool IsValid, string? Conflict);

public sealed record DevelopmentPluginOperationResult(bool Success, string Path);

public sealed class DevelopmentPluginJob
{
    private readonly object sync = new();
    public DevelopmentPluginJob(string jobId, string pluginId, string sourcePath)
    {
        JobId = jobId;
        PluginId = pluginId;
        SourcePath = sourcePath;
    }
    public string JobId { get; }
    public string PluginId { get; }
    public string SourcePath { get; }
    public string? DistPath { get; private set; }
    public string State { get; private set; } = "queued";
    public string Message { get; private set; } = "Queued";
    public void SetState(string state, string message) { lock (sync) { State = state; Message = message; } }
    public void Complete(string distPath) { lock (sync) { DistPath = distPath; State = "completed"; Message = "Plugin source template created."; } }
    public void Fail(string message) { lock (sync) { State = "failed"; Message = message; } }
}
