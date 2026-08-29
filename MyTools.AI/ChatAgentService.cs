using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using OpenAI;
using System.ClientModel;
using System.ClientModel.Primitives;
using OpenAIChatClient = OpenAI.Chat.ChatClient;

namespace MyTools.AI;

/// <summary>
/// Host-owned general chat agent. It shares the same DeepSeek/OpenAI-compatible
/// stack and proxy settings as the plugin creation agent, while exposing only
/// read-only web and skill tools.
/// </summary>
public sealed class ChatAgentService(ChatAgentContext context, ILogger<ChatAgentService>? logger = null) : IDisposable
{
    private const int MaxHistoryMessages = 24;
    private const int MaxStoredConversations = 50;
    private static readonly Regex ValidSessionId = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex ValidSkillName = new("^[a-zA-Z0-9_.-]{1,100}$", RegexOptions.Compiled);
    private static readonly Regex ValidInteractionId = new("^[a-zA-Z0-9_.-]{1,64}$", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, Conversation> conversations = new();
    private readonly object historySync = new();
    private volatile bool historyLoaded;

    public ChatModelAvailability GetAvailability()
    {
        var configuredModel = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL")?.Trim();
        var models = (Environment.GetEnvironmentVariable("DEEPSEEK_MODELS") ?? "")
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Prepend(string.IsNullOrWhiteSpace(configuredModel) ? "deepseek-chat" : configuredModel)
            .Append("deepseek-chat")
            .Append("deepseek-reasoner")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var available = !string.IsNullOrWhiteSpace(
            Environment.GetEnvironmentVariable(PluginCreationAgentService.ApiKeyEnvironmentVariable));
        return new ChatModelAvailability(
            available,
            "DeepSeek",
            models[0],
            models,
            PluginCreationAgentService.ApiKeyEnvironmentVariable,
            available ? null : $"Missing {PluginCreationAgentService.ApiKeyEnvironmentVariable} environment variable.");
    }

    public ChatAgentState GetState(string sessionId, string? requestedModel = null)
    {
        EnsureHistoryLoaded();
        ValidateSessionId(sessionId);
        var model = ResolveModel(requestedModel);
        return conversations.GetOrAdd(sessionId, _ => new Conversation(sessionId, model)).Snapshot();
    }

    public async Task<ChatAgentState> ChatAsync(
        ChatAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHistoryLoaded();
        ValidateSessionId(request.SessionId);
        var availability = GetAvailability();
        if (!availability.Available) throw new InvalidOperationException(availability.UnavailableReason);

        var message = request.Message.Trim();
        if (message.Length == 0) throw new InvalidOperationException("Enter a message.");
        ValidateInteractionResponse(request.InteractionResponse);
        var model = ResolveModel(request.Model);
        var conversation = conversations.GetOrAdd(request.SessionId, _ => new Conversation(request.SessionId, model));
        await conversation.Gate.WaitAsync(cancellationToken);
        CancellationTokenSource? activeCancellation = null;
        try
        {
            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            conversation.BeginTurn(message, model, activeCancellation, request.InteractionResponse);

            var route = ResolveNetworkRoute();
            using var aiHttpClient = CreateHttpClient(route, TimeSpan.FromMinutes(10));
            using var toolHttpClient = CreateHttpClient(route, TimeSpan.FromSeconds(20));
            var tools = new ReadOnlyChatTools(context, toolHttpClient);
            var agent = CreateAgent(model, aiHttpClient, tools);
            var reply = "";
            var inputTokens = 0L;
            var outputTokens = 0L;
            var totalTokens = 0L;
            var hasUsage = false;
            await foreach (var update in agent.RunStreamingAsync(
                               conversation.AgentMessages,
                               cancellationToken: activeCancellation.Token))
            {
                foreach (var usage in update.Contents.OfType<UsageContent>())
                {
                    hasUsage = true;
                    inputTokens += usage.Details.InputTokenCount ?? 0;
                    outputTokens += usage.Details.OutputTokenCount ?? 0;
                    totalTokens += usage.Details.TotalTokenCount ?? 0;
                }
                if (string.IsNullOrEmpty(update.Text)) continue;
                reply += update.Text;
                conversation.AppendAssistantDelta(update.Text);
            }
            conversation.CompleteTurn(
                reply,
                hasUsage ? new ChatTokenUsage(inputTokens, outputTokens, totalTokens) : null);
        }
        catch (OperationCanceledException) when (activeCancellation?.IsCancellationRequested == true)
        {
            conversation.CancelTurn();
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Chat AI failed for session {SessionId} model {Model}", request.SessionId, model);
            conversation.FailTurn(ex.Message);
        }
        finally
        {
            conversation.EndTurn(activeCancellation);
            conversation.Gate.Release();
            SaveHistory();
        }
        return conversation.Snapshot();
    }

    public bool Cancel(string sessionId)
    {
        EnsureHistoryLoaded();
        ValidateSessionId(sessionId);
        return conversations.TryGetValue(sessionId, out var conversation) && conversation.CancelActiveRequest();
    }

    public void Clear(string? sessionId)
    {
        EnsureHistoryLoaded();
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        ValidateSessionId(sessionId);
        if (conversations.TryRemove(sessionId, out var conversation))
        {
            conversation.CancelActiveRequest();
            SaveHistory();
        }
    }

    public IReadOnlyList<ChatConversationSummary> ListConversations()
    {
        EnsureHistoryLoaded();
        return conversations.Values
            .Select(item => item.Summary())
            .Where(item => item is not null)
            .Cast<ChatConversationSummary>()
            .OrderByDescending(item => item.UpdatedAt)
            .Take(MaxStoredConversations)
            .ToArray();
    }

    private void EnsureHistoryLoaded()
    {
        if (historyLoaded) return;
        lock (historySync)
        {
            if (historyLoaded) return;
            historyLoaded = true;
            var path = context.ConversationHistoryPath;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
            try
            {
                var stored = JsonSerializer.Deserialize<StoredConversation[]>(File.ReadAllText(path)) ?? [];
                foreach (var item in stored.Take(MaxStoredConversations))
                {
                    if (!ValidSessionId.IsMatch(item.SessionId) || item.Messages.Count == 0) continue;
                    conversations[item.SessionId] = Conversation.FromStored(item);
                }
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not load chat conversation history from {Path}", path);
            }
        }
    }

    private void SaveHistory()
    {
        var path = context.ConversationHistoryPath;
        if (string.IsNullOrWhiteSpace(path)) return;
        lock (historySync)
        {
            try
            {
                var stored = conversations.Values
                    .Select(item => item.StoredSnapshot())
                    .Where(item => item is not null)
                    .Cast<StoredConversation>()
                    .OrderByDescending(item => item.UpdatedAt)
                    .Take(MaxStoredConversations)
                    .ToArray();
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllText(path, JsonSerializer.Serialize(stored));
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "Could not save chat conversation history to {Path}", path);
            }
        }
    }

    private ChatClientAgent CreateAgent(string model, HttpClient httpClient, ReadOnlyChatTools run)
    {
        var configuredEndpoint = Environment.GetEnvironmentVariable("DEEPSEEK_API_URL")
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL")
            ?? "https://api.deepseek.com";
        var endpoint = configuredEndpoint.TrimEnd('/');
        if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
            endpoint = endpoint[..^"/chat/completions".Length];

        var chatClient = new OpenAIChatClient(
                model,
                new ApiKeyCredential(Environment.GetEnvironmentVariable(
                    PluginCreationAgentService.ApiKeyEnvironmentVariable)!),
                new OpenAIClientOptions
                {
                    Endpoint = new Uri(endpoint),
                    Transport = new HttpClientPipelineTransport(httpClient)
                })
            .AsIChatClient();

        AITool[] tools =
        [
            AIFunctionFactory.Create(run.SearchWebAsync, "search_web"),
            AIFunctionFactory.Create(run.FetchUrlAsync, "fetch_url"),
            AIFunctionFactory.Create(run.ListSkills, "list_skills"),
            AIFunctionFactory.Create(run.ReadSkillFile, "read_skill_file")
        ];
        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "mytools_chat",
            Description = "General-purpose MyTools chat with read-only web and system skill access.",
            ChatOptions = new ChatOptions
            {
                Instructions = """
                    You are the MyTools assistant. Answer the user's request directly and use Markdown when it improves clarity.
                    Use search_web and fetch_url when current public information is needed. Use list_skills and read_skill_file
                    when a system skill is relevant; follow the skill instructions you read. Tool access is read-only. Never claim
                    to have changed files or external state.

                    When the user must choose among options before you can continue, end the response with exactly one fenced
                    `mytools-interaction` JSON block. Keep any explanation before the block. Use this schema:
                    ```mytools-interaction
                    {
                      "version": 1,
                      "id": "stable_interaction_id",
                      "title": "Optional short heading",
                      "questions": [
                        {
                          "id": "stable_id",
                          "prompt": "Question shown to the user",
                          "options": ["First choice", "Second choice"],
                          "multiple": false,
                          "allowText": true,
                          "textPlaceholder": "Enter another answer"
                        }
                      ]
                    }
                    ```
                    Give every interaction and question a short unique ASCII id. `questions` may contain several questions
                    and the UI will paginate them. `multiple` defaults to false,
                    `allowText` defaults to false, and either options or free text may be used. Do not use this block for
                    rhetorical questions or when a normal text response is sufficient. Never put Markdown inside JSON strings.
                    """,
                Tools = tools
            }
        });
    }

    private string ResolveModel(string? requestedModel)
    {
        var availability = GetAvailability();
        var model = string.IsNullOrWhiteSpace(requestedModel) ? availability.SelectedModel : requestedModel.Trim();
        return availability.Models.FirstOrDefault(item => string.Equals(item, model, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Unsupported DeepSeek model: {model}");
    }

    private NetworkRoute ResolveNetworkRoute()
    {
        if (context.ProxyProvider is null) return new NetworkRoute(null, true);
        var configured = context.ProxyProvider.GetProxySettings();
        return new NetworkRoute(configured.ProxyUri, false);
    }

    private static HttpClient CreateHttpClient(NetworkRoute route, TimeSpan timeout)
    {
        var handler = new HttpClientHandler();
        if (!route.UseSystemProxy)
        {
            handler.UseProxy = route.ProxyUri is not null;
            handler.Proxy = route.ProxyUri is null ? null : new WebProxy(route.ProxyUri);
        }
        return new HttpClient(handler, disposeHandler: true) { Timeout = timeout };
    }

    private static void ValidateSessionId(string sessionId)
    {
        if (!ValidSessionId.IsMatch(sessionId)) throw new InvalidOperationException("Invalid chat session ID.");
    }

    public void Dispose()
    {
        foreach (var conversation in conversations.Values) conversation.CancelActiveRequest();
        conversations.Clear();
    }

    private sealed record NetworkRoute(Uri? ProxyUri, bool UseSystemProxy);

    private sealed record StoredConversation(
        string SessionId,
        string SelectedModel,
        IReadOnlyList<ChatAgentMessage> Messages,
        DateTimeOffset UpdatedAt);

    private sealed class Conversation(string sessionId, string selectedModel)
    {
        private readonly object sync = new();
        private readonly List<ChatAgentMessage> messages = [];
        private CancellationTokenSource? activeRequest;
        private bool streaming;
        private bool cancelled;
        private string error = "";
        private string selectedModel = selectedModel;
        private long turnStartedTimestamp;
        private DateTimeOffset updatedAt = DateTimeOffset.UtcNow;
        public List<Microsoft.Extensions.AI.ChatMessage> AgentMessages { get; } = [];
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void BeginTurn(
            string message,
            string model,
            CancellationTokenSource cancellation,
            ChatInteractionResponse? interactionResponse)
        {
            lock (sync)
            {
                selectedModel = model;
                activeRequest = cancellation;
                streaming = true;
                cancelled = false;
                error = "";
                turnStartedTimestamp = Stopwatch.GetTimestamp();
                updatedAt = DateTimeOffset.UtcNow;
                messages.Add(new ChatAgentMessage(
                    "user",
                    message,
                    DateTimeOffset.UtcNow.ToString("O"),
                    InteractionResponse: interactionResponse));
                messages.Add(new ChatAgentMessage("assistant", "", DateTimeOffset.UtcNow.ToString("O")));
                AgentMessages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.User, message));
                if (AgentMessages.Count > MaxHistoryMessages)
                    AgentMessages.RemoveRange(0, AgentMessages.Count - MaxHistoryMessages);
            }
        }

        public void AppendAssistantDelta(string delta)
        {
            lock (sync)
            {
                var current = messages[^1];
                messages[^1] = current with { Content = current.Content + delta };
            }
        }

        public void CompleteTurn(string reply, ChatTokenUsage? usage)
        {
            lock (sync)
            {
                var finalReply = string.IsNullOrWhiteSpace(reply)
                    ? "I could not produce a response. Please try again."
                    : reply.Trim();
                messages[^1] = messages[^1] with { Content = finalReply, Usage = usage };
                AgentMessages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, finalReply));
            }
        }

        public void CancelTurn()
        {
            lock (sync)
            {
                cancelled = true;
                var partial = messages.Count > 0 && messages[^1].Role == "assistant" ? messages[^1].Content : "";
                if (!string.IsNullOrWhiteSpace(partial))
                    AgentMessages.Add(new Microsoft.Extensions.AI.ChatMessage(ChatRole.Assistant, partial));
                else if (messages.Count > 0 && messages[^1].Role == "assistant")
                    messages.RemoveAt(messages.Count - 1);
            }
        }

        public void FailTurn(string message)
        {
            lock (sync)
            {
                error = message;
                if (messages.Count > 0 && messages[^1].Role == "assistant" && messages[^1].Content.Length == 0)
                    messages.RemoveAt(messages.Count - 1);
            }
        }

        public void EndTurn(CancellationTokenSource? cancellation)
        {
            lock (sync)
            {
                if (turnStartedTimestamp != 0 && messages.Count > 0 && messages[^1].Role == "assistant")
                {
                    var duration = Stopwatch.GetElapsedTime(turnStartedTimestamp);
                    messages[^1] = messages[^1] with
                    {
                        DurationMilliseconds = Math.Max(1L, (long)Math.Round(duration.TotalMilliseconds))
                    };
                }
                turnStartedTimestamp = 0;
                streaming = false;
                updatedAt = DateTimeOffset.UtcNow;
                if (ReferenceEquals(activeRequest, cancellation)) activeRequest = null;
            }
            cancellation?.Dispose();
        }

        public bool CancelActiveRequest()
        {
            lock (sync)
            {
                if (activeRequest is null || activeRequest.IsCancellationRequested) return false;
                activeRequest.Cancel();
                return true;
            }
        }

        public ChatAgentState Snapshot()
        {
            lock (sync)
                return new ChatAgentState(sessionId, messages.ToArray(), selectedModel, streaming, cancelled, error);
        }

        public ChatConversationSummary? Summary()
        {
            lock (sync)
            {
                var firstUserMessage = messages.FirstOrDefault(item => item.Role == "user")?.Content;
                if (string.IsNullOrWhiteSpace(firstUserMessage)) return null;
                var title = Regex.Replace(firstUserMessage.Trim(), "\\s+", " ");
                if (title.Length > 42) title = title[..42].TrimEnd() + "…";
                return new ChatConversationSummary(sessionId, title, updatedAt);
            }
        }

        public StoredConversation? StoredSnapshot()
        {
            lock (sync)
            {
                if (messages.All(item => item.Role != "user")) return null;
                return new StoredConversation(sessionId, selectedModel, messages.ToArray(), updatedAt);
            }
        }

        public static Conversation FromStored(StoredConversation stored)
        {
            var conversation = new Conversation(stored.SessionId, stored.SelectedModel);
            lock (conversation.sync)
            {
                conversation.messages.AddRange(stored.Messages);
                conversation.updatedAt = stored.UpdatedAt;
                foreach (var message in stored.Messages.TakeLast(MaxHistoryMessages))
                {
                    var role = message.Role == "user" ? ChatRole.User : ChatRole.Assistant;
                    if (!string.IsNullOrWhiteSpace(message.Content))
                        conversation.AgentMessages.Add(new Microsoft.Extensions.AI.ChatMessage(role, message.Content));
                }
            }
            return conversation;
        }
    }

    private static void ValidateInteractionResponse(ChatInteractionResponse? response)
    {
        if (response is null) return;
        if (string.IsNullOrWhiteSpace(response.InteractionId)
            || !ValidInteractionId.IsMatch(response.InteractionId)
            || response.Answers is null
            || response.Answers.Count is < 1 or > 12)
            throw new InvalidOperationException("Invalid chat interaction response.");
        foreach (var answer in response.Answers)
        {
            if (string.IsNullOrWhiteSpace(answer.QuestionId)
                || !ValidInteractionId.IsMatch(answer.QuestionId)
                || string.IsNullOrWhiteSpace(answer.Prompt)
                || answer.Prompt.Length > 500
                || answer.Text is null
                || answer.Text.Length > 1000
                || answer.Values is null
                || answer.Values.Count > 12
                || answer.Values.Any(value => string.IsNullOrWhiteSpace(value) || value.Length > 120))
                throw new InvalidOperationException("Invalid chat interaction answer.");
        }
    }

    private sealed class ReadOnlyChatTools(ChatAgentContext context, HttpClient httpClient)
    {
        private readonly AgentWebTools webTools = new(httpClient);

        [Description("Searches the public web when current information is needed. Returns a compact plain-text result set.")]
        public Task<string> SearchWebAsync(
            [Description("Search query.")] string query,
            CancellationToken cancellationToken = default) =>
            webTools.SearchWebAsync(query, cancellationToken);

        [Description("Fetches a public HTTPS page. Private and local network targets are rejected.")]
        public Task<string> FetchUrlAsync(
            [Description("Public HTTPS URL.")] string url,
            CancellationToken cancellationToken = default) =>
            webTools.FetchUrlAsync(url, cancellationToken);

        [Description("Lists system skills available to MyTools chat.")]
        public string ListSkills()
        {
            var skills = SkillDirectories()
                .Select(item => new { name = Path.GetFileName(item), path = item })
                .DistinctBy(item => item.name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            return JsonSerializer.Serialize(skills);
        }

        [Description("Reads SKILL.md or a referenced text resource from one available system skill.")]
        public string ReadSkillFile(
            [Description("Skill directory name returned by list_skills.")] string skillName,
            [Description("Path inside the skill directory, normally SKILL.md.")] string relativePath = "SKILL.md")
        {
            if (!ValidSkillName.IsMatch(skillName)) return "Invalid skill name.";
            var skillRoot = SkillDirectories().FirstOrDefault(path =>
                string.Equals(Path.GetFileName(path), skillName, StringComparison.OrdinalIgnoreCase));
            if (skillRoot is null) return "Skill was not found.";
            var root = Path.GetFullPath(skillRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var file = Path.GetFullPath(Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(file, root, StringComparison.OrdinalIgnoreCase))
                return "Path escapes the skill directory.";
            if (!File.Exists(file)) return "Skill file was not found.";
            var info = new FileInfo(file);
            if (info.Length > 512 * 1024) return "Skill file is too large to read.";
            return File.ReadAllText(file);
        }

        private IEnumerable<string> SkillDirectories() => context.SkillRoots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateDirectories(root))
            .Where(directory => File.Exists(Path.Combine(directory, "SKILL.md")));

    }
}
