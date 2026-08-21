using MyTools.Desktop.Converters;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Converters;

[TestFixture]
public class HotkeyPartsConverterTests
{
    [Test]
    public void Parse_Enter_IsReturnIconPart()
    {
        var parts = HotkeyPartsConverter.Parse("Enter");

        Assert.That(parts, Has.Count.EqualTo(1));
        Assert.That(parts[0].IsReturn, Is.True);
        Assert.That(parts[0].MdiName, Is.EqualTo("mdi-keyboard-return"));
    }

    [Test]
    public void Parse_CtrlV_SplitsIntoKeycaps()
    {
        var parts = HotkeyPartsConverter.Parse("Ctrl+V");

        Assert.Multiple(() =>
        {
            Assert.That(parts, Has.Count.EqualTo(2));
            Assert.That(parts[0].Text, Is.EqualTo("Ctrl"));
            Assert.That(parts[0].IsReturn, Is.False);
            Assert.That(parts[1].Text, Is.EqualTo("V"));
            Assert.That(parts[1].IsReturn, Is.False);
        });
    }

    [Test]
    public void Parse_CtrlEnter_ShowsCtrlAndReturn()
    {
        var parts = HotkeyPartsConverter.Parse("Ctrl+Enter");

        Assert.Multiple(() =>
        {
            Assert.That(parts, Has.Count.EqualTo(2));
            Assert.That(parts[0].Text, Is.EqualTo("Ctrl"));
            Assert.That(parts[1].IsReturn, Is.True);
        });
    }

    [Test]
    public void Parse_NodeAction_HasNoKeycaps()
    {
        Assert.That(HotkeyPartsConverter.Parse("NodeAction:1"), Is.Empty);
    }

    [Test]
    public void ToGestureText_HidesPluginActionIds()
    {
        Assert.Multiple(() =>
        {
            Assert.That(HotkeyPartsConverter.ToGestureText("Ctrl+O"), Is.EqualTo("Ctrl+O"));
            Assert.That(HotkeyPartsConverter.ToGestureText("NodeAction:1"), Is.Empty);
        });
    }
}
