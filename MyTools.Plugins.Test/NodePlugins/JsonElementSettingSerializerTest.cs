using System.Text.Json;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class JsonElementSettingSerializerTest
{
    [Test]
    public void RoundTrip_ShouldPreserveArray()
    {
        var serializer = new JsonElementSettingSerializer();
        using var document = JsonDocument.Parse("""[{"trigger":"sig"}]""");
        var json = serializer.Serialize(document.RootElement.Clone());
        var back = (JsonElement)serializer.Deserialize(json)!;

        Assert.That(back.GetArrayLength(), Is.EqualTo(1));
        Assert.That(back[0].GetProperty("trigger").GetString(), Is.EqualTo("sig"));
    }

    [Test]
    public void Deserialize_Empty_ShouldReturnEmptyArray()
    {
        var serializer = new JsonElementSettingSerializer();
        var value = (JsonElement)serializer.Deserialize("")!;
        Assert.That(value.ValueKind, Is.EqualTo(JsonValueKind.Array));
        Assert.That(value.GetArrayLength(), Is.EqualTo(0));
    }
}
