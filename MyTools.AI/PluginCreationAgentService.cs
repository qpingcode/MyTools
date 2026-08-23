using System.Collections.Concurrent;
using System.ComponentModel;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Agents.AI;
using Microsoft.Extensions.AI;
using OpenAI;
using System.ClientModel;
using OpenAIChatClient = OpenAI.Chat.ChatClient;

namespace MyTools.AI;

/// <summary>
/// Host-owned AI agent for creating MyTools development plugins. File writes are
/// restricted to one direct child of the configured coding root.
/// </summary>
public sealed class PluginCreationAgentService : IDisposable
{
    public const string ApiKeyEnvironmentVariable = "DEEPSEEK_API_KEY";
    private const int MaxHistoryMessages = 24;
    private static readonly Regex ValidPluginId = new(
        "^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$", RegexOptions.Compiled);
    private static readonly Regex ValidSessionId = new("^[a-zA-Z0-9_-]{1,64}$", RegexOptions.Compiled);
    private readonly PluginCreationContext context;
    private readonly ConcurrentDictionary<string, ExistingPlugin> existingPlugins;
    private readonly ConcurrentDictionary<string, Conversation> conversations = new();
    private readonly HttpClient httpClient = new() { Timeout = TimeSpan.FromSeconds(20) };

    public PluginCreationAgentService(PluginCreationContext context)
    {
        this.context = context;
        existingPlugins = new ConcurrentDictionary<string, ExistingPlugin>(
            context.ExistingPlugins
                .DistinctBy(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(plugin => plugin.Id, StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);
    }

    public AiAvailability GetAvailability()
    {
        var model = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL") ?? "deepseek-v4-flash";
        var available = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable));
        return new AiAvailability(
            available,
            "DeepSeek",
            model,
            ApiKeyEnvironmentVariable,
            available ? null : $"Missing {ApiKeyEnvironmentVariable} environment variable.");
    }

    public async Task<PluginCreationChatResponse> ChatAsync(
        PluginCreationChatRequest request,
        CancellationToken cancellationToken = default)
    {
        var availability = GetAvailability();
        if (!availability.Available)
        {
            throw new InvalidOperationException(availability.UnavailableReason);
        }

        var message = request.Message.Trim();
        if (message.Length == 0) throw new InvalidOperationException("Describe the plugin you want to create.");

        var sessionId = string.IsNullOrWhiteSpace(request.SessionId)
            ? Guid.NewGuid().ToString("N")
            : request.SessionId.Trim();
        if (!ValidSessionId.IsMatch(sessionId)) throw new InvalidOperationException("Invalid AI session ID.");
        var conversation = conversations.GetOrAdd(sessionId, _ => new Conversation());
        await conversation.Gate.WaitAsync(cancellationToken);
        try
        {
            conversation.Messages.Add(new ChatMessage(ChatRole.User, message));
            if (conversation.Messages.Count > MaxHistoryMessages)
            {
                conversation.Messages.RemoveRange(0, conversation.Messages.Count - MaxHistoryMessages);
            }

            var currentContext = context with { ExistingPlugins = existingPlugins.Values.ToArray() };
            var run = new ToolRunState(currentContext, request.SelectedPlugin, httpClient, conversation.Report);
            var agent = CreateAgent(run, availability.Model);
            conversation.Report("thinking", null);
            var replyBuilder = new StringBuilder();
            await foreach (var update in agent.RunStreamingAsync(
                               conversation.Messages, cancellationToken: cancellationToken))
            {
                if (string.IsNullOrEmpty(update.Text)) continue;
                replyBuilder.Append(update.Text);
                conversation.Report("responseDelta", update.Text);
            }
            var reply = string.IsNullOrWhiteSpace(replyBuilder.ToString())
                ? "I could not produce a response. Please add more detail and try again."
                : replyBuilder.ToString().Trim();
            conversation.Messages.Add(new ChatMessage(ChatRole.Assistant, reply));
            conversation.Report("responseComplete", null);
            return new PluginCreationChatResponse(sessionId, reply, run.CreatedPlugin);
        }
        catch (Exception ex)
        {
            conversation.Report("failed", ex.Message);
            throw;
        }
        finally
        {
            conversation.Gate.Release();
        }
    }

    public void ClearConversation(string? sessionId)
    {
        if (!string.IsNullOrWhiteSpace(sessionId) && conversations.TryRemove(sessionId, out var conversation))
        {
            conversation.Dispose();
        }
    }

    public void MarkPluginRegistered(string pluginId, string name)
    {
        existingPlugins[pluginId] = new ExistingPlugin(pluginId, name);
    }

    public void ForgetPlugin(string pluginId)
    {
        existingPlugins.TryRemove(pluginId, out _);
    }

    public void ReportProgress(string sessionId, string kind, string? detail = null)
    {
        if (conversations.TryGetValue(sessionId, out var conversation)) conversation.Report(kind, detail);
    }

    public async Task<AiProgressBatch> GetProgressAsync(
        string sessionId,
        long afterSequence,
        CancellationToken cancellationToken = default)
    {
        if (!ValidSessionId.IsMatch(sessionId)) throw new InvalidOperationException("Invalid AI session ID.");
        var conversation = conversations.GetOrAdd(sessionId, _ => new Conversation());
        return new AiProgressBatch(await conversation.WaitForProgressAsync(afterSequence, cancellationToken));
    }

    private ChatClientAgent CreateAgent(ToolRunState run, string model)
    {
        var apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable)!;
        var configuredEndpoint = Environment.GetEnvironmentVariable("DEEPSEEK_API_URL")
            ?? Environment.GetEnvironmentVariable("DEEPSEEK_BASE_URL")
            ?? "https://api.deepseek.com";
        var endpoint = configuredEndpoint.TrimEnd('/');
        if (endpoint.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
        {
            endpoint = endpoint[..^"/chat/completions".Length];
        }

        var chatClient = new OpenAIChatClient(
                model,
                new ApiKeyCredential(apiKey),
                new OpenAIClientOptions { Endpoint = new Uri(endpoint) })
            .AsIChatClient();

        AITool[] tools =
        [
            AIFunctionFactory.Create(run.GetMyToolsContext, "get_mytools_context"),
            AIFunctionFactory.Create(run.GetHostCityAsync, "get_host_city"),
            AIFunctionFactory.Create(run.ListFiles, "list_files"),
            AIFunctionFactory.Create(run.ReadFile, "read_file"),
            AIFunctionFactory.Create(run.WritePluginFile, "write_plugin_file"),
            AIFunctionFactory.Create(run.StartPluginWatchAsync, "start_plugin_watch"),
            AIFunctionFactory.Create(run.GetPluginWatchLogs, "get_plugin_watch_logs"),
            AIFunctionFactory.Create(run.GetMyToolsLogs, "get_mytools_logs"),
            AIFunctionFactory.Create(run.SearchWebAsync, "search_web"),
            AIFunctionFactory.Create(run.FetchUrlAsync, "fetch_url"),
            AIFunctionFactory.Create(run.CompletePlugin, "complete_plugin")
        ];

        return new ChatClientAgent(chatClient, new ChatClientAgentOptions
        {
            Name = "mytools_plugin_creator",
            Description = "Creates MyTools v3 plugins in the host development workspace.",
            ChatOptions = new ChatOptions
            {
                Instructions = BuildInstructions(run),
                Tools = tools
            }
        });
    }

    private string BuildInstructions(ToolRunState run)
    {
        run.Report("readingSkill", Path.GetFileName(context.SkillPath));
        var skill = File.Exists(context.SkillPath)
            ? File.ReadAllText(context.SkillPath)
            : throw new FileNotFoundException("The MyTools create-plugin skill is unavailable.", context.SkillPath);
        var operationInstructions = run.SelectedPlugin is null
            ? """
              No plugin is selected. Create a new plugin. Choose a unique kebab-case plugin ID and a unique user-facing name.
              """
            : $$"""
              The user selected an existing development plugin for editing. Modify only this plugin, inspect its files before
              writing, preserve unrelated behavior, and keep its existing plugin ID:
              id={{run.SelectedPlugin.Id}}, name={{run.SelectedPlugin.Name}}, type={{run.SelectedPlugin.PluginType}},
              sourcePath={{run.SelectedPlugin.SourcePath}}.
              """;
        return $$"""
            You are the MyTools Host plugin creation and editing agent. Help the user clarify requirements and then create or
            edit a complete, runnable v3 Node plugin. Use the tools for all file inspection and writes. Never claim a file was written unless
            write_plugin_file succeeded. Never write outside the coding workspace. Do not request or reveal secrets.

            {{operationInstructions}}

            Before writing, call get_mytools_context and inspect the selected plugin (when present) plus the smallest relevant
            example plugins under referenceRoot. The bundled references are authoritative and available even when the user
            has no MyTools source checkout. Generate or preserve every required source, manifest, package, build,
            i18n and icon-related field described by the skill. Do not run shell commands. When editing a selected development
            plugin, you may start its singleton watch and inspect watch/MyTools logs to diagnose build or runtime failures. When all files are ready, call
            complete_plugin exactly once. If the request is materially ambiguous, ask a concise question instead of guessing.
            After completion, summarize what was created. The Host will run npm install and open npm run watch after
            registration, so do not ask the user to run those commands unless the Host reports a setup failure.

            The authoritative repository skill follows:

            {{skill}}
            """;
    }

    public void Dispose()
    {
        foreach (var conversation in conversations.Values) conversation.Dispose();
        conversations.Clear();
        httpClient.Dispose();
    }

    private sealed class Conversation : IDisposable
    {
        private readonly object progressSync = new();
        private readonly List<AiProgressEvent> progress = [];
        private TaskCompletionSource<bool> progressChanged = NewProgressSignal();
        private long nextSequence;
        public List<ChatMessage> Messages { get; } = [];
        public SemaphoreSlim Gate { get; } = new(1, 1);

        public void Report(string kind, string? detail)
        {
            TaskCompletionSource<bool> signal;
            lock (progressSync)
            {
                var clippedDetail = detail is { Length: > 2000 } ? detail[..2000] : detail;
                progress.Add(new AiProgressEvent(++nextSequence, kind, clippedDetail));
                if (progress.Count > 4000) progress.RemoveRange(0, progress.Count - 4000);
                signal = progressChanged;
                progressChanged = NewProgressSignal();
            }
            signal.TrySetResult(true);
        }

        public async Task<IReadOnlyList<AiProgressEvent>> WaitForProgressAsync(
            long afterSequence,
            CancellationToken cancellationToken)
        {
            Task waitTask;
            lock (progressSync)
            {
                var available = progress.Where(item => item.Sequence > afterSequence).Take(100).ToArray();
                if (available.Length > 0) return available;
                waitTask = progressChanged.Task;
            }
            await Task.WhenAny(waitTask, Task.Delay(TimeSpan.FromSeconds(12), cancellationToken));
            cancellationToken.ThrowIfCancellationRequested();
            lock (progressSync)
            {
                return progress.Where(item => item.Sequence > afterSequence).Take(100).ToArray();
            }
        }

        public void Dispose()
        {
            lock (progressSync) progressChanged.TrySetResult(true);
            Gate.Dispose();
        }

        private static TaskCompletionSource<bool> NewProgressSignal() =>
            new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ToolRunState(
        PluginCreationContext context,
        SelectedPluginContext? selectedPlugin,
        HttpClient httpClient,
        Action<string, string?> report)
    {
        public SelectedPluginContext? SelectedPlugin { get; } = selectedPlugin;
        public CreatedPluginArtifact? CreatedPlugin { get; private set; }
        public void Report(string kind, string? detail = null) => report(kind, detail);

        [Description("Returns MyTools plugin paths and existing plugin IDs/names. Configuration contents are intentionally not exposed.")]
        public string GetMyToolsContext()
        {
            Report("readingContext");
            return JsonSerializer.Serialize(new
            {
                repositoryRoot = context.RepositoryRoot,
                examplesRoot = context.ExamplesRoot,
                pluginsRoot = context.PluginsRoot,
                codingRoot = context.CodingRoot,
                configurationRoot = context.ConfigurationRoot,
                skillPath = context.SkillPath,
                referenceRoot = context.ReferenceRoot,
                existingPlugins = context.ExistingPlugins,
                selectedPlugin = SelectedPlugin
            });
        }

        [Description("Returns the Host's approximate city-level location. Never returns an IP address, coordinates, or a postal address.")]
        public async Task<string> GetHostCityAsync(CancellationToken cancellationToken = default)
        {
            Report("resolvingLocation");
            if (context.HostCityProvider is null)
                return JsonSerializer.Serialize(new { available = false, error = "Host city location is unavailable." });
            return JsonSerializer.Serialize(await context.HostCityProvider.GetCityAsync(cancellationToken));
        }

        [Description("Lists files under the repository, bundled reference root, or coding root. Path may be absolute, relative to the repository root, or start with references/.")]
        public string ListFiles(
            [Description("Directory to inspect.")] string path,
            [Description("Optional search pattern such as *.json.")] string pattern = "*")
        {
            var directory = ResolveReadablePath(path);
            Report("listingFiles", DisplayPath(directory));
            if (!Directory.Exists(directory)) return "Directory does not exist.";
            var files = Directory.EnumerateFiles(directory, pattern, SearchOption.AllDirectories)
                .Where(file => !HasIgnoredSegment(file))
                .Take(200)
                .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/'));
            return string.Join('\n', files);
        }

        [Description("Reads a UTF-8 text file under the repository or coding root. Configuration files outside the repository are not readable.")]
        public string ReadFile([Description("Absolute path or path relative to the repository root.")] string path)
        {
            var file = ResolveReadablePath(path);
            Report("readingFile", DisplayPath(file));
            if (!File.Exists(file)) return "File does not exist.";
            var fileName = Path.GetFileName(file);
            var extension = Path.GetExtension(file).ToLowerInvariant();
            if (fileName.StartsWith(".env", StringComparison.OrdinalIgnoreCase)
                || extension is ".pfx" or ".p12" or ".pem" or ".key")
                return "Sensitive credential files are not readable by the AI agent.";
            var info = new FileInfo(file);
            if (info.Length > 512 * 1024) return "File is too large to read.";
            return File.ReadAllText(file);
        }

        [Description("Writes one UTF-8 source file under codingRoot/pluginId. Creates parent directories. Cannot write existing/system plugin IDs.")]
        public string WritePluginFile(
            [Description("Unique kebab-case plugin ID.")] string pluginId,
            [Description("Path relative to the plugin root, for example src/backend/index.mts.")] string relativePath,
            [Description("Complete file content.")] string content)
        {
            var normalizedId = ValidateWritablePluginId(pluginId);
            if (content.Length > 512 * 1024) throw new InvalidOperationException("A plugin file cannot exceed 512 KB.");
            var pluginRoot = GetPluginRoot(normalizedId);
            var target = ResolveWithin(pluginRoot, relativePath);
            Report(File.Exists(target) ? "editingFile" : "writingFile", $"{normalizedId}/{Path.GetRelativePath(pluginRoot, target).Replace('\\', '/')}");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllText(target, content);
            return $"Wrote {Path.GetRelativePath(pluginRoot, target).Replace('\\', '/')}";
        }

        [Description("Builds the selected development plugin and ensures its singleton npm watch is running. Use after edits when runtime validation is needed.")]
        public async Task<string> StartPluginWatchAsync(
            [Description("Selected development plugin ID.")] string pluginId,
            CancellationToken cancellationToken = default)
        {
            var normalizedId = ValidateDiagnosticPluginId(pluginId);
            if (context.DevelopmentDiagnostics is null)
                return JsonSerializer.Serialize(new { available = false, error = "Development diagnostics are unavailable." });
            Report("startingPluginWatch", normalizedId);
            return JsonSerializer.Serialize(await context.DevelopmentDiagnostics.StartPluginWatchAsync(
                normalizedId, cancellationToken));
        }

        [Description("Returns the most recent watch output lines for the selected development plugin.")]
        public string GetPluginWatchLogs(
            [Description("Selected development plugin ID.")] string pluginId,
            [Description("Number of recent lines, from 1 to 500.")] int count = 100)
        {
            var normalizedId = ValidateDiagnosticPluginId(pluginId);
            if (context.DevelopmentDiagnostics is null)
                return JsonSerializer.Serialize(new { available = false, error = "Development diagnostics are unavailable." });
            Report("readingPluginWatchLogs", normalizedId);
            return JsonSerializer.Serialize(context.DevelopmentDiagnostics.GetPluginWatchLogs(normalizedId, count));
        }

        [Description("Returns the most recent sanitized MyTools Host log lines for diagnosing plugin integration failures.")]
        public string GetMyToolsLogs(
            [Description("Number of recent lines, from 1 to 500.")] int count = 100)
        {
            if (context.DevelopmentDiagnostics is null)
                return JsonSerializer.Serialize(new { available = false, error = "Development diagnostics are unavailable." });
            Report("readingMyToolsLogs", count.ToString());
            return JsonSerializer.Serialize(context.DevelopmentDiagnostics.GetSystemLogs(count));
        }

        [Description("Searches the public web when current documentation is needed. Returns a small text result set.")]
        public async Task<string> SearchWebAsync([Description("Search query.")] string query)
        {
            if (string.IsNullOrWhiteSpace(query)) return "Search query is empty.";
            var uri = new Uri("https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query.Trim()));
            Report("searchingWeb", uri.ToString());
            var html = await httpClient.GetStringAsync(uri);
            return ToPlainText(html, 12000);
        }

        [Description("Fetches a public HTTPS documentation URL. Private/local network targets are rejected.")]
        public async Task<string> FetchUrlAsync([Description("Public HTTPS URL.")] string url)
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            {
                return "Only absolute HTTPS URLs are supported.";
            }
            if (uri.IsLoopback || await ResolvesToPrivateAddress(uri.Host)) return "Local and private network URLs are not allowed.";
            Report("fetchingUrl", uri.ToString());
            var text = await httpClient.GetStringAsync(uri);
            return ToPlainText(text, 20000);
        }

        [Description("Validates and finalizes a generated plugin. Call only after every required file has been written.")]
        public string CompletePlugin(
            [Description("Plugin ID used for the workspace directory and manifest.")] string pluginId,
            [Description("User-facing plugin name.")] string name,
            [Description("standard for native list results or custom-ui for a web detail page.")] string pluginType)
        {
            Report("validatingPlugin", pluginId);
            var normalizedId = ValidateWritablePluginId(pluginId);
            var pluginRoot = GetPluginRoot(normalizedId);
            var manifestPath = Path.Combine(pluginRoot, "plugin.json");
            var packagePath = Path.Combine(pluginRoot, "package.json");
            var buildPath = Path.Combine(pluginRoot, "build-plugin.mjs");
            if (!File.Exists(manifestPath) || !File.Exists(packagePath) || !File.Exists(buildPath))
            {
                throw new InvalidOperationException("plugin.json, package.json and build-plugin.mjs are required.");
            }

            var buildScript = File.ReadAllText(buildPath);
            if (Regex.IsMatch(buildScript, @"\bonRebuild\b", RegexOptions.CultureInvariant))
            {
                throw new InvalidOperationException(
                    "build-plugin.mjs uses the obsolete esbuild onRebuild watch option. Use a build.onEnd plugin and call context.watch() without arguments.");
            }

            using (var packageDocument = JsonDocument.Parse(File.ReadAllText(packagePath)))
            {
                if (!packageDocument.RootElement.TryGetProperty("scripts", out var scripts)
                    || scripts.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("package.json scripts are required.");
                RequireString(scripts, "build");
                RequireString(scripts, "watch");
                RequireString(scripts, "check");
            }

            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            RequireString(root, "version");
            RequireString(root, "icon");
            if (RequireString(root, "id") != normalizedId) throw new InvalidOperationException("plugin.json id does not match pluginId.");
            if (RequireString(root, "protocolVersion") != "3.0") throw new InvalidOperationException("protocolVersion must be 3.0.");
            if (!root.TryGetProperty("i18n", out var i18n) || i18n.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("plugin.json i18n is required.");
            RequireString(i18n, "catalog");
            RequireString(i18n, "localesPath");
            if (!root.TryGetProperty("entries", out var entries) || entries.ValueKind != JsonValueKind.Array || entries.GetArrayLength() == 0)
                throw new InvalidOperationException("plugin.json must contain at least one entry.");

            var aliases = new List<string>();
            var hotKeys = new List<string>();
            foreach (var entry in entries.EnumerateArray())
            {
                RequireString(entry, "id");
                RequireString(entry, "entry");
                if (!entry.TryGetProperty("name", out var entryName) || entryName.ValueKind != JsonValueKind.Object)
                    throw new InvalidOperationException("Every entry requires a localized name.");
                RequireString(entryName, "key");
                RequireString(entryName, "defaultValue");
                if (!entry.TryGetProperty("capabilities", out var capabilities) || capabilities.ValueKind != JsonValueKind.Array)
                    throw new InvalidOperationException("Every entry requires a capabilities array.");
                if (entry.TryGetProperty("alias", out var alias) && alias.ValueKind == JsonValueKind.Array)
                    aliases.AddRange(alias.EnumerateArray().Select(item => item.GetString()).OfType<string>());
                if (entry.TryGetProperty("hotKey", out var hotKey) && hotKey.ValueKind == JsonValueKind.String)
                    hotKeys.Add(hotKey.GetString()!);
            }

            var catalog = ResolveWithin(pluginRoot, RequireString(i18n, "catalog"));
            var locales = ResolveWithin(pluginRoot, RequireString(i18n, "localesPath"));
            if (!File.Exists(catalog) || !Directory.Exists(locales) || !Directory.EnumerateFiles(locales, "*.json").Any())
                throw new InvalidOperationException("The i18n catalog and locale files are required.");

            CreatedPlugin = new CreatedPluginArtifact(
                normalizedId,
                name.Trim(),
                pluginType == "custom-ui" ? "custom-ui" : "standard",
                pluginRoot,
                Path.Combine(pluginRoot, "dist"),
                aliases.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                hotKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                ["Open a terminal in the plugin directory.", "Run npm install.", "Run npm run watch.", "Open MyTools and test the alias or hotkey; use Refresh All after manifest changes."],
                SelectedPlugin is not null);
            Report("pluginReady", normalizedId);
            return JsonSerializer.Serialize(CreatedPlugin);
        }

        private string DisplayPath(string path)
        {
            if (!string.IsNullOrWhiteSpace(context.ReferenceRoot) && IsWithin(context.ReferenceRoot, path))
                return "references/" + Path.GetRelativePath(context.ReferenceRoot, path).Replace('\\', '/');
            if (IsWithin(context.CodingRoot, path))
                return "coding/" + Path.GetRelativePath(context.CodingRoot, path).Replace('\\', '/');
            if (IsWithin(context.ExamplesRoot, path))
                return "Examples/" + Path.GetRelativePath(context.ExamplesRoot, path).Replace('\\', '/');
            if (IsWithin(context.PluginsRoot, path))
                return "plugins/" + Path.GetRelativePath(context.PluginsRoot, path).Replace('\\', '/');
            return Path.GetRelativePath(context.RepositoryRoot, path).Replace('\\', '/');
        }

        private string ValidateWritablePluginId(string pluginId)
        {
            var normalized = pluginId.Trim().ToLowerInvariant();
            if (!ValidPluginId.IsMatch(normalized)) throw new InvalidOperationException("Invalid plugin ID.");
            if (SelectedPlugin is not null)
            {
                if (!string.Equals(SelectedPlugin.Id, normalized, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException($"Editing is limited to the selected plugin '{SelectedPlugin.Id}'.");
                return normalized;
            }
            if (context.ExistingPlugins.Any(plugin => string.Equals(plugin.Id, normalized, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException("The plugin ID already exists.");
            return normalized;
        }

        private string ValidateDiagnosticPluginId(string pluginId)
        {
            var normalized = pluginId.Trim().ToLowerInvariant();
            if (!ValidPluginId.IsMatch(normalized)) throw new InvalidOperationException("Invalid plugin ID.");
            if (SelectedPlugin is null
                || !string.Equals(SelectedPlugin.Id, normalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Diagnostics are limited to the selected development plugin.");
            }
            return normalized;
        }

        private string GetPluginRoot(string pluginId) => ResolveWithin(context.CodingRoot, pluginId);

        private string ResolveReadablePath(string path)
        {
            string candidate;
            if (Path.IsPathRooted(path))
            {
                candidate = Path.GetFullPath(path);
            }
            else if (!string.IsNullOrWhiteSpace(context.ReferenceRoot)
                     && path.Replace('\\', '/').StartsWith("references/", StringComparison.OrdinalIgnoreCase))
            {
                candidate = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(context.ReferenceRoot)!, path));
            }
            else
            {
                candidate = Path.GetFullPath(Path.Combine(context.RepositoryRoot, path));
            }
            if (IsWithin(context.RepositoryRoot, candidate) || IsWithin(context.PluginsRoot, candidate)
                || IsWithin(context.CodingRoot, candidate)
                || (!string.IsNullOrWhiteSpace(context.ReferenceRoot) && IsWithin(context.ReferenceRoot, candidate))) return candidate;
            throw new InvalidOperationException("Reading is limited to the MyTools repository, bundled references, installed plugins, and coding workspace.");
        }

        private static string ResolveWithin(string root, string relativePath)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var candidate = Path.GetFullPath(Path.Combine(fullRoot, relativePath.Replace('/', Path.DirectorySeparatorChar)));
            if (!IsWithin(fullRoot, candidate) || string.Equals(fullRoot, candidate, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path escapes its allowed root.");
            return candidate;
        }

        private static bool IsWithin(string root, string candidate)
        {
            var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var fullCandidate = Path.GetFullPath(candidate).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return string.Equals(fullRoot, fullCandidate, StringComparison.OrdinalIgnoreCase)
                || fullCandidate.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasIgnoredSegment(string path) => path.Split(Path.DirectorySeparatorChar)
            .Any(part => part is "node_modules" or "bin" or "obj" or ".git" or "dist");

        private static string RequireString(JsonElement element, string property)
        {
            if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidOperationException($"A non-empty {property} is required.");
            return value.GetString()!;
        }

        private static async Task<bool> ResolvesToPrivateAddress(string host)
        {
            try
            {
                var addresses = await Dns.GetHostAddressesAsync(host);
                return addresses.Length == 0 || addresses.Any(IsPrivateAddress);
            }
            catch
            {
                return true;
            }
        }

        private static bool IsPrivateAddress(IPAddress address)
        {
            if (IPAddress.IsLoopback(address)) return true;
            var bytes = address.GetAddressBytes();
            if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                return bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254)
                    || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
            return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.Equals(IPAddress.IPv6Loopback);
        }

        private static string ToPlainText(string content, int limit)
        {
            var text = Regex.Replace(content, "<script[\\s\\S]*?</script>|<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
            text = Regex.Replace(text, "<[^>]+>", " ");
            text = WebUtility.HtmlDecode(text);
            text = Regex.Replace(text, "\\s+", " ").Trim();
            return text.Length <= limit ? text : text[..limit];
        }
    }
}
