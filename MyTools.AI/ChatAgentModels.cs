namespace MyTools.AI;

public sealed record ChatAgentContext(
    string RepositoryRoot,
    IReadOnlyCollection<string> SkillRoots,
    IPluginCreationProxyProvider? ProxyProvider = null);

public sealed record ChatModelAvailability(
    bool Available,
    string Provider,
    string SelectedModel,
    IReadOnlyList<string> Models,
    string RequiredEnvironmentVariable,
    string? UnavailableReason);

public sealed record ChatAgentRequest(string SessionId, string Message, string Model);

public sealed record ChatTokenUsage(long InputTokens, long OutputTokens, long TotalTokens);

public sealed record ChatAgentMessage(
    string Role,
    string Content,
    string CreatedAt,
    ChatTokenUsage? Usage = null);

public sealed record ChatAgentState(
    string SessionId,
    IReadOnlyList<ChatAgentMessage> Messages,
    string SelectedModel,
    bool Streaming,
    bool Cancelled,
    string Error);
