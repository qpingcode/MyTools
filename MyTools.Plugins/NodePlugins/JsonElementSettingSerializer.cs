using System.Text.Json;
using MyTools.Common.Config.Interfaces;

namespace MyTools.Plugins.NodePlugins;

public sealed class JsonElementSettingSerializer : IRegistrySerializer
{
    public Type SerializedType => typeof(JsonElement);

    public object? Deserialize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return JsonSerializer.SerializeToElement(Array.Empty<object>());
        }

        using var document = JsonDocument.Parse(value);
        return document.RootElement.Clone();
    }

    public string Serialize(object value)
    {
        if (value is JsonElement json)
        {
            return json.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
                ? "[]"
                : json.GetRawText();
        }

        return JsonSerializer.Serialize(value);
    }
}
