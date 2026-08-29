namespace MyTools.AI;

public sealed record ChatAgentContext(
    string RepositoryRoot,
    IReadOnlyCollection<string> SkillRoots,
    IPluginCreationProxyProvider? ProxyProvider = null,
    string? ConversationHistoryPath = null);

public sealed record ChatModelAvailability(
    bool Available,
    string Provider,
    string SelectedModel,
    IReadOnlyList<string> Models,
    string RequiredEnvironmentVariable,
    string? UnavailableReason);

public sealed record ChatInteractionAnswer(
    string QuestionId,
    string Prompt,
    IReadOnlyList<string> Values,
    string Text);

public sealed record ChatInteractionResponse(
    string InteractionId,
    IReadOnlyList<ChatInteractionAnswer> Answers);

public sealed record ChatAgentRequest(
    string SessionId,
    string Message,
    string Model,
    ChatInteractionResponse? InteractionResponse = null);

public sealed record ChatTokenUsage(long InputTokens, long OutputTokens, long TotalTokens);

public sealed record ChatAgentMessage(
    string Role,
    string Content,
    string CreatedAt,
    ChatTokenUsage? Usage = null,
    long? DurationMilliseconds = null,
    ChatInteractionResponse? InteractionResponse = null);

public sealed record ChatAgentState(
    string SessionId,
    IReadOnlyList<ChatAgentMessage> Messages,
    string SelectedModel,
    bool Streaming,
    bool Cancelled,
    string Error);

public sealed record ChatConversationSummary(
    string SessionId,
    string Title,
    DateTimeOffset UpdatedAt);
