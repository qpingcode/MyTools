using System.Text.Json;
using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Actions;

[TestFixture]
public class RunCommandActionScriptsTest
{
    [Test]
    public void ReadScripts_ShouldAcceptStringAndArray()
    {
        using var stringDocument = JsonDocument.Parse("\"echo one\\necho two\"");
        Assert.That(
            RunCommandAction.ReadScripts(stringDocument.RootElement),
            Is.EqualTo(new[] { "echo one", "echo two" }).AsCollection);

        using var arrayDocument = JsonDocument.Parse("""["echo one","echo two"]""");
        Assert.That(
            RunCommandAction.ReadScripts(arrayDocument.RootElement),
            Is.EqualTo(new[] { "echo one", "echo two" }).AsCollection);
    }

    [Test]
    public void SplitScriptLines_ShouldIgnoreBlankLines()
    {
        Assert.That(RunCommandAction.SplitScriptLines("echo one\n\n  \necho two\r\n"),
            Is.EqualTo(new[] { "echo one", "echo two" }).AsCollection);
        Assert.That(RunCommandAction.SplitScriptLines(null), Is.Empty);
    }
}
