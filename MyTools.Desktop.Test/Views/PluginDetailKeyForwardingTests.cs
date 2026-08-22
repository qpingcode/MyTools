using MyTools.Desktop.Views;
using NUnit.Framework;
using System.Windows.Controls;
using System.Windows.Input;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class PluginDetailKeyForwardingTests
{
    [Test]
    public void CanForward_WhenFocusedElementIsNotButton_ReturnsTrue()
    {
        Assert.That(
            PluginDetailKeyForwarding.CanForward(new Border(), isPluginContentFocused: false),
            Is.True);
    }

    [Test]
    public void CanForward_WhenFocusedElementIsButton_ReturnsFalse()
    {
        Assert.That(
            PluginDetailKeyForwarding.CanForward(new Button(), isPluginContentFocused: false),
            Is.False);
    }

    [Test]
    public void CanForward_WhenPluginContentAlreadyHasFocus_ReturnsFalse()
    {
        Assert.That(
            PluginDetailKeyForwarding.CanForward(new Border(), isPluginContentFocused: true),
            Is.False);
    }

    [Test]
    public void ResolveHostKey_MapsPlainAndShiftedEnter()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                PluginDetailKeyForwarding.ResolveHostKey(Key.Enter, ModifierKeys.None),
                Is.EqualTo(PluginDetailKeyForwarding.Enter));
            Assert.That(
                PluginDetailKeyForwarding.ResolveHostKey(Key.Enter, ModifierKeys.Shift),
                Is.EqualTo(PluginDetailKeyForwarding.ShiftEnter));
        });
    }

    [TestCase(Key.Enter, ModifierKeys.Control)]
    [TestCase(Key.Enter, ModifierKeys.Alt)]
    [TestCase(Key.Enter, ModifierKeys.Shift | ModifierKeys.Control)]
    [TestCase(Key.Tab, ModifierKeys.None)]
    [TestCase(Key.A, ModifierKeys.None)]
    public void ResolveHostKey_IgnoresEverythingElse(Key key, ModifierKeys modifiers)
    {
        Assert.That(PluginDetailKeyForwarding.ResolveHostKey(key, modifiers), Is.Null);
    }

    [Test]
    public void IsFocusIntoPageKey_OnlyMatchesUnmodifiedTab()
    {
        Assert.Multiple(() =>
        {
            Assert.That(
                PluginDetailKeyForwarding.IsFocusIntoPageKey(Key.Tab, ModifierKeys.None),
                Is.True);
            Assert.That(
                PluginDetailKeyForwarding.IsFocusIntoPageKey(Key.Tab, ModifierKeys.Shift),
                Is.False);
            Assert.That(
                PluginDetailKeyForwarding.IsFocusIntoPageKey(Key.Enter, ModifierKeys.None),
                Is.False);
        });
    }
}
