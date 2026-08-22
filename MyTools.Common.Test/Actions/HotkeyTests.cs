using MyTools.Common;
using NUnit.Framework;

namespace MyTools.Common.Test.Actions;

[TestFixture]
public class HotkeyTests
{
    [Test]
    public void ToString_UsesStableModifierOrderAndDisplayKey()
    {
        var hotkey = new Hotkey(
            HotkeyKey.D7,
            HotkeyModifiers.Shift | HotkeyModifiers.Control | HotkeyModifiers.Alt);

        Assert.That(hotkey.ToString(), Is.EqualTo("Ctrl+Alt+Shift+7"));
    }

    [TestCase("M", 1, HotkeyKey.M, HotkeyModifiers.Control)]
    [TestCase("enter", 0, HotkeyKey.Enter, HotkeyModifiers.None)]
    public void TryParse_AcceptsOnlyDeclaredKeysAndModifierBits(
        string key,
        int modifiers,
        HotkeyKey expectedKey,
        HotkeyModifiers expectedModifiers)
    {
        Assert.That(Hotkey.TryParse(key, modifiers, out var hotkey), Is.True);
        Assert.That(hotkey, Is.EqualTo(new Hotkey(expectedKey, expectedModifiers)));
    }

    [TestCase("BrowserBack", 0)]
    [TestCase("M", 8)]
    [TestCase("None", 0)]
    public void TryParse_RejectsKeysAndModifierBitsOutsideTheContract(string key, int modifiers)
    {
        Assert.That(Hotkey.TryParse(key, modifiers, out var hotkey), Is.False);
        Assert.That(hotkey, Is.EqualTo(Hotkey.None));
    }
}
