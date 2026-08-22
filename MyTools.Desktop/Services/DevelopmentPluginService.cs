using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class DevelopmentPluginService : IDisposable
{
    private const string RefreshPipeName = "MyTools.DevelopmentPlugins.Refresh";
    private static readonly Regex ValidPluginId = new("^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$", RegexOptions.Compiled);
    private readonly ILogger<DevelopmentPluginService> logger;
    private readonly Dictionary<string, CancellationTokenSource> reloadDebounces = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource refreshListenerCancellation = new();
    private readonly object sync = new();
    private Task? refreshListenerTask;
    private bool disposed;

    public DevelopmentPluginService(ILogger<DevelopmentPluginService> logger)
    {
        this.logger = logger;
    }

    public event EventHandler<DevelopmentPluginReloadRequestedEventArgs>? ReloadRequested;

    public string CodingRoot => Path.Combine(ConfigPath.Base, "coding");

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
        var registration = new DevelopmentPluginRegistration(
            pluginId, request.Name.Trim(), request.Author.Trim(), request.PluginType,
            sourcePath, distPath);
        var registrations = DevelopmentPluginRegistrationStore.Load()
            .Where(item => !string.Equals(item.PluginId, pluginId, StringComparison.OrdinalIgnoreCase))
            .Append(registration)
            .ToList();
        DevelopmentPluginRegistrationStore.Save(registrations);

        var job = new DevelopmentPluginJob(Guid.NewGuid().ToString("N"), pluginId, sourcePath);
        job.Complete(distPath);
        return job;
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
        return existing;
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
        lock (sync)
        {
            foreach (var cts in reloadDebounces.Values) { cts.Cancel(); cts.Dispose(); }
            reloadDebounces.Clear();
        }
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
    public string Author { get; init; } = "";
    public string PluginType { get; init; } = "standard";
    public Dictionary<string, string> Files { get; init; } = [];
}

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
