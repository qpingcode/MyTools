using System.Windows.Input;
using MyTools.Common;
using MyTools.Desktop.Components;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class ResultActionBarHotkeysTests
{
    [Test]
    public void ToCommand_CtrlO_MatchesOverflowHotkey()
    {
        Assert.That(
            ResultActionBarHotkeys.ToCommand(Key.O, ModifierKeys.Control),
            Is.EqualTo(Commands.Ctrl_O));
    }

    [Test]
    public void ToCommand_CtrlEnter_MatchesAdminExecuteHotkey()
    {
        Assert.That(
            ResultActionBarHotkeys.ToCommand(Key.Enter, ModifierKeys.Control),
            Is.EqualTo(Commands.Ctrl_Enter));
    }

    [Test]
    public void ToCommand_WithoutCtrl_IsIgnored()
    {
        Assert.That(ResultActionBarHotkeys.ToCommand(Key.O, ModifierKeys.None), Is.Null);
    }

    [Test]
    public void IsOverflowToggle_CtrlK()
    {
        Assert.Multiple(() =>
        {
            Assert.That(ResultActionBarHotkeys.IsOverflowToggle(Key.K, Key.None, ModifierKeys.Control), Is.True);
            Assert.That(ResultActionBarHotkeys.IsOverflowToggle(Key.O, Key.None, ModifierKeys.Control), Is.False);
            Assert.That(ResultActionBarHotkeys.IsCtrlKey(Key.LeftCtrl), Is.True);
            Assert.That(ResultActionBarHotkeys.IsCtrlKey(Key.O), Is.False);
        });
    }
}
