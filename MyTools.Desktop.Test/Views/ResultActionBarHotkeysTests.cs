using System.Windows.Input;
using MyTools.Common;
using MyTools.Desktop.Components;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class ResultActionBarHotkeysTests
{
    [Test]
    public void ToHotkey_CtrlO_MatchesOverflowHotkey()
    {
        Assert.That(
            ResultActionBarHotkeys.ToHotkey(Key.O, ModifierKeys.Control),
            Is.EqualTo(Hotkey.Ctrl(HotkeyKey.O)));
    }

    [Test]
    public void ToHotkey_CtrlEnter_MatchesAdminExecuteHotkey()
    {
        Assert.That(
            ResultActionBarHotkeys.ToHotkey(Key.Enter, ModifierKeys.Control),
            Is.EqualTo(Hotkey.Ctrl(HotkeyKey.Enter)));
    }

    [Test]
    public void ToHotkey_WithoutModifier_IsTyped()
    {
        Assert.That(ResultActionBarHotkeys.ToHotkey(Key.O, ModifierKeys.None), Is.EqualTo(new Hotkey(HotkeyKey.O)));
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
