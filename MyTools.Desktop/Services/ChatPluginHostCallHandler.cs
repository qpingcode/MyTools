using System.Text.Json;
using MyTools.AI;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Services;

public sealed class ChatPluginHostCallHandler(ChatAgentService chatService) : IPluginHostCapabilityHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public IReadOnlyCollection<string> Capabilities { get; } =
    [
        "ai.chat.status", "ai.chat.send", "ai.chat.state", "ai.chat.cancel", "ai.chat.clear"
    ];

    public async Task<JsonElement> HandleAsync(HostCallRequest request, CancellationToken cancellationToken)
    {
        return request.Method switch
        {
            "ai.chat.status" => Serialize(chatService.GetAvailability()),
            "ai.chat.send" => Serialize(await chatService.ChatAsync(
                request.Params.Deserialize<ChatAgentRequest>(JsonOptions)
                ?? throw new InvalidOperationException("Invalid chat request."), cancellationToken)),
            "ai.chat.state" => Serialize(chatService.GetState(
                RequiredString(request.Params, "sessionId"),
                OptionalString(request.Params, "model"))),
            "ai.chat.cancel" => Serialize(new
            {
                cancelled = chatService.Cancel(RequiredString(request.Params, "sessionId"))
            }),
            "ai.chat.clear" => Clear(request.Params),
            _ => throw new NotSupportedException($"Unknown chat hostCall method: {request.Method}")
        };
    }

    private JsonElement Clear(JsonElement payload)
    {
        chatService.Clear(OptionalString(payload, "sessionId"));
        return Serialize(new { success = true });
    }

    private static JsonElement Serialize<T>(T value) => JsonSerializer.SerializeToElement(value, JsonOptions);

    private static string RequiredString(JsonElement payload, string property) =>
        OptionalString(payload, property) ?? throw new InvalidOperationException($"Missing {property}.");

    private static string? OptionalString(JsonElement payload, string property) =>
        payload.TryGetProperty(property, out var value) ? value.GetString() : null;
}
