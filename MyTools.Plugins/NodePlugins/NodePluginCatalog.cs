using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginCatalog
{
    private readonly string pluginRoot;
    private readonly ILogger<NodePluginCatalog> logger;

    public NodePluginCatalog(ILogger<NodePluginCatalog> logger)
        : this(Path.Combine(ConfigPath.Base, "plugins"), logger)
    {
    }

    public NodePluginCatalog(string pluginRoot, ILogger<NodePluginCatalog> logger)
    {
        this.pluginRoot = pluginRoot;
        this.logger = logger;
    }

    public IReadOnlyList<NodePluginManifest> Plugins { get; private set; } = [];

    public IReadOnlyList<NodePluginManifest> Reload()
    {
        Directory.CreateDirectory(pluginRoot);

        var manifests = new List<NodePluginManifest>();
        foreach (var pluginDirectory in Directory.GetDirectories(pluginRoot))
        {
            var manifestPath = Path.Combine(pluginDirectory, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                continue;
            }

            manifests.AddRange(ReadManifests(pluginDirectory, manifestPath));
        }

        Plugins = manifests;
        logger.LogInformation("Discovered {Count} node plugin manifests in {PluginRoot}.", Plugins.Count, pluginRoot);
        return Plugins;
    }

    private IReadOnlyList<NodePluginManifest> ReadManifests(string pluginDirectory, string manifestPath)
    {
        try
        {
            var json = File.ReadAllText(manifestPath);
            var fileModel = JsonSerializer.Deserialize<NodePluginManifestFile>(json, JsonOptions);
            if (fileModel == null || !IsValid(fileModel))
            {
                return [];
            }

            var fullPluginDirectory = Path.GetFullPath(pluginDirectory);
            var manifests = new List<NodePluginManifest>();
            foreach (var entryModel in fileModel.Entries!)
            {
                if (!IsValidEntry(entryModel))
                {
                    logger.LogWarning("Skipping node plugin manifest with invalid entry: {ManifestPath}", manifestPath);
                    return [];
                }

                var entryFullPath = ResolveFileUnderPluginRoot(fullPluginDirectory, entryModel.Entry!);
                if (entryFullPath == null)
                {
                    logger.LogWarning("Skipping node plugin manifest with invalid backend entry: {ManifestPath}", manifestPath);
                    return [];
                }

                var detailEntryFullPath = ResolveFileUnderPluginRoot(fullPluginDirectory, entryModel.Detail!.Entry!);
                if (detailEntryFullPath == null)
                {
                    logger.LogWarning("Skipping node plugin manifest with invalid detail entry: {ManifestPath}", manifestPath);
                    return [];
                }

                manifests.Add(new NodePluginManifest
                {
                    Id = $"{fileModel.Id!}:{entryModel.Id!}",
                    ParentId = fileModel.Id!,
                    EntryId = entryModel.Id!,
                    Name = string.IsNullOrWhiteSpace(entryModel.Name) ? $"{fileModel.Name!} {entryModel.Id}" : entryModel.Name!,
                    Version = fileModel.Version!,
                    Runtime = fileModel.Runtime!,
                    Entry = entryModel.Entry!,
                    ProtocolVersion = fileModel.ProtocolVersion!,
                    PluginDirectory = fullPluginDirectory,
                    EntryFullPath = entryFullPath,
                    DetailEntry = entryModel.Detail.Entry,
                    DetailEntryFullPath = detailEntryFullPath,
                    Keywords = entryModel.Keywords ?? [],
                    HotKey = entryModel.HotKey
                });
            }

            return manifests;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to read node plugin manifest: {ManifestPath}", manifestPath);
            return [];
        }
    }

    private static string? ResolveFileUnderPluginRoot(string pluginDirectory, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(pluginDirectory, relativePath));
        if (!fullPath.StartsWith(pluginDirectory, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return fullPath;
    }

    private static bool IsValid(NodePluginManifestFile fileModel)
    {
        return !string.IsNullOrWhiteSpace(fileModel.Id)
            && !string.IsNullOrWhiteSpace(fileModel.Name)
            && !string.IsNullOrWhiteSpace(fileModel.Version)
            && string.Equals(fileModel.Runtime, "node", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(fileModel.ProtocolVersion)
            && fileModel.Entries is { Count: > 0 };
    }

    private static bool IsValidEntry(EntryManifestFile entryModel)
    {
        return !string.IsNullOrWhiteSpace(entryModel.Id)
            && !string.IsNullOrWhiteSpace(entryModel.Entry)
            && entryModel.Detail != null
            && string.Equals(entryModel.Detail.Type, "web", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(entryModel.Detail.Entry);
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class NodePluginManifestFile
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Version { get; init; }
        public string? Runtime { get; init; }
        public string? ProtocolVersion { get; init; }
        public List<EntryManifestFile>? Entries { get; init; }
    }

    private sealed class EntryManifestFile
    {
        public string? Id { get; init; }
        public string? Name { get; init; }
        public string? Entry { get; init; }
        public List<string>? Keywords { get; init; }
        public string? HotKey { get; init; }
        public DetailManifestFile? Detail { get; init; }
    }

    private sealed class DetailManifestFile
    {
        public string? Type { get; init; }
        public string? Entry { get; init; }
    }
}