namespace MyTools.AI;

public sealed record PluginCreationContext(
    string RepositoryRoot,
    string ExamplesRoot,
    string PluginsRoot,
    string CodingRoot,
    string ConfigurationRoot,
    string SkillPath,
    IReadOnlyCollection<ExistingPlugin> ExistingPlugins,
    IHostCityProvider? HostCityProvider = null,
    string? ReferenceRoot = null,
    IPluginDevelopmentDiagnostics? DevelopmentDiagnostics = null,
    IPluginCreationProxyProvider? ProxyProvider = null);

public interface IPluginCreationProxyProvider
{
    PluginCreationProxySettings GetProxySettings();
}

public sealed record PluginCreationProxySettings(Uri? ProxyUri, string Source);

public interface IPluginDevelopmentDiagnostics
{
    Task<PluginWatchStartResult> StartPluginWatchAsync(
        string pluginId,
        CancellationToken cancellationToken = default);

    PluginWatchLogResult GetPluginWatchLogs(string pluginId, int count);

    SystemLogResult GetSystemLogs(int count);
}

public sealed record PluginWatchStartResult(
    string PluginId,
    bool Running,
    bool Started,
    string SourcePath);

public sealed record PluginWatchLogResult(
    string PluginId,
    bool Running,
    IReadOnlyList<string> Lines);

public sealed record SystemLogResult(IReadOnlyList<string> Lines);

public interface IHostCityProvider
{
    Task<HostCityResult> GetCityAsync(CancellationToken cancellationToken = default);
}

public sealed record HostCityResult(
    bool Available,
    string? City,
    string? Region,
    string? Country,
    string? CountryCode,
    bool Approximate,
    string Source,
    string? Error = null);

public sealed record ExistingPlugin(string Id, string Name);

public sealed record SelectedPluginContext(
    string Id,
    string Name,
    string PluginType,
    string SourcePath,
    string DistPath);

public sealed record AiAvailability(
    bool Available,
    string Provider,
    string Model,
    string RequiredEnvironmentVariable,
    string? UnavailableReason);

public sealed record PluginCreationChatRequest(
    string? SessionId,
    string Message,
    SelectedPluginContext? SelectedPlugin = null);

public sealed record PluginCreationChatResponse(
    string SessionId,
    string Reply,
    CreatedPluginArtifact? CreatedPlugin,
    PluginSetupResult? Setup = null,
    bool Stopped = false);

public sealed record AiProgressEvent(long Sequence, string Kind, string? Detail);

public sealed record AiProgressBatch(IReadOnlyList<AiProgressEvent> Events);

public sealed record PluginSetupResult(bool Installed, bool WatchStarted, string? Error);

public sealed record CreatedPluginArtifact(
    string PluginId,
    string Name,
    string PluginType,
    string SourcePath,
    string DistPath,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string> HotKeys,
    IReadOnlyList<string> TestSteps,
    bool IsUpdate = false);
