using System.Text.Json;
using System.Text.Json.Serialization;

namespace MyTools.Protocol.Messages;

/// <summary>
/// The three message kinds on the bus. Serialized as lowercase wire strings
/// (<see cref="MessageKindWire"/>); the field name is frozen in Phase 1.
/// </summary>
[JsonConverter(typeof(MessageKindConverter))]
public enum MessageKind
{
    Request,
    Response,
    Event
}

/// <summary>Lowercase wire strings for <see cref="MessageKind"/>. The converter is the only other consumer.</summary>
public static class MessageKindWire
{
    public const string Request = "request";
    public const string Response = "response";
    public const string Event = "event";

    public static string Format(MessageKind kind) => kind switch
    {
        MessageKind.Request => Request,
        MessageKind.Response => Response,
        MessageKind.Event => Event,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, null)
    };

    public static bool TryParse(string? wire, out MessageKind kind)
    {
        switch (wire)
        {
            case Request:
                kind = MessageKind.Request;
                return true;
            case Response:
                kind = MessageKind.Response;
                return true;
            case Event:
                kind = MessageKind.Event;
                return true;
            default:
                kind = default;
                return false;
        }
    }
}

/// <summary>
/// Serializes <see cref="MessageKind"/> as lowercase wire strings. Explicit converter avoids
/// relying on JsonNamingPolicy polyfills and guarantees exact values from <see cref="MessageKindWire"/>.
/// </summary>
file sealed class MessageKindConverter : JsonConverter<MessageKind>
{
    public override MessageKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var s = reader.GetString();
        return MessageKindWire.TryParse(s, out var kind)
            ? kind
            : throw new JsonException($"unknown message kind '{s}'");
    }

    public override void Write(Utf8JsonWriter writer, MessageKind value, JsonSerializerOptions options)
        => writer.WriteStringValue(MessageKindWire.Format(value));
}
