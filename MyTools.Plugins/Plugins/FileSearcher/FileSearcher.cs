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

    private const LuceneVersion LuceneVersion = Lucene.Net.Util.LuceneVersion.LUCENE_48;
    private const string IndexDir = "FileSearcherIndex";
    private const string RootField = "root";
    private static readonly TimeSpan WatchDebounce = TimeSpan.FromSeconds(1);
    private static readonly StringComparer PathComparer = StringComparer.OrdinalIgnoreCase;

    private readonly ILogger<FileSearcher> logger;
    private readonly IMemoryCache cache;
    private readonly object indexLock = new();
    private readonly object watcherLock = new();
    private readonly SemaphoreSlim configurationLock = new(1, 1);
    private readonly CancellationTokenSource disposeCancellation = new();
    private readonly Dictionary<string, FileSystemWatcher> watchers = new(PathComparer);
    private readonly Dictionary<string, CancellationTokenSource> pendingReindexes = new(PathComparer);
    private HashSet<string> configuredDirectories = new(PathComparer);
    private IConfigurationRegistry? configurationRegistry;
    private ConfigurationSetting? searchDirectoriesSetting;
    private IndexWriter? indexWriter;
    private LuceneDirectory? indexDirectory;
    private Analyzer? analyzer;
    private long configurationRevision;
    private bool initialized;
    private bool disposed;

    public FileSearcher(ILogger<FileSearcher> logger, IMemoryCache cache)
    {
        this.logger = logger;
        this.cache = cache;
    }

    public override PluginId PluginId => new("file-searcher");
    public override string Name => GetCaption("Plugin.FileSearcher.Name", "File Searcher");
    public override string Description => GetCaption("Plugin.FileSearcher.Description", "Search for files and scripts");
    protected override string SettingsCategoryName => Name;
    protected override string SettingsCategoryDescription => GetCaption(
        "Plugin.FileSearcher.Settings.Category.Description",
        "Choose the directories whose shortcuts and PowerShell scripts are searchable");

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
                "Add or remove directories containing shortcuts (.lnk) and PowerShell scripts (.ps1)"),
            CreateDefaultSearchDirectories(),
            new JsonElementSettingSerializer(),
            valueType: SettingValueTypes.Array);
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
        registry.ConfigurationChanged += OnConfigurationChanged;

        if (initialized)
        {
            QueueConfigurationApply(searchDirectoriesSetting.CurrentValue);
        }
    }

    public override async Task InitializeAsync()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        initialized = true;
        var value = searchDirectoriesSetting?.CurrentValue ?? CreateDefaultSearchDirectories();
        var revision = Interlocked.Increment(ref configurationRevision);
        await ApplyConfiguredDirectoriesAsync(ReadSearchDirectories(value), revision);
    }

    private void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs args)
    {
        if (string.Equals(args.Setting.Key, SearchDirectoriesSettingPath, StringComparison.OrdinalIgnoreCase))
        {
            QueueConfigurationApply(args.NewValue);
        }
    }

    private void QueueConfigurationApply(object? value)
    {
        if (disposed) return;

        var directories = ReadSearchDirectories(value);
        var revision = Interlocked.Increment(ref configurationRevision);
        _ = Task.Run(async () =>
        {
            try
            {
                await ApplyConfiguredDirectoriesAsync(directories, revision);
            }
            catch (OperationCanceledException) when (disposeCancellation.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to apply FileSearcher directory configuration.");
            }
        });
    }

    private async Task ApplyConfiguredDirectoriesAsync(IReadOnlySet<string> desiredDirectories, long revision)
    {
        await configurationLock.WaitAsync(disposeCancellation.Token);
        try
        {
            if (revision != Interlocked.Read(ref configurationRevision) || disposed) return;

            HashSet<string> previousDirectories;
            lock (watcherLock)
            {
                previousDirectories = new HashSet<string>(configuredDirectories, PathComparer);
                configuredDirectories = new HashSet<string>(desiredDirectories, PathComparer);
            }

            var changes = CalculateDirectoryChanges(previousDirectories, desiredDirectories);
            var removed = changes.Removed;
            var added = changes.Added;

            foreach (var directory in removed) StopWatching(directory);
            foreach (var directory in added) StartWatching(directory);

            Stopwatch stopwatch = Stopwatch.StartNew();
            lock (indexLock)
            {
                EnsureIndexWriter();
                foreach (var directory in removed)
                {
                    indexWriter!.DeleteDocuments(new Term(RootField, directory));
                }
                foreach (var directory in added)
                {
                    ReplaceDirectoryIndex(directory, commit: false);
                }
                indexWriter!.Commit();
            }
            stopwatch.Stop();
            logger.LogInformation(
                "FileSearcher directories updated: {AddedCount} added, {RemovedCount} removed, cost {CostTime} ms.",
                added.Length, removed.Length, stopwatch.ElapsedMilliseconds);
        }
        finally
        {
            configurationLock.Release();
        }
    }

    private void EnsureIndexWriter()
    {
        if (indexWriter != null) return;

        var indexPath = Path.Combine(ConfigPath.Base, IndexDir);
        SystemDirectory.CreateDirectory(indexPath);
        indexDirectory = FSDirectory.Open(indexPath);
        analyzer = new StandardAnalyzer(LuceneVersion);
        var config = new IndexWriterConfig(LuceneVersion, analyzer) { OpenMode = OpenMode.CREATE };
        indexWriter = new IndexWriter(indexDirectory, config);
    }

    private void ReplaceDirectoryIndex(string directory, bool commit)
    {
        indexWriter!.DeleteDocuments(new Term(RootField, directory));
        if (SystemDirectory.Exists(directory))
        {
            IndexDirectory(directory, indexWriter);
        }
        else
        {
            logger.LogWarning("FileSearcher directory does not exist: {Directory}", directory);
        }
        if (commit) indexWriter.Commit();
    }

    private void IndexDirectory(string rootDirectory, IndexWriter writer)
    {
        var count = 0;
        foreach (var file in EnumerateIndexableFiles(rootDirectory))
        {
            var fileName = Path.GetFileNameWithoutExtension(file);
            writer.AddDocument(new Document
            {
                new StringField(RootField, rootDirectory, Field.Store.NO),
                new StoredField("path", file),
                new StoredField("filename", fileName),
                new StringField("indexedTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), Field.Store.YES),
                new StringField("searchFilename", fileName.ToLowerInvariant(), Field.Store.NO),
                new StringField("searchInitials", StringUtils.GetInitialsFromWords(fileName), Field.Store.NO),
                new TextField("searchPossibles", fileName, Field.Store.NO)
            });
            count++;
        }
        logger.LogInformation("Indexed {FileCount} files under {Directory}.", count, rootDirectory);
    }

    private IEnumerable<string> EnumerateIndexableFiles(string rootDirectory)
    {
        var pending = new Stack<string>();
        pending.Push(rootDirectory);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files;
            try
            {
                files = SystemDirectory.GetFiles(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                logger.LogDebug(ex, "Skipping inaccessible directory {Directory}.", current);
                continue;
            }

            foreach (var file in files)
            {
                if (IsIndexableFile(file)) yield return file;
            }

            string[] directories;
            try
            {
                directories = SystemDirectory.GetDirectories(current);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                logger.LogDebug(ex, "Cannot enumerate child directories of {Directory}.", current);
                continue;
            }

            foreach (var directory in directories)
            {
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0) pending.Push(directory);
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
                InternalBufferSize = 32 * 1024
            };
            watcher.Changed += (_, args) =>
            {
                if (IsIndexableFile(args.FullPath)) ScheduleDirectoryReindex(rootDirectory);
            };
            watcher.Created += (_, args) =>
            {
                if (IsIndexableFile(args.FullPath) || SystemDirectory.Exists(args.FullPath))
                    ScheduleDirectoryReindex(rootDirectory);
            };
            watcher.Deleted += (_, _) => ScheduleDirectoryReindex(rootDirectory);
            watcher.Renamed += (_, _) => ScheduleDirectoryReindex(rootDirectory);
            watcher.Error += (_, args) =>
            {
                logger.LogWarning(args.GetException(),
                    "FileSearcher watcher error for {Directory}; rebuilding that directory.", rootDirectory);
                ScheduleDirectoryReindex(rootDirectory);
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

    private void StopWatching(string rootDirectory)
    {
        lock (watcherLock)
        {
            if (watchers.Remove(rootDirectory, out var watcher)) watcher.Dispose();
            if (pendingReindexes.Remove(rootDirectory, out var pending))
            {
                pending.Cancel();
                pending.Dispose();
            }
        }
    }

    private void ScheduleDirectoryReindex(string rootDirectory)
    {
        CancellationTokenSource pending;
        lock (watcherLock)
        {
            if (disposed || !configuredDirectories.Contains(rootDirectory)) return;
            if (pendingReindexes.Remove(rootDirectory, out var previous))
            {
                previous.Cancel();
                previous.Dispose();
            }
            pending = CancellationTokenSource.CreateLinkedTokenSource(disposeCancellation.Token);
            pendingReindexes[rootDirectory] = pending;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(WatchDebounce, pending.Token);
                lock (watcherLock)
                {
                    if (disposed || !configuredDirectories.Contains(rootDirectory)) return;
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                lock (indexLock)
                {
                    if (indexWriter == null) return;
                    ReplaceDirectoryIndex(rootDirectory, commit: true);
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
                lock (watcherLock)
                {
                    if (pendingReindexes.TryGetValue(rootDirectory, out var current) && ReferenceEquals(current, pending))
                    {
                        pendingReindexes.Remove(rootDirectory);
                        pending.Dispose();
                    }
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
                reader = indexWriter?.GetReader(true)
                         ?? throw new InvalidOperationException("FileSearcher index is not initialized.");
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

    private static JsonElement CreateDefaultSearchDirectories() =>
        JsonSerializer.SerializeToElement(DefaultSearchDirectoryPaths()
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(PathComparer)
            .Select(path => new Dictionary<string, string> { ["Path"] = path }));

    private static IEnumerable<string> DefaultSearchDirectoryPaths()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
        yield return Path.Combine("C:\\Users", Environment.UserName, "OneDrive", "Custom Shortcuts", "Scripts");
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Microsoft", "Windows", "Start Menu", "Programs");
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }

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

    private static bool IsIndexableFile(string path) =>
        path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (configurationRegistry != null) configurationRegistry.ConfigurationChanged -= OnConfigurationChanged;
        disposeCancellation.Cancel();

        lock (watcherLock)
        {
            foreach (var watcher in watchers.Values) watcher.Dispose();
            watchers.Clear();
            foreach (var pending in pendingReindexes.Values)
            {
                pending.Cancel();
                pending.Dispose();
            }
            pendingReindexes.Clear();
            configuredDirectories.Clear();
        }

        lock (indexLock)
        {
            indexWriter?.Dispose();
            analyzer?.Dispose();
            indexDirectory?.Dispose();
            indexWriter = null;
            analyzer = null;
            indexDirectory = null;
        }
        disposeCancellation.Dispose();
        GC.SuppressFinalize(this);
    }
}
