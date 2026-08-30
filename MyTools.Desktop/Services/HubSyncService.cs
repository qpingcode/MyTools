using System.IO;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Plugins;
using MyTools.Desktop.Storage;

namespace MyTools.Desktop.Services;

public sealed class HubSyncService : IDisposable
{
    private static readonly string[] RootFiles = ["Settings.json", "Gestures.json", "PluginOverrides.json"];
    private readonly HubApiClient client;
    private readonly IConfigurationStorage storage;
    private readonly IConfigurationRegistry registry;
    private readonly PluginOverrideProvider pluginOverrideProvider;
    private readonly GestureConfigProvider gestureConfigProvider;
    private readonly ILogger<HubSyncService> logger;
    private readonly FileSystemWatcher rootWatcher;
    private readonly FileSystemWatcher? pluginsWatcher;
    private readonly object gate = new();
    private CancellationTokenSource? debounce;
    private bool applying;
    private bool disposed;

    public HubSyncService(
        HubApiClient client,
        IConfigurationStorage storage,
        IConfigurationRegistry registry,
        PluginOverrideProvider pluginOverrideProvider,
        GestureConfigProvider gestureConfigProvider,
        ILogger<HubSyncService> logger)
    {
        this.client = client;
        this.storage = storage;
        this.registry = registry;
        this.pluginOverrideProvider = pluginOverrideProvider;
        this.gestureConfigProvider = gestureConfigProvider;
        this.logger = logger;
        Directory.CreateDirectory(ConfigPath.Base);
        Directory.CreateDirectory(ConfigPath.PluginsDataPath);
        rootWatcher = CreateWatcher(ConfigPath.Base, false);
        pluginsWatcher = Directory.Exists(ConfigPath.PluginsDataPath)
            ? CreateWatcher(ConfigPath.PluginsDataPath, true)
            : null;
    }

    public async Task<object> PullAsync(CancellationToken cancellationToken)
    {
        var remote = await client.GetAsync<HubSyncPayload?>("/api/sync", authenticate: true, cancellationToken);
        if (remote?.Files is null || remote.Files.Count == 0)
        {
            return await PushAsync(cancellationToken);
        }

        Apply(remote.Files);
        return new { success = true, updatedAt = remote.UpdatedAt, pulled = true };
    }

    public async Task<object> PushAsync(CancellationToken cancellationToken)
    {
        var payload = new HubSyncPayload(DateTimeOffset.UtcNow, Collect());
        var stored = await client.SendJsonAsync<HubSyncPayload>(HttpMethod.Put, "/api/sync", payload, authenticate: true, cancellationToken);
        return new { success = true, updatedAt = stored.UpdatedAt, pulled = false };
    }

    public void SchedulePush()
    {
        if (!client.IsSignedIn || applying)
        {
            return;
        }

        lock (gate)
        {
            debounce?.Cancel();
            debounce?.Dispose();
            debounce = new CancellationTokenSource();
            var token = debounce.Token;
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(1500, token);
                    await PushAsync(token);
                }
                catch (OperationCanceledException)
                {
                    /* replaced */
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Failed to push MyTools Hub settings.");
                }
            }, token);
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        debounce?.Cancel();
        debounce?.Dispose();
        rootWatcher.Dispose();
        pluginsWatcher?.Dispose();
    }

    private void Apply(Dictionary<string, string> files)
    {
        applying = true;
        try
        {
            foreach (var (relative, content) in files)
            {
                var full = ResolveLocalPath(relative);
                if (full == null) continue;
                Directory.CreateDirectory(Path.GetDirectoryName(full)!);
                File.WriteAllText(full, content ?? "");
            }

            if (storage is CompositeConfigurationStorage composite)
            {
                composite.ReloadFromDisk();
            }
            else
            {
                storage.Initialize();
            }

            registry.Reload();
            pluginOverrideProvider.Reload();
            gestureConfigProvider.Reload();
        }
        finally
        {
            applying = false;
        }
    }

    private static Dictionary<string, string> Collect()
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in RootFiles)
        {
            var path = Path.Combine(ConfigPath.Base, name);
            if (File.Exists(path))
            {
                files[name] = File.ReadAllText(path);
            }
        }

        if (Directory.Exists(ConfigPath.PluginsDataPath))
        {
            foreach (var directory in Directory.EnumerateDirectories(ConfigPath.PluginsDataPath))
            {
                var settings = Path.Combine(directory, ConfigPath.PluginSettingsFileName);
                if (!File.Exists(settings)) continue;
                var pluginId = Path.GetFileName(directory);
                files[$"pluginsData/{pluginId}/{ConfigPath.PluginSettingsFileName}"] = File.ReadAllText(settings);
            }
        }

        return files;
    }

    private static string? ResolveLocalPath(string relative)
    {
        var normalized = relative.Replace('\\', '/').TrimStart('/');
        if (RootFiles.Contains(normalized, StringComparer.OrdinalIgnoreCase))
        {
            return Path.Combine(ConfigPath.Base, Path.GetFileName(normalized));
        }

        var parts = normalized.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 3
            && parts[0].Equals("pluginsData", StringComparison.OrdinalIgnoreCase)
            && parts[2].Equals(ConfigPath.PluginSettingsFileName, StringComparison.OrdinalIgnoreCase)
            && parts[1].IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
        {
            return ConfigPath.PluginSettingsPath(parts[1]);
        }

        return null;
    }

    private FileSystemWatcher CreateWatcher(string path, bool recursive)
    {
        var watcher = new FileSystemWatcher(path)
        {
            IncludeSubdirectories = recursive,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Changed += OnChanged;
        watcher.Created += OnChanged;
        watcher.Renamed += OnChanged;
        return watcher;
    }

    private void OnChanged(object sender, FileSystemEventArgs e)
    {
        var name = Path.GetFileName(e.Name ?? e.FullPath);
        if (name.Equals("HubSession.json", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!RootFiles.Contains(name, StringComparer.OrdinalIgnoreCase)
            && !name.Equals(ConfigPath.PluginSettingsFileName, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        SchedulePush();
    }
}

public sealed record HubSyncPayload(DateTimeOffset UpdatedAt, Dictionary<string, string> Files);
