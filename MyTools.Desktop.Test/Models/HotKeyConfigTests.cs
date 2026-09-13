using System.Windows.Input;
using MyTools.Desktop.Models;
using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Models;

[TestFixture]
public class HotKeyConfigTests
{
    [Test]
    public void Parse_ShouldReadDefaultSearchHotKey()
    {
        var hotKey = new HotKeyConfig("Alt+Space");

        Assert.That(hotKey.Key, Is.EqualTo(Key.Space));
        Assert.That(hotKey.Modifiers, Is.EqualTo(ModifierKeys.Alt));
    }

    [Test]
    public void Parse_ShouldReadDefaultDetachPluginWindowHotKey()
    {
        var hotKey = new HotKeyConfig(GeneralSettings.DefaultDetachPluginWindowHotKey);

        Assert.That(hotKey.Key, Is.EqualTo(Key.Q));
        Assert.That(hotKey.Modifiers, Is.EqualTo(ModifierKeys.Alt));
    }

    [Test]
    public void Parse_ShouldMapCtrlAliasToControl()
    {
        var hotKey = new HotKeyConfig("Ctrl+Shift+S");

        Assert.That(hotKey.Key, Is.EqualTo(Key.S));
        Assert.That(hotKey.Modifiers, Is.EqualTo(ModifierKeys.Control | ModifierKeys.Shift));
    }
}
