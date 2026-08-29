using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.Localization;
using MyTools.Protocol.Manifest;
using MyTools.Protocol.Versioning;

namespace MyTools.Plugins.NodePlugins;

public sealed class NodePluginCatalog
{
    private readonly string pluginRoot;
    private readonly ILogger<NodePluginCatalog> logger;
    private readonly bool includeDevelopmentRegistrations;

    public NodePluginCatalog(ILogger<NodePluginCatalog> logger)
        : this(Path.Combine(ConfigPath.Base, "plugins"), logger, true)
    {
    }

    public NodePluginCatalog(string pluginRoot, ILogger<NodePluginCatalog> logger)
        : this(pluginRoot, logger, false)
    {
    }

    private NodePluginCatalog(string pluginRoot, ILogger<NodePluginCatalog> logger, bool includeDevelopmentRegistrations)
    {
        this.pluginRoot = pluginRoot;
        this.logger = logger;
        this.includeDevelopmentRegistrations = includeDevelopmentRegistrations;
    }

    public IReadOnlyList<NodePluginManifest> Plugins { get; private set; } = [];
    public IReadOnlySet<string> DevelopmentPluginIds { get; private set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        var developmentIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in includeDevelopmentRegistrations
                     ? DevelopmentPluginRegistrationStore.Load().Where(item =>
                         DevelopmentPluginSession.IsActive(item.PluginId))
                     : [])
        {
            try
            {
                var distDirectory = Path.GetFullPath(registration.DistPath);
                var manifestPath = Path.Combine(distDirectory, "plugin.json");
                if (!File.Exists(manifestPath))
                {
                    continue;
                }

                manifests.RemoveAll(item => string.Equals(item.ParentId, registration.PluginId, StringComparison.OrdinalIgnoreCase));
                manifests.AddRange(ReadManifests(distDirectory, manifestPath));
                developmentIds.Add(registration.PluginId);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Skipping invalid development plugin registration {PluginId}.", registration.PluginId);
            }
        }

        Plugins = manifests;
        DevelopmentPluginIds = developmentIds;
        logger.LogInformation("Discovered {Count} node plugin manifests in {PluginRoot}.", Plugins.Count, pluginRoot);
        return Plugins;
    }

    public bool IsDevelopmentPlugin(string pluginId)
    {
        var parentId = pluginId.Split(':', 2)[0];
        return DevelopmentPluginIds.Contains(parentId);
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

            var configurationResult = PluginConfigurationValidator.Validate(fileModel.Configuration);
            if (!configurationResult.IsValid)
            {
                logger.LogWarning(
                    "Skipping node plugin manifest with invalid configuration: {ManifestPath}. {Reason}",
                    manifestPath,
                    configurationResult.Error?.Message);
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
            var entryFullPath = ResolveFileUnderPluginRoot(fullPluginDirectory, fileModel.Entry!);
            if (entryFullPath == null)
            {
                logger.LogWarning("Skipping node plugin manifest with invalid backend entry: {ManifestPath}", manifestPath);
                return [];
            }

            var detailResult = PluginDetailResolver.TryResolve(
                fileModel.Detail?.Type,
                fileModel.Detail?.Entry,
                fileModel.Id!,
                out var resolvedDetail);
            if (!detailResult.IsValid)
            {
                logger.LogWarning(
                    "Skipping node plugin manifest with invalid detail: {ManifestPath}. {Reason}",
                    manifestPath,
                    detailResult.Error?.Message);
                return [];
            }

            string? detailEntry = null;
            string? detailEntryFullPath = null;
            if (resolvedDetail.IsWeb)
            {
                detailEntry = resolvedDetail.Entry;
                detailEntryFullPath = ResolveFileUnderPluginRoot(fullPluginDirectory, resolvedDetail.Entry!);
                if (detailEntryFullPath == null)
                {
                    logger.LogWarning("Skipping node plugin manifest with invalid detail entry: {ManifestPath}", manifestPath);
                    return [];
                }
            }

            return
            [
                new NodePluginManifest
                {
                    Id = fileModel.Id!,
                    ParentId = fileModel.Id!,
                    NameMessage = fileModel.Name != null
                        ? new LocalizedMessage(fileModel.Name.Key ?? "", fileModel.Name.DefaultValue ?? "")
                        : null,
                    DescriptionMessage = fileModel.Description != null
                        ? new LocalizedMessage(fileModel.Description.Key ?? "", fileModel.Description.DefaultValue ?? "")
                        : null,
                    Version = fileModel.Version!,
                    Entry = fileModel.Entry!,
                    ProtocolVersion = fileModel.ProtocolVersion!,
                    PluginDirectory = fullPluginDirectory,
                    EntryFullPath = entryFullPath,
                    DetailEntry = detailEntry,
                    DetailEntryFullPath = detailEntryFullPath,
                    ShowStatusBarInPluginWindow = fileModel.Window?.ShowStatusBar ?? true,
                    Keywords = fileModel.Alias ?? [],
                    SearchGlobal = ResolveSearchGlobal(fileModel.Search),
                    HotKey = fileModel.HotKey,
                    Capabilities = fileModel.Capabilities ?? [],
                    Configuration = fileModel.Configuration ?? [],
                    Icon = fileModel.Icon,
                    DefaultLocale = fileModel.I18n?.DefaultLocale ?? "en-US",
                    CatalogFullPath = catalogFullPath,
                    LocalesDirectoryFullPath = localesDirectoryFullPath,
                    SupportedLocales = fileModel.I18n?.SupportedLocales ?? []
                }
            ];
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
            && fileModel.ProtocolVersion == ProtocolVersion.CurrentWire
            && !string.IsNullOrWhiteSpace(fileModel.Entry)
            && (fileModel.I18n == null
                || (!string.IsNullOrWhiteSpace(fileModel.I18n.DefaultLocale)
                    && !string.IsNullOrWhiteSpace(fileModel.I18n.Catalog)
                    && !string.IsNullOrWhiteSpace(fileModel.I18n.LocalesPath)));
    }

    /// <summary>Omitted <c>search.global</c> defaults to false (opt-in).</summary>
    private static bool ResolveSearchGlobal(SearchManifestFile? search) => search?.Global ?? false;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private sealed class NodePluginManifestFile
    {
        public string? Id { get; init; }
        public LocalizedNameDto? Name { get; init; }
        public string? Version { get; init; }
        public string? ProtocolVersion { get; init; }
        public string? Entry { get; init; }
        public List<string>? Alias { get; init; }
        public SearchManifestFile? Search { get; init; }
        public WindowManifestFile? Window { get; init; }
        public string? HotKey { get; init; }
        public List<string>? Capabilities { get; init; }
        public DetailManifestFile? Detail { get; init; }
        public I18nManifestFile? I18n { get; init; }
        public string? Icon { get; init; }
        public LocalizedNameDto? Description { get; init; }
        public List<PluginConfigurationSettingV3>? Configuration { get; init; }
    }

    private sealed class SearchManifestFile
    {
        public bool? Global { get; init; }
    }

    private sealed class WindowManifestFile
    {
        public bool? ShowStatusBar { get; init; }
    }

    /// <summary>
    /// plugin.json 中 name 的反序列化 DTO。
    /// 使用独立 DTO 避免 LocalizedMessage 多构造函数导致的 JSON 反序列化歧义。
    /// </summary>
    private sealed class LocalizedNameDto
    {
        public string? Key { get; init; }
        public string? DefaultValue { get; init; }
    }

    private sealed class DetailManifestFile
    {
        public string Type { get; init; } = PluginDetailTypes.Web;
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
