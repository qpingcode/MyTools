using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Plugins;
using MyTools.Common.Utils;
using MyTools.Plugins.NodePlugins;
using MyTools.Plugins.Param;
using SystemDirectory = System.IO.Directory;
using LuceneDirectory = Lucene.Net.Store.Directory;

namespace MyTools.Plugins;

public sealed class FileSearcher : PluginBase, IDisposable
{
    public const string SearchDirectoriesSettingName = "SearchDirectories";
    public const string SearchDirectoriesSettingPath = "file-searcher.SearchDirectories";
    public const string IgnorePatternsSettingName = "IgnorePatterns";
    public const string IgnorePatternsSettingPath = "file-searcher.IgnorePatterns";
    public const string UseIgnoreFilesSettingName = "UseIgnoreFiles";
    public const string UseIgnoreFilesSettingPath = "file-searcher.UseIgnoreFiles";
    public const string KeepFilesFromRemovedVolumesSettingName = "KeepFilesFromRemovedVolumes";
    public const string KeepFilesFromRemovedVolumesSettingPath = "file-searcher.KeepFilesFromRemovedVolumes";

    private const LuceneVersion LuceneVersion = Lucene.Net.Util.LuceneVersion.LUCENE_48;
    private const string PluginDataId = "file-searcher";
    private const string IndexDir = "FileSearcherIndex";
    private const string IndexedRootsFileName = "FileSearcherIndexedRoots.json";
    private const string RootField = "root";
    private static readonly TimeSpan WatchDebounce = TimeSpan.FromSeconds(10);
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private readonly ILogger<FileSearcher> logger;
    private readonly IMemoryCache cache;
    private readonly object indexLock = new();
    private readonly object indexWriteLock = new();
    private readonly object watcherLock = new();
    private readonly object configurationRequestLock = new();
    private readonly SemaphoreSlim configurationLock = new(1, 1);
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly CancellationToken disposeToken;
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(PathComparer);
    private readonly Dictionary<string, CancellationTokenSource> pendingReindexes = new(PathComparer);
    private HashSet<string> configuredDirectories = new(PathComparer);
    private FileSearchConfiguration currentConfiguration = FileSearchConfiguration.Empty;
    private IConfigurationRegistry? configurationRegistry;
    private ConfigurationSetting? searchDirectoriesSetting;
    private ConfigurationSetting? ignorePatternsSetting;
    private ConfigurationSetting? useIgnoreFilesSetting;
    private ConfigurationSetting? keepFilesFromRemovedVolumesSetting;
    private IndexWriter? indexWriter;
    private LuceneDirectory? indexDirectory;
    private Analyzer? analyzer;
    private HashSet<string> indexedRoots = new(PathComparer);
    private long configurationRevision;
    private CancellationTokenSource? configurationApplyCancellation;
    private bool initialized;
    private bool disposed;

    public FileSearcher(ILogger<FileSearcher> logger, IMemoryCache cache)
    {
        this.logger = logger;
        this.cache = cache;
        disposeToken = disposeCancellation.Token;
    }

    public override PluginId PluginId => new(PluginDataId);
    public override string Name => GetCaption("Plugin.FileSearcher.Name", "File Searcher");
    public override string Description => GetCaption("Plugin.FileSearcher.Description", "Search indexed files");
    protected override string SettingsCategoryName => Name;
    protected override string SettingsCategoryDescription => GetCaption(
        "Plugin.FileSearcher.Settings.Category.Description",
        "Configure indexed directories and exclusion rules");

    public override List<IActionWithHotkey> Actions =>
    [
        WellKnownActions.Execute.WithDefaultHotkey(),
        WellKnownActions.AdminExecute.WithHotkey(Hotkey.Ctrl(HotkeyKey.Enter)),
        WellKnownActions.OpenInExplorer.WithHotkey(Hotkey.Ctrl(HotkeyKey.O))
    ];

    public override bool IsGlobalSearchPlugin => true;

    protected override void AddPluginSettings(
        ConfigurationCategory pluginCategory,
        IConfigurationRegistry registry)
    {
        if (configurationRegistry != null)
        {
            configurationRegistry.ConfigurationChanged -= OnConfigurationChanged;
        }

        configurationRegistry = registry;
        searchDirectoriesSetting = registry.AddSetting(
            pluginCategory,
            SearchDirectoriesSettingName,
            GetCaption("Plugin.FileSearcher.Settings.SearchDirectories.Title", "Search directories"),
            GetCaption(
                "Plugin.FileSearcher.Settings.SearchDirectories.Description",
                "Add or remove directories whose files should be indexed"),
            CreateDefaultSearchDirectories(),
            new JsonElementSettingSerializer(),
            valueType: SettingValueTypes.Array);
        searchDirectoriesSetting.UiHint = "directory-list";
        searchDirectoriesSetting.Schema = new SettingSchema
        {
            Properties =
            [
                new SettingSchemaProperty
                {
                    Key = "Path",
                    Type = SchemaPropertyType.Path,
                    Title = GetCaption("Plugin.FileSearcher.Settings.SearchDirectories.Path", "Directory"),
                    UiHint = "directory"
                }
            ]
        };

        ignorePatternsSetting = registry.AddSetting(
            pluginCategory,
            IgnorePatternsSettingName,
            GetCaption("Plugin.FileSearcher.Settings.IgnorePatterns.Title", "Ignore patterns"),
            GetCaption(
                "Plugin.FileSearcher.Settings.IgnorePatterns.Description",
                "Exclude matching files and directories from the index"),
            CreateDefaultIgnorePatterns(),
            new JsonElementSettingSerializer(),
            valueType: SettingValueTypes.Array);
        ignorePatternsSetting.Schema = new SettingSchema
        {
            Properties =
            [
                new SettingSchemaProperty
                {
                    Key = "Pattern",
                    Type = SchemaPropertyType.String,
                    Title = GetCaption("Plugin.FileSearcher.Settings.IgnorePatterns.Pattern", "Pattern")
                }
            ]
        };

        useIgnoreFilesSetting = registry.AddSetting(
            pluginCategory,
            UseIgnoreFilesSettingName,
            GetCaption("Plugin.FileSearcher.Settings.UseIgnoreFiles.Title", "Use ignore files"),
            GetCaption(
                "Plugin.FileSearcher.Settings.UseIgnoreFiles.Description",
                "Respect .gitignore and .ignore files found while indexing"),
            true,
            null);

        keepFilesFromRemovedVolumesSetting = registry.AddSetting(
            pluginCategory,
            KeepFilesFromRemovedVolumesSettingName,
            GetCaption(
                "Plugin.FileSearcher.Settings.KeepFilesFromRemovedVolumes.Title",
                "Keep files from removed volumes"),
            GetCaption(
                "Plugin.FileSearcher.Settings.KeepFilesFromRemovedVolumes.Description",
                "Preserve indexed files when their volumes are disconnected. Useful for network drives and external storage"),
            false,
            null);
        registry.ConfigurationChanged += OnConfigurationChanged;

        if (initialized)
        {
            QueueConfigurationApply();
        }
    }

    public override async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        initialized = true;
        var configuration = ReadCurrentConfiguration();
        var request = BeginConfigurationApply();
        try
        {
            await ApplyConfiguredDirectoriesAsync(
                configuration, request.Revision, request.CancellationToken);
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            // A newer configuration request superseded initialization.
        }
        finally
        {
            CompleteConfigurationApply(request);
        }
    }

    private void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs args)
    {
        if (IsFileSearchSetting(args.Setting.Key))
        {
            QueueConfigurationApply();
        }
    }

    private static bool IsFileSearchSetting(string key) =>
        key.Equals(SearchDirectoriesSettingPath, StringComparison.OrdinalIgnoreCase)
        || key.Equals(IgnorePatternsSettingPath, StringComparison.OrdinalIgnoreCase)
        || key.Equals(UseIgnoreFilesSettingPath, StringComparison.OrdinalIgnoreCase)
        || key.Equals(KeepFilesFromRemovedVolumesSettingPath, StringComparison.OrdinalIgnoreCase);

    private FileSearchConfiguration ReadCurrentConfiguration() => new(
        ReadSearchDirectories(searchDirectoriesSetting?.CurrentValue ?? CreateDefaultSearchDirectories()),
        ReadIgnorePatterns(ignorePatternsSetting?.CurrentValue ?? CreateDefaultIgnorePatterns()),
        useIgnoreFilesSetting?.CurrentValue as bool? ?? true,
        keepFilesFromRemovedVolumesSetting?.CurrentValue as bool? ?? false);

    private void QueueConfigurationApply()
    {
        if (disposed) return;

        var configuration = ReadCurrentConfiguration();
        var request = BeginConfigurationApply();
        _ = Task.Run(async () =>
        {
            try
            {
                await ApplyConfiguredDirectoriesAsync(
                    configuration, request.Revision, request.CancellationToken);
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply FileSearcher directory configuration.");
            }
            finally
            {
                CompleteConfigurationApply(request);
            }
        });
    }

    private ConfigurationApplyRequest BeginConfigurationApply()
    {
        lock (configurationRequestLock)
        {
            configurationApplyCancellation?.Cancel();
            if (disposed)
            {
                var canceled = new CancellationTokenSource();
                canceled.Cancel();
                return new ConfigurationApplyRequest(
                    Interlocked.Increment(ref configurationRevision),
                    canceled);
            }

            configurationApplyCancellation = CancellationTokenSource.CreateLinkedTokenSource(disposeToken);
            return new ConfigurationApplyRequest(
                Interlocked.Increment(ref configurationRevision),
                configurationApplyCancellation);
        }
    }

    private void CompleteConfigurationApply(ConfigurationApplyRequest request)
    {
        lock (configurationRequestLock)
        {
            if (ReferenceEquals(configurationApplyCancellation, request.Cancellation))
            {
                configurationApplyCancellation = null;
            }
        }

        request.Cancellation.Dispose();
    }

    private async Task ApplyConfiguredDirectoriesAsync(
        FileSearchConfiguration configuration,
        long revision,
        CancellationToken cancellationToken)
    {
        await configurationLock.WaitAsync(cancellationToken);
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (revision != Interlocked.Read(ref configurationRevision) || disposed) return;

            var desiredDirectories = configuration.Directories;
            HashSet<string> previousDirectories;
            lock (watcherLock)
            {
                previousDirectories = new HashSet<string>(configuredDirectories, PathComparer);
                configuredDirectories = new HashSet<string>(desiredDirectories, PathComparer);
                currentConfiguration = configuration;
            }

            var changes = CalculateDirectoryChanges(previousDirectories, desiredDirectories);
            var removed = changes.Removed;
            var added = changes.Added;

            // A recursive watcher on a large root also observes the Lucene files written under
            // that root (for example under AppData). Stop watching until the replacement index is
            // committed so those writes cannot overflow the native watcher buffer.
            foreach (var directory in previousDirectories.Union(desiredDirectories, PathComparer))
            {
                StopWatching(directory);
            }

            Stopwatch stopwatch = Stopwatch.StartNew();
            lock (indexLock)
            {
                EnsureIndexWriter();
            }

            var preparedDirectories = await RunIndexingWorkAsync(
                () => desiredDirectories
                    .Select(directory =>
                    {
                        logger.LogInformation(
                            "Starting FileSearcher index for {Directory}.", directory);
                        return PrepareDirectoryIndex(directory, configuration, cancellationToken);
                    })
                    .ToArray(),
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            if (revision != Interlocked.Read(ref configurationRevision) || disposed) return;

            lock (indexWriteLock)
            {
                IndexWriter writer;
                lock (indexLock)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (revision != Interlocked.Read(ref configurationRevision)
                        || disposed
                        || indexWriter == null)
                    {
                        return;
                    }
                    writer = indexWriter;
                }

                var removedFromIndex = indexedRoots.Except(desiredDirectories, PathComparer).ToArray();
                foreach (var directory in removedFromIndex)
                {
                    writer.DeleteDocuments(new Term(RootField, directory));
                    indexedRoots.Remove(directory);
                }
                foreach (var prepared in preparedDirectories)
                {
                    if (!prepared.IsAvailable && configuration.KeepFilesFromRemovedVolumes) continue;
                    ReplaceDirectoryIndex(writer, prepared, commit: false);
                    if (prepared.IsAvailable)
                    {
                        indexedRoots.Add(prepared.RootDirectory);
                    }
                    else
                    {
                        indexedRoots.Remove(prepared.RootDirectory);
                    }
                }
                writer.Commit();
                WriteIndexedRoots();
            }
            stopwatch.Stop();
            logger.LogInformation(
                "FileSearcher directories updated: {AddedCount} added, {RemovedCount} removed, cost {CostTime} ms.",
                added.Length, removed.Length, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            if (revision == Interlocked.Read(ref configurationRevision) && !disposed)
            {
                foreach (var directory in configuration.Directories)
                {
                    StartWatching(directory);
                }
            }
            configurationLock.Release();
        }
    }

    private void EnsureIndexWriter()
    {
        if (indexWriter != null) return;

        var pluginDataDirectory = ConfigPath.PluginDataDirectory(PluginDataId);
        SystemDirectory.CreateDirectory(pluginDataDirectory);
        MigrateLegacyIndexStorage(pluginDataDirectory);

        var indexPath = GetIndexDirectory(ConfigPath.PluginsDataPath);
        SystemDirectory.CreateDirectory(indexPath);
        indexDirectory = FSDirectory.Open(indexPath);
        analyzer = new StandardAnalyzer(LuceneVersion);
        var rootsPath = GetIndexedRootsPath(ConfigPath.PluginsDataPath);
        var canReuseIndex = File.Exists(rootsPath) && DirectoryReader.IndexExists(indexDirectory);
        indexedRoots = canReuseIndex ? ReadIndexedRoots(rootsPath) : new HashSet<string>(PathComparer);
        var config = new IndexWriterConfig(LuceneVersion, analyzer)
        {
            OpenMode = canReuseIndex ? OpenMode.CREATE_OR_APPEND : OpenMode.CREATE
        };
        indexWriter = new IndexWriter(indexDirectory, config);
        indexWriter.Commit();
    }

    internal static string GetIndexDirectory(string pluginsDataRoot) =>
        Path.Combine(ConfigPath.PluginDataDirectory(pluginsDataRoot, PluginDataId), IndexDir);

    internal static string GetIndexedRootsPath(string pluginsDataRoot) =>
        Path.Combine(ConfigPath.PluginDataDirectory(pluginsDataRoot, PluginDataId), IndexedRootsFileName);

    private void MigrateLegacyIndexStorage(string pluginDataDirectory)
    {
        var legacyIndexPath = Path.Combine(ConfigPath.Base, IndexDir);
        var indexPath = Path.Combine(pluginDataDirectory, IndexDir);
        var legacyRootsPath = Path.Combine(ConfigPath.Base, IndexedRootsFileName);
        var rootsPath = Path.Combine(pluginDataDirectory, IndexedRootsFileName);

        try
        {
            if (!SystemDirectory.Exists(indexPath) && SystemDirectory.Exists(legacyIndexPath))
            {
                SystemDirectory.Move(legacyIndexPath, indexPath);
                logger.LogInformation(
                    "Migrated FileSearcher index from {LegacyPath} to {Path}.",
                    legacyIndexPath, indexPath);
            }

            if (!File.Exists(rootsPath) && File.Exists(legacyRootsPath))
            {
                File.Move(legacyRootsPath, rootsPath);
                logger.LogInformation(
                    "Migrated FileSearcher indexed roots from {LegacyPath} to {Path}.",
                    legacyRootsPath, rootsPath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(
                ex,
                "Could not migrate legacy FileSearcher index storage to {Directory}; a new index will be used.",
                pluginDataDirectory);
        }
    }

    private static HashSet<string> ReadIndexedRoots(string path)
    {
        try
        {
            var roots = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
            return roots
                .Select(NormalizeDirectoryPath)
                .Where(root => root != null)
                .Select(root => root!)
                .ToHashSet(PathComparer);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return new HashSet<string>(PathComparer);
        }
    }

    private void WriteIndexedRoots()
    {
        var path = GetIndexedRootsPath(ConfigPath.PluginsDataPath);
        try
        {
            File.WriteAllText(path, JsonSerializer.Serialize(indexedRoots.Order(PathComparer)));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not persist FileSearcher indexed roots.");
        }
    }

    private void ReplaceDirectoryIndex(IndexWriter writer, PreparedDirectoryIndex prepared, bool commit)
    {
        writer.DeleteDocuments(new Term(RootField, prepared.RootDirectory));
        foreach (var file in prepared.Files)
        {
            writer.AddDocument(new Document
            {
                new StringField(RootField, prepared.RootDirectory, Field.Store.NO),
                new StoredField("path", file.Path),
                new StoredField("filename", file.FileName),
                new StringField("indexedTime", file.IndexedTime, Field.Store.YES),
                new StringField("searchFilename", file.FileName.ToLowerInvariant(), Field.Store.NO),
                new StringField("searchInitials", file.SearchInitials, Field.Store.NO),
                new TextField("searchPossibles", file.FileName, Field.Store.NO)
            });
        }
        if (commit) writer.Commit();
        logger.LogInformation("Indexed {FileCount} files under {Directory}.",
            prepared.Files.Count, prepared.RootDirectory);
    }

    private PreparedDirectoryIndex PrepareDirectoryIndex(
        string rootDirectory,
        FileSearchConfiguration configuration,
        CancellationToken cancellationToken)
    {
        var files = new List<PreparedFile>();
        if (!SystemDirectory.Exists(rootDirectory))
        {
            logger.LogWarning("FileSearcher directory does not exist: {Directory}", rootDirectory);
            return new PreparedDirectoryIndex(rootDirectory, files, IsAvailable: false);
        }

        var indexedTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
        foreach (var file in EnumerateIndexableFiles(
                     rootDirectory, configuration.IgnoreMatcher, configuration.UseIgnoreFiles, cancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileName = Path.GetFileNameWithoutExtension(file);
            files.Add(new PreparedFile(
                file,
                fileName,
                StringUtils.GetInitialsFromWords(fileName),
                indexedTime));
        }

        return new PreparedDirectoryIndex(rootDirectory, files, IsAvailable: true);
    }

    internal IEnumerable<string> EnumerateIndexableFiles(
        string rootDirectory,
        FileSearchIgnoreMatcher ignoreMatcher,
        bool useIgnoreFiles,
        CancellationToken cancellationToken)
    {
        var pending = new Stack<PendingDirectory>();
        pending.Push(new PendingDirectory(rootDirectory, string.Empty, FileSearchIgnoreMatcher.ScopedRules.Empty));
        while (pending.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = pending.Pop();
            var scopedRules = useIgnoreFiles
                ? current.Rules.AddFromDirectory(current.Path, current.RelativePath)
                : current.Rules;
            string[] files;
            try
            {
                files = SystemDirectory.GetFiles(current.Path);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                logger.LogDebug(ex, "Skipping inaccessible directory {Directory}.", current.Path);
                continue;
            }

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootDirectory, file));
                if (!ignoreMatcher.IsIgnored(relativePath, isDirectory: false)
                    && !scopedRules.IsIgnored(relativePath, isDirectory: false))
                {
                    yield return file;
                }
            }

            string[] directories;
            try
            {
                directories = SystemDirectory.GetDirectories(current.Path);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                logger.LogDebug(ex, "Cannot enumerate child directories of {Directory}.", current.Path);
                continue;
            }

            foreach (var directory in directories)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootDirectory, directory));
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0
                        && !ignoreMatcher.IsIgnored(relativePath, isDirectory: true)
                        && !scopedRules.IsIgnored(relativePath, isDirectory: true))
                    {
                        pending.Push(new PendingDirectory(directory, relativePath, scopedRules));
                    }
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    logger.LogDebug(ex, "Skipping inaccessible directory {Directory}.", directory);
                }
            }
        }
    }

    private void StartWatching(string rootDirectory)
    {
        if (!SystemDirectory.Exists(rootDirectory)) return;

        try
        {
            var watcher = new FileSystemWatcher(rootDirectory)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
                               | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                InternalBufferSize = 64 * 1024
            };
            watcher.Changed += (_, args) =>
            {
                // The index contains paths and file names, not file contents. Ordinary writes do
                // not change searchable data; only ignore-file content changes require a rebuild.
                if (IsIgnoreRulesFile(args.FullPath))
                {
                    ScheduleDirectoryReindex(rootDirectory, args.ChangeType, args.FullPath);
                }
            };
            watcher.Created += (_, args) =>
            {
                if (ShouldReindexForChange(rootDirectory, args.FullPath))
                    ScheduleDirectoryReindex(rootDirectory, args.ChangeType, args.FullPath);
            };
            watcher.Deleted += (_, args) =>
            {
                if (ShouldReindexForChange(rootDirectory, args.FullPath))
                    ScheduleDirectoryReindex(rootDirectory, args.ChangeType, args.FullPath);
            };
            watcher.Renamed += (_, args) =>
            {
                if (ShouldReindexForChange(rootDirectory, args.FullPath)
                    || ShouldReindexForChange(rootDirectory, args.OldFullPath))
                {
                    ScheduleDirectoryReindex(rootDirectory, args.ChangeType, args.FullPath);
                }
            };
            watcher.Error += (_, args) =>
            {
                PauseWatching(rootDirectory);
                logger.LogWarning(args.GetException(),
                    "FileSearcher watcher error for {Directory}; rebuilding that directory.", rootDirectory);
                ScheduleDirectoryReindex(rootDirectory, WatcherChangeTypes.All, rootDirectory);
            };

            lock (watcherLock)
            {
                if (disposed || !configuredDirectories.Contains(rootDirectory))
                {
                    watcher.Dispose();
                    return;
                }
                if (watchers.Remove(rootDirectory, out var previous)) previous.Dispose();
                watchers[rootDirectory] = watcher;
                watcher.EnableRaisingEvents = true;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not monitor FileSearcher directory {Directory}.", rootDirectory);
        }
    }

    private void PauseWatching(string rootDirectory)
    {
        lock (watcherLock)
        {
            if (watchers.TryGetValue(rootDirectory, out var watcher))
            {
                watcher.EnableRaisingEvents = false;
            }
        }
    }

    private bool ShouldReindexForChange(string rootDirectory, string path)
    {
        if (IsIgnoreRulesFile(path))
        {
            return true;
        }

        FileSearchConfiguration configuration;
        lock (watcherLock)
        {
            configuration = currentConfiguration;
        }

        var relativePath = NormalizeRelativePath(Path.GetRelativePath(rootDirectory, path));
        return !configuration.IgnoreMatcher.IsIgnored(relativePath, SystemDirectory.Exists(path));
    }

    internal static bool IsIgnoreRulesFile(string path)
    {
        var fileName = Path.GetFileName(path);
        return fileName.Equals(".gitignore", StringComparison.OrdinalIgnoreCase)
               || fileName.Equals(".ignore", StringComparison.OrdinalIgnoreCase);
    }

    private void StopWatching(string rootDirectory)
    {
        lock (watcherLock)
        {
            if (watchers.Remove(rootDirectory, out var watcher)) watcher.Dispose();
            if (pendingReindexes.Remove(rootDirectory, out var pending))
            {
                pending.Cancel();
            }
        }
    }

    private void ScheduleDirectoryReindex(
        string rootDirectory,
        WatcherChangeTypes changeType,
        string changedPath)
    {
        CancellationTokenSource pending;
        lock (watcherLock)
        {
            if (disposed || !configuredDirectories.Contains(rootDirectory)) return;
            if (pendingReindexes.Remove(rootDirectory, out var previous))
            {
                previous.Cancel();
            }
            pending = CancellationTokenSource.CreateLinkedTokenSource(disposeToken);
            pendingReindexes[rootDirectory] = pending;
        }
        var pendingToken = pending.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(WatchDebounce, pendingToken);
                lock (watcherLock)
                {
                    if (disposed || !configuredDirectories.Contains(rootDirectory)) return;
                }

                PauseWatching(rootDirectory);
                logger.LogInformation(
                    "Starting FileSearcher reindex for {Directory}; trigger {ChangeType} at {ChangedPath}.",
                    rootDirectory, changeType, changedPath);
                Stopwatch stopwatch = Stopwatch.StartNew();
                FileSearchConfiguration configuration;
                lock (watcherLock)
                {
                    configuration = currentConfiguration;
                }
                var prepared = await RunIndexingWorkAsync(
                    () => PrepareDirectoryIndex(rootDirectory, configuration, pendingToken),
                    pendingToken);

                lock (watcherLock)
                {
                    if (disposed || !configuredDirectories.Contains(rootDirectory)) return;
                }

                lock (indexWriteLock)
                {
                    IndexWriter writer;
                    lock (indexLock)
                    {
                        if (disposed || indexWriter == null) return;
                        writer = indexWriter;
                    }
                    if (!prepared.IsAvailable && configuration.KeepFilesFromRemovedVolumes) return;
                    ReplaceDirectoryIndex(writer, prepared, commit: true);
                    if (prepared.IsAvailable)
                    {
                        indexedRoots.Add(rootDirectory);
                    }
                    else
                    {
                        indexedRoots.Remove(rootDirectory);
                    }
                    WriteIndexedRoots();
                }
                stopwatch.Stop();
                logger.LogInformation("Reindexed changed FileSearcher directory {Directory}, cost {CostTime} ms.",
                    rootDirectory, stopwatch.ElapsedMilliseconds);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to reindex changed directory {Directory}.", rootDirectory);
            }
            finally
            {
                var shouldRestartWatcher = false;
                lock (watcherLock)
                {
                    if (pendingReindexes.TryGetValue(rootDirectory, out var current) && ReferenceEquals(current, pending))
                    {
                        pendingReindexes.Remove(rootDirectory);
                        shouldRestartWatcher = !disposed && configuredDirectories.Contains(rootDirectory);
                    }
                    pending.Dispose();
                }
                if (shouldRestartWatcher)
                {
                    StartWatching(rootDirectory);
                }
            }
        });
    }

    public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken,
        SearchOptions? searchOptions = null)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            DirectoryReader reader;
            lock (indexLock)
            {
                if (indexDirectory is null || !DirectoryReader.IndexExists(indexDirectory))
                {
                    throw new InvalidOperationException("FileSearcher index is not initialized.");
                }
                reader = DirectoryReader.Open(indexDirectory);
            }

            using (reader)
            {
                query = query.ToLowerInvariant();
                var searcher = new IndexSearcher(reader);
                var prefixQuery1 = new PrefixQuery(new Term("searchInitials", query)) { Boost = 10.0f };
                var prefixQuery2 = new PrefixQuery(new Term("searchFilename", query)) { Boost = 2.0f };
                var prefixQuery3 = new PrefixQuery(new Term("searchPossibles", query)) { Boost = 2.0f };
                var combineQuery = new BooleanQuery
                {
                    { prefixQuery1, Occur.SHOULD },
                    { prefixQuery2, Occur.SHOULD },
                    { prefixQuery3, Occur.SHOULD }
                };

                var results = new List<ResultItem>();
                foreach (var hit in searcher.Search(combineQuery, 30).ScoreDocs)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var doc = searcher.Doc(hit.Doc);
                    var title = doc.Get("filename");
                    var path = doc.Get("path");
                    var score = (int)Math.Ceiling(hit.Score * 1000);
                    results.Add(new ResultItem(GetFileIcon(path), title, path, ActionStringParam.From(path), score));
                }
                return Task.FromResult(Result.CreateSuccessResult(results));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return Task.FromResult(Result.CreateFailure(ex.Message, ex));
        }
    }

    private Icon GetFileIcon(string path)
    {
        var icon = cache.GetOrCreate<Icon?>(PluginConstants.FileSearcherCachePrefix + path, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            var imageData = FileIconHelper.GetFileIconData(path);
            return imageData != null ? new ImageIcon(imageData) : null;
        });
        return icon ?? new StringIcon("📄");
    }

    internal static IReadOnlySet<string> ReadSearchDirectories(object? value)
    {
        var result = new HashSet<string>(PathComparer);
        if (value is not JsonElement { ValueKind: JsonValueKind.Array } array) return result;

        foreach (var item in array.EnumerateArray())
        {
            string? path = null;
            if (item.ValueKind == JsonValueKind.String)
            {
                path = item.GetString();
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var property = item.EnumerateObject()
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, "Path", StringComparison.OrdinalIgnoreCase));
                if (property.Value.ValueKind == JsonValueKind.String) path = property.Value.GetString();
            }

            var normalized = NormalizeDirectoryPath(path);
            if (normalized != null) result.Add(normalized);
        }
        return result;
    }

    internal static IReadOnlyList<string> ReadIgnorePatterns(object? value)
    {
        var result = new List<string>();
        if (value is not JsonElement { ValueKind: JsonValueKind.Array } array) return result;

        foreach (var item in array.EnumerateArray())
        {
            string? pattern = null;
            if (item.ValueKind == JsonValueKind.String)
            {
                pattern = item.GetString();
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                var property = item.EnumerateObject()
                    .FirstOrDefault(candidate => string.Equals(candidate.Name, "Pattern", StringComparison.OrdinalIgnoreCase));
                if (property.Value.ValueKind == JsonValueKind.String) pattern = property.Value.GetString();
            }

            pattern = pattern?.Trim();
            if (!string.IsNullOrEmpty(pattern)
                && !result.Contains(pattern, StringComparer.OrdinalIgnoreCase))
            {
                result.Add(pattern);
            }
        }
        return result;
    }

    internal static DirectoryChanges CalculateDirectoryChanges(
        IEnumerable<string> previous,
        IEnumerable<string> current)
    {
        var previousSet = previous.ToHashSet(PathComparer);
        var currentSet = current.ToHashSet(PathComparer);
        return new DirectoryChanges(
            currentSet.Except(previousSet, PathComparer).ToArray(),
            previousSet.Except(currentSet, PathComparer).ToArray());
    }

    internal sealed record DirectoryChanges(string[] Added, string[] Removed);

    internal static JsonElement CreateDefaultSearchDirectories() =>
        JsonSerializer.SerializeToElement(DefaultSearchDirectoryPaths()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(PathComparer)
            .Select(path => new Dictionary<string, string> { ["Path"] = path }));

    private static IEnumerable<string> DefaultSearchDirectoryPaths()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
    }

    internal static JsonElement CreateDefaultIgnorePatterns() => JsonSerializer.SerializeToElement(
        new[]
        {
            "*.tmp",
            "*.temp",
            "node_modules",
            "**/tmp/**",
            "**/temp/**",
            "**/[Cc]ache/**",
            "**/[Cc]aches/**",
            "**/AppData/**"
        }.Select(pattern => new Dictionary<string, string> { ["Pattern"] = pattern }));

    private static string? NormalizeDirectoryPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"')));
            var pathRoot = Path.GetPathRoot(fullPath);
            return string.Equals(fullPath, pathRoot, StringComparison.OrdinalIgnoreCase)
                ? fullPath
                : fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return null;
        }
    }

    private static string NormalizeRelativePath(string path) => path.Replace('\\', '/').Trim('/');

    private static Task<T> RunIndexingWorkAsync<T>(Func<T> work, CancellationToken cancellationToken) =>
        Task.Factory.StartNew(
            work,
            cancellationToken,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default);

    private sealed record PreparedDirectoryIndex(
        string RootDirectory,
        IReadOnlyList<PreparedFile> Files,
        bool IsAvailable);
    private sealed record PreparedFile(
        string Path,
        string FileName,
        string SearchInitials,
        string IndexedTime);
    private sealed record PendingDirectory(
        string Path,
        string RelativePath,
        FileSearchIgnoreMatcher.ScopedRules Rules);
    private sealed record FileSearchConfiguration(
        IReadOnlySet<string> Directories,
        IReadOnlyList<string> IgnorePatterns,
        bool UseIgnoreFiles,
        bool KeepFilesFromRemovedVolumes)
    {
        public FileSearchIgnoreMatcher IgnoreMatcher { get; } = new(IgnorePatterns);

        public static FileSearchConfiguration Empty { get; } = new(
            new HashSet<string>(PathComparer), [], true, false);
    }
    private readonly record struct ConfigurationApplyRequest(
        long Revision,
        CancellationTokenSource Cancellation)
    {
        public CancellationToken CancellationToken => Cancellation.Token;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (configurationRegistry != null) configurationRegistry.ConfigurationChanged -= OnConfigurationChanged;
        disposeCancellation.Cancel();
        CancellationTokenSource? activeConfigurationApply;
        lock (configurationRequestLock)
        {
            activeConfigurationApply = configurationApplyCancellation;
            configurationApplyCancellation = null;
            activeConfigurationApply?.Cancel();
        }
        // The owning apply operation disposes its source after observing cancellation.

        lock (watcherLock)
        {
            foreach (var watcher in watchers.Values) watcher.Dispose();
            watchers.Clear();
            foreach (var pending in pendingReindexes.Values)
            {
                pending.Cancel();
            }
            pendingReindexes.Clear();
            configuredDirectories.Clear();
        }

        lock (indexWriteLock)
        {
            lock (indexLock)
            {
                indexWriter?.Dispose();
                analyzer?.Dispose();
                indexDirectory?.Dispose();
                indexWriter = null;
                analyzer = null;
                indexDirectory = null;
            }
        }
        disposeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
