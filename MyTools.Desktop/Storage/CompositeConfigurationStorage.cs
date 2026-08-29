using System.IO;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Plugins;

namespace MyTools.Desktop.Storage;

/// <summary>
/// Routes host settings to <c>Settings.json</c> and plugin-owned settings to
/// <c>pluginsData/{pluginId}/settings.json</c> using the explicit <see cref="PluginId"/>
/// on each call — not by parsing name prefixes.
/// </summary>
public sealed class CompositeConfigurationStorage : IConfigurationStorage, IDisposable
{
    private readonly IConfigurationStorage hostStorage;
    private readonly string pluginsDataRoot;
    private readonly Dictionary<PluginId, JsonConfigurationStorage> pluginStorages = new();
    private readonly object lockObject = new();
    private bool disposed;

    public CompositeConfigurationStorage()
        : this(new JsonConfigurationStorage(Path.Combine(ConfigPath.Base, "Settings.json")), ConfigPath.PluginsDataPath)
    {
    }

    public CompositeConfigurationStorage(JsonConfigurationStorage hostStorage, string pluginsDataRoot)
    {
        ArgumentNullException.ThrowIfNull(hostStorage);
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginsDataRoot);
        this.hostStorage = hostStorage;
        this.pluginsDataRoot = pluginsDataRoot;
        Initialize();
    }

    public void Initialize()
    {
        hostStorage.Initialize();
    }

    public void Store(string name, string value, PluginId? pluginId = null)
    {
        if (pluginId is not null)
        {
            GetOrCreatePluginStorage(pluginId).Store(name, value);
            DeleteLegacyHostKey(pluginId, name);
            return;
        }

        hostStorage.Store(name, value);
    }

    public string? Retrieve(string name, PluginId? pluginId = null)
    {
        if (pluginId is null)
        {
            return hostStorage.Retrieve(name);
        }

        var pluginStorage = TryGetPluginStorage(pluginId);
        var fromPlugin = pluginStorage?.Retrieve(name);
        if (fromPlugin != null)
        {
            DeleteLegacyHostKey(pluginId, name);
            return fromPlugin;
        }

        return MigrateLegacyHostValue(pluginId, name);
    }

    public bool Exists(string name, PluginId? pluginId = null)
    {
        if (pluginId is null)
        {
            return hostStorage.Exists(name);
        }

        var pluginStorage = TryGetPluginStorage(pluginId);
        if (pluginStorage != null && pluginStorage.Exists(name))
        {
            return true;
        }

        return hostStorage.Exists(LegacyHostKey(pluginId, name));
    }

    public void Delete(string name, PluginId? pluginId = null)
    {
        if (pluginId is null)
        {
            hostStorage.Delete(name);
            return;
        }

        TryGetPluginStorage(pluginId)?.Delete(name);
        DeleteLegacyHostKey(pluginId, name);
    }

    public void Clear()
    {
        hostStorage.Clear();
        lock (lockObject)
        {
            ScanPluginSettingsFiles();
            foreach (var storage in pluginStorages.Values)
            {
                storage.Clear();
                storage.Dispose();
            }

            pluginStorages.Clear();
        }
    }

    public IEnumerable<string> GetAllNames(PluginId? pluginId = null)
    {
        if (pluginId is not null)
        {
            var pluginStorage = TryGetPluginStorage(pluginId);
            return pluginStorage?.GetAllNames() ?? [];
        }

        return hostStorage.GetAllNames();
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        try
        {
            lock (lockObject)
            {
                foreach (var storage in pluginStorages.Values)
                {
                    storage.Dispose();
                }

                pluginStorages.Clear();
            }

            hostStorage.Dispose();
        }
        finally
        {
            disposed = true;
        }
    }

    private string? MigrateLegacyHostValue(PluginId pluginId, string name)
    {
        var legacyName = LegacyHostKey(pluginId, name);
        var fromHost = hostStorage.Retrieve(legacyName);
        if (fromHost is null)
        {
            return null;
        }

        GetOrCreatePluginStorage(pluginId).Store(name, fromHost);
        hostStorage.Delete(legacyName);
        return fromHost;
    }

    private void DeleteLegacyHostKey(PluginId pluginId, string name)
    {
        var legacyName = LegacyHostKey(pluginId, name);
        if (hostStorage.Exists(legacyName))
        {
            hostStorage.Delete(legacyName);
        }
    }

    private static string LegacyHostKey(PluginId pluginId, string name) => pluginId.Value + "." + name;

    private JsonConfigurationStorage GetOrCreatePluginStorage(PluginId pluginId)
    {
        lock (lockObject)
        {
            if (pluginStorages.TryGetValue(pluginId, out var existing))
            {
                return existing;
            }

            var storage = new JsonConfigurationStorage(
                ConfigPath.PluginSettingsPath(pluginsDataRoot, pluginId.Value));
            pluginStorages[pluginId] = storage;
            return storage;
        }
    }

    private JsonConfigurationStorage? TryGetPluginStorage(PluginId pluginId)
    {
        lock (lockObject)
        {
            if (pluginStorages.TryGetValue(pluginId, out var existing))
            {
                return existing;
            }

            var path = ConfigPath.PluginSettingsPath(pluginsDataRoot, pluginId.Value);
            if (!File.Exists(path))
            {
                return null;
            }

            var storage = new JsonConfigurationStorage(path);
            pluginStorages[pluginId] = storage;
            return storage;
        }
    }

    private void ScanPluginSettingsFiles()
    {
        if (!Directory.Exists(pluginsDataRoot))
        {
            return;
        }

        foreach (var directory in Directory.EnumerateDirectories(pluginsDataRoot))
        {
            var settingsPath = Path.Combine(directory, ConfigPath.PluginSettingsFileName);
            if (!File.Exists(settingsPath))
            {
                continue;
            }

            var folderName = Path.GetFileName(directory);
            if (string.IsNullOrWhiteSpace(folderName))
            {
                continue;
            }

            var pluginId = new PluginId(folderName);
            if (!pluginStorages.ContainsKey(pluginId))
            {
                pluginStorages[pluginId] = new JsonConfigurationStorage(settingsPath);
            }
        }
    }
}
