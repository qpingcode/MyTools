using System.IO;
using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Localization;
using MyTools.Common.Plugins;
using MyTools.Plugins.NodePlugins;
using MyTools.Plugins.Param;

namespace MyTools.Plugins;

public sealed class ApplicationsPlugin(
    ILogger<ApplicationsPlugin> logger, IMemoryCache cache, ILocalizationService localization) : PluginBase, IDisposable
{
    public const string SearchScopesKey = "applications.SearchScopes";
    private readonly object scanLock = new();
    private readonly CancellationTokenSource lifetime = new();
    private IConfigurationRegistry? registry;
    private ConfigurationSetting? scopesSetting;
    private ApplicationEntry[] indexedPaths = [];
    private sealed record ApplicationEntry(string Path, string Identity);
    private long revision;
    private bool initialized;
    private bool disposed;

    public override PluginId PluginId => new("applications");
    public override string Name => localization.GetCaption("Plugin.applications.Name", "Applications");
    public override string Description => localization.GetCaption("Plugin.applications.Description", "Search and launch applications");
    public override bool IsGlobalSearchPlugin => true;
    public override List<IActionWithHotkey> Actions =>
    [
        WellKnownActions.Execute.WithDefaultHotkey(),
        WellKnownActions.AdminExecute.WithHotkey(Hotkey.Ctrl(HotkeyKey.Enter)),
        WellKnownActions.OpenInExplorer.WithHotkey(Hotkey.Ctrl(HotkeyKey.O))
    ];

    internal static JsonElement DefaultScopes() => JsonSerializer.SerializeToElement(new[]
    {
        new { Path = @"~\AppData\Roaming\Microsoft\Windows\Start Menu" },
        new { Path = @"~\Desktop" },
        new { Path = @"C:\ProgramData\Microsoft\Windows\Start Menu" }
    });

    protected override void AddPluginSettings(ConfigurationCategory category, IConfigurationRegistry configurationRegistry)
    {
        if (registry != null) registry.ConfigurationChanged -= OnConfigurationChanged;
        registry = configurationRegistry;
        scopesSetting = registry.AddSetting(category, "SearchScopes",
            localization.GetCaption("Plugin.applications.SearchScopes.Title", "Search Scopes"),
            localization.GetCaption("Plugin.applications.SearchScopes.Description",
                "Pick additional directories to be included when searching for applications."),
            DefaultScopes(), new JsonElementSettingSerializer(), valueType: SettingValueTypes.Array);
        scopesSetting.UiHint = "directory-list";
        scopesSetting.Schema = new SettingSchema
        {
            Properties = [new SettingSchemaProperty
            {
                Key = "Path", Type = SchemaPropertyType.Path, UiHint = "directory",
                Title = localization.GetCaption("Plugin.applications.SearchScopes.Directory", "Directory")
            }]
        };
        registry.ConfigurationChanged += OnConfigurationChanged;
    }

    public override Task InitializeAsync()
    {
        initialized = true;
        return RescanAsync(scopesSetting?.CurrentValue ?? DefaultScopes());
    }

    private void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        if (initialized && e.Setting.Key == SearchScopesKey) _ = RescanAsync(e.NewValue);
    }

    internal Task RescanAsync(object? value)
    {
        var scopes = ReadScopes(value);
        long currentRevision;
        CancellationToken token;
        lock (scanLock)
        {
            if (disposed) return Task.CompletedTask;
            currentRevision = ++revision;
            token = lifetime.Token;
            // Removed scopes must stop returning results immediately.
            indexedPaths = [];
        }
        return Task.Run(() =>
        {
            try
            {
                var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var entries = new List<ApplicationEntry>();
                foreach (var scope in scopes)
                {
                    token.ThrowIfCancellationRequested();
                    if (!Directory.Exists(scope)) continue;
                    try
                    {
                        foreach (var path in Directory.EnumerateFiles(scope, "*", new EnumerationOptions
                        {
                            RecurseSubdirectories = true, IgnoreInaccessible = true,
                            AttributesToSkip = FileAttributes.ReparsePoint
                        }))
                        {
                            token.ThrowIfCancellationRequested();
                            lock (scanLock) { if (currentRevision != revision) return; }
                            var fullPath = Path.GetFullPath(path);
                            if (!paths.Add(fullPath)) continue;
                            try
                            {
                                var identity = GetApplicationIdentity(fullPath);
                                if (identity != null) entries.Add(new ApplicationEntry(fullPath, identity));
                            }
                            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                            {
                                logger.LogDebug(ex, "Could not read application {Path}.", fullPath);
                            }
                        }
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        logger.LogWarning(ex, "Could not scan application scope {Scope}.", scope);
                    }
                }
                lock (scanLock)
                {
                    if (!disposed && currentRevision == revision)
                        indexedPaths = entries.OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase).ToArray();
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex) { logger.LogError(ex, "Application scanning failed."); }
        });
    }

    internal static IReadOnlySet<string> ReadScopes(object? value)
    {
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (value is not JsonElement { ValueKind: JsonValueKind.Array } array) return result;
        foreach (var item in array.EnumerateArray())
        {
            var path = item.ValueKind == JsonValueKind.String ? item.GetString()
                : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("Path", out var p)
                    && p.ValueKind == JsonValueKind.String ? p.GetString() : null;
            if (string.IsNullOrWhiteSpace(path)) continue;
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (path == "~" || path.StartsWith(@"~\") || path.StartsWith("~/"))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path.Length > 1 ? path[2..] : "");
            try
            {
                if (Path.IsPathFullyQualified(path)) result.Add(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { }
        }
        return result;
    }

    private static string? GetApplicationIdentity(string path)
    {
        var extension = Path.GetExtension(path);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".com", StringComparison.OrdinalIgnoreCase)) return LaunchIdentity(path, string.Empty);
        if (extension.Equals(".appref-ms", StringComparison.OrdinalIgnoreCase))
            return FileContentIdentity(path);
        if (!extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase)) return null;
        try
        {
            var launch = LnkParser.ReadApplicationLaunch(path);
            if (launch == null) return null;
            var target = launch.Value.Target;
            // Preserve opaque shell shortcuts; exact copies can still be merged.
            if (string.IsNullOrWhiteSpace(target)) return FileContentIdentity(path);
            target = Environment.ExpandEnvironmentVariables(target);
            return !Directory.Exists(target)
                && (target.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    || target.EndsWith(".com", StringComparison.OrdinalIgnoreCase)
                    || target.EndsWith(".appref-ms", StringComparison.OrdinalIgnoreCase))
                ? LaunchIdentity(Path.GetFullPath(target, Path.GetDirectoryName(path)!), launch.Value.Arguments)
                : null;
        }
        catch { return null; }
    }

    private static string LaunchIdentity(string target, string arguments) =>
        JsonSerializer.Serialize(new[] { Path.GetFullPath(target).ToUpperInvariant(), arguments.Trim() });

    private static string FileContentIdentity(string path)
    {
        using var stream = File.OpenRead(path);
        // Empty/invalid launch files must not collapse unrelated applications.
        return stream.Length == 0 ? LaunchIdentity(path, string.Empty)
            : Path.GetExtension(path).ToUpperInvariant() + ":" + Convert.ToHexString(SHA256.HashData(stream));
    }

    public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationEntry[] paths;
        lock (scanLock) paths = indexedPaths;
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var matches = paths.Where(entry => terms.All(term => Path.GetFileNameWithoutExtension(entry.Path)
                .Contains(term, StringComparison.OrdinalIgnoreCase)))
            .Where(entry => File.Exists(entry.Path))
            .OrderByDescending(entry => Path.GetFileNameWithoutExtension(entry.Path).StartsWith(query.Trim(), StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(entry => Path.GetExtension(entry.Path).Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            .DistinctBy(entry => entry.Identity, StringComparer.Ordinal)
            .Take(50);
        var items = new List<ResultItem>();
        foreach (var match in matches)
        {
            var path = match.Path;
            cancellationToken.ThrowIfCancellationRequested();
            var icon = cache.GetOrCreate<Icon>("Applications.Icon:" + path, entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
                try
                {
                    var data = FileIconHelper.GetFileIconData(path);
                    if (data != null) return new ImageIcon(data);
                }
                catch (Exception ex) { logger.LogDebug(ex, "Could not load application icon {Path}.", path); }
                return new MdiIcon("mdi-application-outline");
            })!;
            items.Add(new ResultItem(
                icon,
                Path.GetFileNameWithoutExtension(path),
                path,
                ActionStringParam.From(path),
                ResultItemPriorities.Highest)
            {
                ResultKey = match.Identity, AllowedActions = Actions
            });
        }
        return Task.FromResult(new Result(true, null, items));
    }

    public void Dispose()
    {
        lock (scanLock)
        {
            if (disposed) return;
            disposed = true;
            lifetime.Cancel();
            indexedPaths = [];
        }
        if (registry != null) registry.ConfigurationChanged -= OnConfigurationChanged;
        lifetime.Dispose();
    }
}
