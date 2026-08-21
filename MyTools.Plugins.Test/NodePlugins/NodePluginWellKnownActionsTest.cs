using MyTools.Common;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using MyTools.Plugins.Param;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class NodePluginWellKnownActionsTest
{
    [Test]
    public void Resolve_MapsHostActions()
    {
        Assert.Multiple(() =>
        {
            Assert.That(NodePluginWellKnownActions.Resolve("copy"), Is.SameAs(WellKnownActions.Copy));
            Assert.That(NodePluginWellKnownActions.Resolve("copyAndPaste"), Is.SameAs(WellKnownActions.CopyAndPaste));
            Assert.That(NodePluginWellKnownActions.Resolve("execute"), Is.SameAs(WellKnownActions.Execute));
            Assert.That(NodePluginWellKnownActions.Resolve("openInExplorer"), Is.SameAs(WellKnownActions.OpenInExplorer));
            Assert.That(NodePluginWellKnownActions.Resolve("openInBrowser"), Is.SameAs(WellKnownActions.OpenInBrowser));
            Assert.That(NodePluginWellKnownActions.Resolve("detail"), Is.Null);
            Assert.That(NodePluginWellKnownActions.IsWellKnown("run"), Is.True);
            Assert.That(NodePluginWellKnownActions.IsWellKnown("open"), Is.False);
        });
    }

    [Test]
    public void CreateParams_Execute_UsesPathAndArgs()
    {
        var args = NodePluginWellKnownActions.CreateParams(
            "execute",
            @"C:\Apps\rider64.exe",
            @"""D:\work\app.sln""",
            copyText: null,
            itemId: "openpath:rider",
            title: "Open Rider",
            query: "");

        Assert.That(args, Is.TypeOf<ExecuteActionParams>());
        var execute = (ExecuteActionParams)args;
        Assert.That(execute.GetValue(), Is.EqualTo(@"C:\Apps\rider64.exe"));
        Assert.That(execute.Arguments, Is.EqualTo(@"""D:\work\app.sln"""));
    }

    [Test]
    public void CreateParams_Copy_UsesCopyText()
    {
        var args = NodePluginWellKnownActions.CreateParams(
            "copy",
            path: null,
            args: null,
            copyText: "hello",
            itemId: "id",
            title: "title",
            query: "q");

        Assert.That(args, Is.InstanceOf<IActionStringParam>());
        Assert.That(((IActionStringParam)args).GetValue(), Is.EqualTo("hello"));
    }
}
