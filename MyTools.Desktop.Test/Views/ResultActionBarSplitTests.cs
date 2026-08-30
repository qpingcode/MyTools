using MyTools.Common;
using MyTools.Desktop.Components;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class ResultActionBarSplitTests
{
    [Test]
    public void Split_Empty_HasNoPrimary()
    {
        var (primary, overflow) = ResultActionBarSplit.Split([]);

        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.Empty);
            Assert.That(overflow, Is.Empty);
        });
    }

    [Test]
    public void Split_SingleAction_KeepsItPrimaryWithoutOverflow()
    {
        var paste = new StubAction("Paste", Hotkey.Enter);

        var (primary, overflow) = ResultActionBarSplit.Split([paste]);

        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.EqualTo(new IActionWithHotkey[] { paste }));
            Assert.That(overflow, Is.Empty);
        });
    }

    [Test]
    public void Split_AlwaysKeepsDefaultVisible_AndHidesTheRest()
    {
        var copy = new StubAction("Copy", Hotkey.Ctrl(HotkeyKey.D));
        var paste = new StubAction("Paste", Hotkey.Enter);
        var open = new StubAction("Open", Hotkey.Ctrl(HotkeyKey.O));

        var (primary, overflow) = ResultActionBarSplit.Split([copy, paste, open]);

        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.EqualTo(new IActionWithHotkey[] { paste }));
            Assert.That(overflow, Is.EqualTo(new IActionWithHotkey[] { copy, open }));
        });
    }

    [Test]
    public void Split_WithoutEnter_UsesFirstActionAsDefault()
    {
        var copy = new StubAction("Copy", Hotkey.Ctrl(HotkeyKey.D));
        var open = new StubAction("Open", Hotkey.Ctrl(HotkeyKey.O));

        var (primary, overflow) = ResultActionBarSplit.Split([copy, open]);

        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.EqualTo(new IActionWithHotkey[] { copy }));
            Assert.That(overflow, Is.EqualTo(new IActionWithHotkey[] { open }));
        });
    }

    [Test]
    public void Split_PinnedActions_StayVisibleTogether_AndUnpinnedGoToOverflow()
    {
        var encode = new StubAction("Encode", Hotkey.Ctrl(HotkeyKey.E), pinned: true);
        var decode = new StubAction("Decode", Hotkey.Ctrl(HotkeyKey.D), pinned: true);
        var copy = new StubAction("Copy", Hotkey.Ctrl(HotkeyKey.C));
        var clear = new StubAction("Clear", Hotkey.Ctrl(HotkeyKey.L));

        var (primary, overflow) = ResultActionBarSplit.Split([encode, decode, copy, clear]);

        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.EqualTo(new IActionWithHotkey[] { encode, decode }));
            Assert.That(overflow, Is.EqualTo(new IActionWithHotkey[] { copy, clear }));
        });
    }

    [Test]
    public void Split_PinnedActions_ReplaceTheSingleDefaultEvenWhenEnterExists()
    {
        var encode = new StubAction("Encode", Hotkey.Ctrl(HotkeyKey.E), pinned: true);
        var paste = new StubAction("Paste", Hotkey.Enter);
        var open = new StubAction("Open", Hotkey.Ctrl(HotkeyKey.O));

        var (primary, overflow) = ResultActionBarSplit.Split([encode, paste, open]);

        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.EqualTo(new IActionWithHotkey[] { encode }));
            Assert.That(overflow, Is.EqualTo(new IActionWithHotkey[] { paste, open }));
        });
    }

    private sealed class StubAction(string name, Hotkey hotkey, bool pinned = false) : IActionWithHotkey
    {
        public string Name { get; } = name;
        public string Description => "";
        public Hotkey Hotkey { get; } = hotkey;
        public bool Pinned { get; } = pinned;

        public Task<ActionResult> ExecuteAsync(IActionParams args) =>
            Task.FromResult(ActionResult.CreateSuccess("ok"));
    }
}
