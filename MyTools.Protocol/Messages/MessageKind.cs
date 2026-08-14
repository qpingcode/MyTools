using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTools.Protocol.Messages;

/// <summary>
/// The three message kinds on the bus. Serialized as lowercase wire strings
/// ("request", "response", "event"); the field name is frozen in Phase 1.
/// </summary>
[JsonConverter(typeof(MessageKindConverter))]
public enum MessageKind
{
    Request,
    Response,
    Event
}

/// <summary>
/// Serializes <see cref="MessageKind"/> as lowercase wire strings. Explicit converter avoids
/// relying on JsonNamingPolicy polyfills and guarantees exact "request"/"response"/"event".
/// </summary>
file sealed class MessageKindConverter : JsonConverter<MessageKind>
{
    public override MessageKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return s switch
        {
            "request" => MessageKind.Request,
            "response" => MessageKind.Response,
            "event" => MessageKind.Event,
            _ => throw new JsonException($"unknown message kind '{s}'")
        };
    }

    public override void Write(Utf8JsonWriter writer, MessageKind value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value switch
        {
            MessageKind.Request => "request",
            MessageKind.Response => "response",
            MessageKind.Event => "event",
            _ => throw new JsonException($"unknown message kind {value}")
        });
    }
}
