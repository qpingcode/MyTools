using System.Text.Json;
using MyTools.Common;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class NodePluginWellKnownActionsTest
{
    [Test]
    [TestCase("{\"host\":{\"kind\":\"copy\",\"text\":\"x\"}}")]
    [TestCase("{\"web\":{}}")]
    [TestCase("{\"detail\":{}}")]
    [TestCase("{\"close\":true}")]
    [TestCase("{\"refresh\":true}")]
    public void ActionOutcome_LegacyField_IsRejected(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<NodePluginActionOutcome>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }));
    }

    [Test]
    public void ActionOutcome_TargetHost_NormalizesAction()
    {
        var outcome = JsonSerializer.Deserialize<NodePluginActionOutcome>(
            """{"target":{"kind":"host","action":{"kind":"copy","text":"hello"}},"after":"close"}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var normalized = outcome!.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(normalized.Target, Is.TypeOf<NodePluginNormalizedHostTarget>());
            var target = (NodePluginNormalizedHostTarget)normalized.Target!;
            Assert.That(target.Action.Kind, Is.EqualTo("copy"));
            Assert.That(target.Action.Text, Is.EqualTo("hello"));
            Assert.That(normalized.After, Is.EqualTo(NodePluginActionAfter.Close));
        });
    }

    [Test]
    public void ActionOutcome_TargetWeb_NormalizesPayload()
    {
        var response = JsonSerializer.Deserialize<NodePluginActionOutcome>(
            """{"target":{"kind":"web","payload":{"action":"format"}}}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var normalized = response!.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(normalized.Target, Is.TypeOf<NodePluginNormalizedWebTarget>());
            var target = (NodePluginNormalizedWebTarget)normalized.Target!;
            Assert.That(target.Payload.GetProperty("action").GetString(), Is.EqualTo("format"));
        });
    }

    [Test]
    public void ActionOutcome_TargetDetail_NormalizesDetail()
    {
        var response = JsonSerializer.Deserialize<NodePluginActionOutcome>(
            """{"target":{"kind":"detail","page":"detail.html","title":"Result","initialState":{"id":1},"actions":["run-once"]}}""",
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        var normalized = response!.Normalize();

        Assert.Multiple(() =>
        {
            Assert.That(normalized.Target, Is.TypeOf<NodePluginNormalizedDetailTarget>());
            var target = (NodePluginNormalizedDetailTarget)normalized.Target!;
            Assert.That(target.Detail.Page, Is.EqualTo("detail.html"));
            Assert.That(target.Detail.Title, Is.EqualTo("Result"));
            Assert.That(target.Detail.InitialState.GetProperty("id").GetInt32(), Is.EqualTo(1));
            Assert.That(target.Detail.Actions, Is.EqualTo(new[] { "run-once" }));
        });
    }

    [TestCase("{\"target\":{\"kind\":\"detail\"},\"after\":\"close\"}")]
    [TestCase("{\"target\":{\"kind\":\"web\",\"page\":\"detail.html\"}}")]
    [TestCase("{\"target\":{\"kind\":\"detail\",\"payload\":{}}}")]
    [TestCase("{\"target\":{\"kind\":\"host\"}}")]
    [TestCase("{\"target\":{\"kind\":\"host\",\"action\":{\"kind\":\"copy\"},\"payload\":{}}}")]
    [TestCase("{\"target\":{\"kind\":\"host\",\"action\":{\"kind\":\"copy\",\"text\":\"x\"},\"actions\":[\"run\"]}}")]
    [TestCase("{\"target\":{\"kind\":\"web\",\"actions\":[\"run\"]}}")]
    public void ActionOutcome_ConflictingTarget_Throws(string json)
    {
        var response = JsonSerializer.Deserialize<NodePluginActionOutcome>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Throws<InvalidOperationException>(() => response!.Normalize());
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

    [TestCase(ActionTypeEnum.Close, ActionTypeEnum.Close)]
    [TestCase(ActionTypeEnum.None, ActionTypeEnum.None)]
    public void ResolveActionType_OpenPlugin_UsesActualLauncherResult(
        ActionTypeEnum launcherResult,
        ActionTypeEnum expected)
    {
        var host = new NodePluginHostActionDto { Kind = "openPlugin" };

        var result = NodePluginInvokeAction.ResolveActionType(
            host, launcherResult, NodePluginActionAfter.Close);

        Assert.That(result, Is.EqualTo(expected));
    }

    [Test]
    public void ResolveActionType_OtherHostAction_PreservesPluginCloseRequest()
    {
        var result = NodePluginInvokeAction.ResolveActionType(
            new NodePluginHostActionDto { Kind = "copy" },
            ActionTypeEnum.Close,
            NodePluginActionAfter.Close);

        Assert.That(result, Is.EqualTo(ActionTypeEnum.Close));
    }

    [Test]
    public void ResolveActionType_Keep_PreservesWindow()
    {
        var result = NodePluginInvokeAction.ResolveActionType(
            null,
            ActionTypeEnum.None,
            NodePluginActionAfter.Keep);

        Assert.That(result, Is.EqualTo(ActionTypeEnum.None));
    }
}
