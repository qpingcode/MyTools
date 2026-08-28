using System.Collections.Concurrent;
using System.ComponentModel;
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
    private static readonly Regex ValidSessionId = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);
    private static readonly Regex ValidSkillName = new("^[a-zA-Z0-9_.-]{1,100}$", RegexOptions.Compiled);
    private readonly ConcurrentDictionary<string, Conversation> conversations = new();

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
        ValidateSessionId(sessionId);
        var model = ResolveModel(requestedModel);
        return conversations.GetOrAdd(sessionId, _ => new Conversation(sessionId, model)).Snapshot();
    }

    public async Task<ChatAgentState> ChatAsync(
        ChatAgentRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateSessionId(request.SessionId);
        var availability = GetAvailability();
        if (!availability.Available) throw new InvalidOperationException(availability.UnavailableReason);

        var message = request.Message.Trim();
        if (message.Length == 0) throw new InvalidOperationException("Enter a message.");
        var model = ResolveModel(request.Model);
        var conversation = conversations.GetOrAdd(request.SessionId, _ => new Conversation(request.SessionId, model));
        await conversation.Gate.WaitAsync(cancellationToken);
        CancellationTokenSource? activeCancellation = null;
        try
        {
            activeCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            conversation.BeginTurn(message, model, activeCancellation);

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
        }
        return conversation.Snapshot();
    }

    public bool Cancel(string sessionId)
    {
        ValidateSessionId(sessionId);
        return conversations.TryGetValue(sessionId, out var conversation) && conversation.CancelActiveRequest();
    }

    public void Clear(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId)) return;
        ValidateSessionId(sessionId);
        if (conversations.TryRemove(sessionId, out var conversation)) conversation.CancelActiveRequest();
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

    private sealed class Conversation(string sessionId, string selectedModel)
    {
        private readonly object sync = new();
        private readonly List<ChatAgentMessage> messages = [];
        private CancellationTokenSource? activeRequest;
        private bool streaming;
        private bool cancelled;
        private string error = "";
        private string selectedModel = selectedModel;
        public List<Microsoft.Extensions.AI.ChatMessage> AgentMessages { get; } = [];
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void BeginTurn(string message, string model, CancellationTokenSource cancellation)
        {
            lock (sync)
            {
                selectedModel = model;
                activeRequest = cancellation;
                streaming = true;
                cancelled = false;
                error = "";
                messages.Add(new ChatAgentMessage("user", message, DateTimeOffset.UtcNow.ToString("O")));
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
                streaming = false;
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
