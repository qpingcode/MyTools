using MyTools.Desktop.Components;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class NodePluginDetailViewHostNameTests
{
    [Test]
    public void BuildPluginHostName_SameDirectoryAndVersion_IsStable()
    {
        var first = NodePluginDetailView.BuildPluginHostName(@"C:\plugins\settings", "0.0.9");
        var second = NodePluginDetailView.BuildPluginHostName(@"C:\plugins\settings", "0.0.9");

        Assert.That(second, Is.EqualTo(first));
        Assert.That(first, Does.Match("^plugin-[a-f0-9]{16}\\.mytools\\.localhost$"));
    }

    [Test]
    public void BuildPluginHostName_DifferentVersion_UsesDifferentOrigin()
    {
        var previous = NodePluginDetailView.BuildPluginHostName(@"C:\plugins\settings", "0.0.8");
        var current = NodePluginDetailView.BuildPluginHostName(@"C:\plugins\settings", "0.0.9");

        Assert.That(current, Is.Not.EqualTo(previous));
    }
}
