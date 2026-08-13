using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.Localization;

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
            var catalogFullPath = fileModel.I18n == null
                ? null
                : ResolveFileUnderPluginRoot(fullPluginDirectory, fileModel.I18n.Catalog!);
            var localesDirectoryFullPath = fileModel.I18n == null
                ? null
                : ResolveDirectoryUnderPluginRoot(fullPluginDirectory, fileModel.I18n.LocalesPath!);
            if (fileModel.I18n != null && (catalogFullPath == null || localesDirectoryFullPath == null))
            {
                logger.LogWarning("Skipping node plugin manifest with invalid i18n paths: {ManifestPath}", manifestPath);
                return [];
            }
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
                    NameMessage = entryModel.Name != null
                        ? new LocalizedMessage(entryModel.Name.Key ?? "", entryModel.Name.DefaultValue ?? "")
                        : null,
                    Version = fileModel.Version!,
                    Runtime = fileModel.Runtime!,
                    Entry = entryModel.Entry!,
                    ProtocolVersion = fileModel.ProtocolVersion!,
                    PluginDirectory = fullPluginDirectory,
                    EntryFullPath = entryFullPath,
                    DetailEntry = entryModel.Detail.Entry,
                    DetailEntryFullPath = detailEntryFullPath,
                    Keywords = entryModel.Keywords ?? [],
                    HotKey = entryModel.HotKey,
                    DefaultLocale = fileModel.I18n?.DefaultLocale ?? "en-US",
                    CatalogFullPath = catalogFullPath,
                    LocalesDirectoryFullPath = localesDirectoryFullPath,
                    SupportedLocales = fileModel.I18n?.SupportedLocales ?? []
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
        var rootWithSeparator = pluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!File.Exists(fullPath))
        {
            return null;
        }

        return fullPath;
    }

    private static string? ResolveDirectoryUnderPluginRoot(string pluginDirectory, string relativePath)
    {
        var fullPath = Path.GetFullPath(Path.Combine(pluginDirectory, relativePath));
        var rootWithSeparator = pluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                                + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            || !Directory.Exists(fullPath))
        {
            return null;
        }

        return fullPath;
    }

    private static bool IsValid(NodePluginManifestFile fileModel)
    {
        return !string.IsNullOrWhiteSpace(fileModel.Id)
            && !string.IsNullOrWhiteSpace(fileModel.Version)
            && string.Equals(fileModel.Runtime, "node", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(fileModel.ProtocolVersion)
            && fileModel.Entries is { Count: > 0 }
            && (fileModel.I18n == null
                || (!string.IsNullOrWhiteSpace(fileModel.I18n.DefaultLocale)
                    && !string.IsNullOrWhiteSpace(fileModel.I18n.Catalog)
                    && !string.IsNullOrWhiteSpace(fileModel.I18n.LocalesPath)));
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
        public string? Version { get; init; }
        public string? Runtime { get; init; }
        public string? ProtocolVersion { get; init; }
        public List<EntryManifestFile>? Entries { get; init; }
        public I18nManifestFile? I18n { get; init; }
    }

    private sealed class EntryManifestFile
    {
        public string? Id { get; init; }
        public LocalizedNameDto? Name { get; init; }
        public string? Entry { get; init; }
        public List<string>? Keywords { get; init; }
        public string? HotKey { get; init; }
        public DetailManifestFile? Detail { get; init; }
    }

    /// <summary>
    /// plugin.json 中 entry.name 的反序列化 DTO。
    /// 使用独立 DTO 避免 LocalizedMessage 多构造函数导致的 JSON 反序列化歧义。
    /// </summary>
    private sealed class LocalizedNameDto
    {
        public string? Key { get; init; }
        public string? DefaultValue { get; init; }
    }

    private sealed class DetailManifestFile
    {
        public string? Type { get; init; }
        public string? Entry { get; init; }
    }

    private sealed class I18nManifestFile
    {
        public string? DefaultLocale { get; init; }
        public string? Catalog { get; init; }
        public string? LocalesPath { get; init; }
        public List<string>? SupportedLocales { get; init; }
    }
}