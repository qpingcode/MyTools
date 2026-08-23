using System.Text.Json;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class NodePluginWellKnownActionsTest
{
    [Test]
    public void ActionResponse_Refresh_ReadsRefreshFlag()
    {
        var response = JsonSerializer.Deserialize<NodePluginActionResponse>(
            """{"refresh":true}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(response?.Refresh, Is.True);
    }

    [Test]
    public void HostActionDto_Copy_UsesItsOwnTextField()
    {
        var action = JsonSerializer.Deserialize<NodePluginHostActionDto>(
            """{"kind":"copy","text":"hello","path":"ignored"}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(action, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(action!.Kind, Is.EqualTo("copy"));
            Assert.That(action.Text, Is.EqualTo("hello"));
            Assert.That(action.Path, Is.EqualTo("ignored"));
        });
    }

    [Test]
    public void HostActionDto_AddClipboardHistory_ReadsTexts()
    {
        var action = JsonSerializer.Deserialize<NodePluginHostActionDto>(
            """{"kind":"addClipboardHistory","texts":["first","second"]}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.That(action, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(action!.Kind, Is.EqualTo("addClipboardHistory"));
            Assert.That(action.Texts, Is.EqualTo(new[] { "first", "second" }));
        });
    }

    [Test]
    public async Task ExecuteAsync_UnknownKind_ReturnsFailure()
    {
        var result = await NodePluginWellKnownActions.ExecuteAsync(new NodePluginHostActionDto
        {
            Kind = "not-an-action"
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.LocalizedMessage?.Key, Is.EqualTo("NodePlugin.UnknownHostAction"));
    }

    [Test]
    public async Task ExecuteAsync_ExecuteWithoutPath_ReturnsTypedPayloadFailure()
    {
        var result = await NodePluginWellKnownActions.ExecuteAsync(new NodePluginHostActionDto
        {
            Kind = "execute",
            Text = "must not be used as a fallback"
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.LocalizedMessage?.Key, Is.EqualTo("NodePlugin.InvalidHostAction"));
    }

    [Test]
    public async Task ExecuteAsync_AddClipboardHistoryWithoutTexts_ReturnsTypedPayloadFailure()
    {
        var result = await NodePluginWellKnownActions.ExecuteAsync(new NodePluginHostActionDto
        {
            Kind = "addClipboardHistory"
        });

        Assert.That(result.Success, Is.False);
        Assert.That(result.LocalizedMessage?.Key, Is.EqualTo("NodePlugin.InvalidHostAction"));
    }
}
