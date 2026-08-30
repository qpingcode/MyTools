using System.Collections.Concurrent;
using System.Text.Json;
using MyTools.AI;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class DevelopmentPluginHostCallHandler : IPluginHostCapabilityHandler
{
    private readonly DevelopmentPluginService service;
    private readonly PluginCreationAgentService aiService;
    private readonly ConcurrentDictionary<string, CancellationTokenSource> aiOperations = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public DevelopmentPluginHostCallHandler(
        DevelopmentPluginService service,
        PluginCreationAgentService aiService)
    {
        this.service = service;
        this.aiService = aiService;
    }

    public IReadOnlyCollection<string> Capabilities { get; } =
    [
        "development.create", "development.validate", "development.list", "development.delete",
        "development.refresh", "development.openFolder", "development.openCode",
        "development.startDebug", "development.stopDebug", "development.watch.start", "development.watch.logs",
        "development.logs", "development.publish",
        "development.ai.status", "development.ai.chat", "development.ai.progress", "development.ai.cancel"
    ];

    public async Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        return request.Method switch
        {
            "development.create" => Create(request.Params),
            "development.validate" => Validate(request.Params),
            "development.list" => JsonSerializer.SerializeToElement(new { plugins = service.GetRegistrations() }, JsonOptions),
            "development.delete" => Delete(request.Params),
            "development.refresh" => Refresh(),
            "development.openFolder" => Open(request.Params, DevelopmentPluginService.OpenFolder),
            "development.openCode" => Open(request.Params, DevelopmentPluginService.OpenVisualStudioCode),
            "development.startDebug" => await StartDebugAsync(request.Params, cancellationToken),
            "development.stopDebug" => StopDebug(request.Params),
            "development.watch.start" => await StartWatchAsync(request.Params, cancellationToken),
            "development.watch.logs" => GetWatchLogs(request.Params),
            "development.logs" => GetSystemLogs(request.Params),
            "development.publish" => await PublishAsync(request.Params, cancellationToken),
            "development.ai.status" => JsonSerializer.SerializeToElement(aiService.GetAvailability(), JsonOptions),
            "development.ai.chat" => await ChatAsync(request.Params, cancellationToken),
            "development.ai.progress" => await GetAiProgressAsync(request.Params, cancellationToken),
            "development.ai.cancel" => CancelAiCreation(request.Params),
            _ => throw new NotSupportedException($"Unknown development hostCall method: {request.Method}")
        };
    }

    private async Task<JsonElement> ChatAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var hostRequest = payload.Deserialize<AiChatHostRequest>(JsonOptions)
            ?? throw new InvalidOperationException("Invalid AI plugin creation request.");
        SelectedPluginContext? selectedPlugin = null;
        if (!string.IsNullOrWhiteSpace(hostRequest.SelectedPluginId))
        {
            var registration = service.GetAiEditableRegistration(hostRequest.SelectedPluginId);
            selectedPlugin = new SelectedPluginContext(
                registration.PluginId,
                registration.Name,
                registration.PluginType,
                registration.SourcePath,
                registration.DistPath);
        }
        var sessionId = string.IsNullOrWhiteSpace(hostRequest.SessionId)
            ? Guid.NewGuid().ToString("N")
            : hostRequest.SessionId.Trim();
        var chatRequest = new PluginCreationChatRequest(
            sessionId,
            hostRequest.Message,
            selectedPlugin);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        if (!aiOperations.TryAdd(sessionId, operation))
        {
            throw new InvalidOperationException("An AI creation operation is already running for this session.");
        }
        PluginCreationChatResponse response;
        try
        {
            response = await aiService.ChatAsync(chatRequest, operation.Token);
            if (response.CreatedPlugin is not null)
            {
                service.RegisterAiPlugin(response.CreatedPlugin);
                aiService.MarkPluginRegistered(response.CreatedPlugin.PluginId, response.CreatedPlugin.Name);
                aiService.ReportProgress(response.SessionId, "pluginRegistered", response.CreatedPlugin.PluginId);
                var setup = await service.InstallAndStartWatchAsync(
                    response.CreatedPlugin,
                    (kind, detail) => aiService.ReportProgress(response.SessionId, kind, detail),
                    operation.Token);
                response = response with { Setup = setup };
            }
        }
        catch (OperationCanceledException) when (operation.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            const string reply = "Plugin creation was stopped. Files already written to the development folder were kept.";
            aiService.ReportProgress(sessionId, "stopped", null);
            response = new PluginCreationChatResponse(sessionId, reply, null, Stopped: true);
        }
        finally
        {
            aiOperations.TryRemove(sessionId, out _);
        }
        aiService.ReportProgress(response.SessionId, "turnComplete");
        return JsonSerializer.SerializeToElement(response, JsonOptions);
    }

    private async Task<JsonElement> GetAiProgressAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var sessionId = payload.GetProperty("sessionId").GetString() ?? "";
        var afterSequence = payload.TryGetProperty("afterSequence", out var value) && value.TryGetInt64(out var sequence)
            ? sequence
            : 0;
        var progress = await aiService.GetProgressAsync(sessionId, afterSequence, cancellationToken);
        return JsonSerializer.SerializeToElement(progress, JsonOptions);
    }

    private JsonElement CancelAiCreation(JsonElement payload)
    {
        var sessionId = payload.TryGetProperty("sessionId", out var value) ? value.GetString() : null;
        var cancelled = !string.IsNullOrWhiteSpace(sessionId)
            && aiOperations.TryGetValue(sessionId, out var operation)
            && TryCancel(operation);
        return JsonSerializer.SerializeToElement(new { cancelled }, JsonOptions);
    }

    private static bool TryCancel(CancellationTokenSource operation)
    {
        try
        {
            operation.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    private JsonElement Create(JsonElement payload)
    {
        var createRequest = payload.Deserialize<CreateDevelopmentPluginRequest>(JsonOptions)
            ?? throw new InvalidOperationException("Invalid create plugin request.");
        return JsonSerializer.SerializeToElement(service.Create(createRequest), JsonOptions);
    }

    private JsonElement Validate(JsonElement payload)
    {
        var name = payload.GetProperty("name").GetString() ?? "";
        var pluginId = payload.GetProperty("pluginId").GetString() ?? "";
        return JsonSerializer.SerializeToElement(service.Validate(name, pluginId), JsonOptions);
    }

    private JsonElement Delete(JsonElement payload)
    {
        var pluginId = payload.GetProperty("pluginId").GetString() ?? "";
        service.Delete(pluginId);
        aiService.ForgetPlugin(pluginId);
        return JsonSerializer.SerializeToElement(new { success = true }, JsonOptions);
    }

    private JsonElement Refresh()
    {
        service.RefreshAll();
        return JsonSerializer.SerializeToElement(new { success = true }, JsonOptions);
    }

    private static JsonElement Open(JsonElement payload, Action<string> open)
    {
        open(payload.GetProperty("sourcePath").GetString() ?? "");
        return JsonSerializer.SerializeToElement(new { success = true }, JsonOptions);
    }

    private async Task<JsonElement> StartDebugAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var result = await service.StartDebugAsync(GetPluginId(payload), cancellationToken);
        return JsonSerializer.SerializeToElement(result, JsonOptions);
    }

    private JsonElement StopDebug(JsonElement payload)
    {
        var result = service.StopDebug(GetPluginId(payload));
        return JsonSerializer.SerializeToElement(result, JsonOptions);
    }

    private async Task<JsonElement> StartWatchAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var result = await service.StartPluginWatchAsync(GetPluginId(payload), cancellationToken);
        return JsonSerializer.SerializeToElement(result, JsonOptions);
    }

    private JsonElement GetWatchLogs(JsonElement payload)
    {
        var result = service.GetPluginWatchLogs(GetPluginId(payload), GetCount(payload));
        return JsonSerializer.SerializeToElement(result, JsonOptions);
    }

    private JsonElement GetSystemLogs(JsonElement payload)
    {
        return JsonSerializer.SerializeToElement(service.GetSystemLogs(GetCount(payload)), JsonOptions);
    }

    private async Task<JsonElement> PublishAsync(JsonElement payload, CancellationToken cancellationToken)
    {
        var result = await service.PublishAsync(GetPluginId(payload), cancellationToken);
        return JsonSerializer.SerializeToElement(result, JsonOptions);
    }

    private static string GetPluginId(JsonElement payload) =>
        payload.GetProperty("pluginId").GetString() ?? "";

    private static int GetCount(JsonElement payload) =>
        payload.TryGetProperty("count", out var count) && count.TryGetInt32(out var value) ? value : 100;

    private sealed record AiChatHostRequest(string? SessionId, string Message, string? SelectedPluginId);
}
