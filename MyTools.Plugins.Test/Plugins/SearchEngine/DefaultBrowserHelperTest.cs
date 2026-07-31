using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins.SearchEngine;

public class DefaultBrowserHelperTest
{
    [Test]
    public void TestGetDefaultBrowserPath()
    {
        var path = DefaultBrowserHelper.GetBrowserExecutePath();
        Assert.That(path, Is.Not.Null);
    }
}