using System.Text.Json;
using MyTools.Protocol.Messages;
using NUnit.Framework;

namespace MyTools.Protocol.Test.Messages;

[TestFixture]
public class MessageKindTest
{
    [TestCase(MessageKind.Request, "request")]
    [TestCase(MessageKind.Response, "response")]
    [TestCase(MessageKind.Event, "event")]
    public void Serialize_ShouldProduceLowercaseWireString(MessageKind kind, string expected)
    {
        var json = JsonSerializer.Serialize(kind);

        Assert.That(json, Is.EqualTo($"\"{expected}\""));
    }

    [TestCase("request", MessageKind.Request)]
    [TestCase("response", MessageKind.Response)]
    [TestCase("event", MessageKind.Event)]
    public void Deserialize_ShouldParseLowercaseWireString(string wire, MessageKind expected)
    {
        var kind = JsonSerializer.Deserialize<MessageKind>($"\"{wire}\"");

        Assert.That(kind, Is.EqualTo(expected));
    }

    [Test]
    public void Deserialize_ShouldRejectUnknownKind()
    {
        Assert.That(() => JsonSerializer.Deserialize<MessageKind>("\"notification\""),
            Throws.InstanceOf<JsonException>());
    }
}
