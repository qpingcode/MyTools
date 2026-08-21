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
            Assert.That(primary, Is.Null);
            Assert.That(overflow, Is.Empty);
        });
    }

    [Test]
    public void Split_SingleAction_KeepsItPrimaryWithoutOverflow()
    {
        var paste = new StubAction("Paste", Commands.DefaultCommand);

        var (primary, overflow) = ResultActionBarSplit.Split([paste]);

        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.SameAs(paste));
            Assert.That(overflow, Is.Empty);
        });
    }

    [Test]
    public void Split_AlwaysKeepsDefaultVisible_AndHidesTheRest()
    {
        var copy = new StubAction("Copy", Commands.Ctrl_D);
        var paste = new StubAction("Paste", Commands.DefaultCommand);
        var open = new StubAction("Open", Commands.Ctrl_O);

        var (primary, overflow) = ResultActionBarSplit.Split([copy, paste, open]);

        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.SameAs(paste));
            Assert.That(overflow, Is.EqualTo(new IActionWithCommand[] { copy, open }));
        });
    }

    [Test]
    public void Split_WithoutEnter_UsesFirstActionAsDefault()
    {
        var copy = new StubAction("Copy", Commands.Ctrl_D);
        var open = new StubAction("Open", Commands.Ctrl_O);

        var (primary, overflow) = ResultActionBarSplit.Split([copy, open]);

        Assert.Multiple(() =>
        {
            Assert.That(primary, Is.SameAs(copy));
            Assert.That(overflow, Is.EqualTo(new IActionWithCommand[] { open }));
        });
    }

    private sealed class StubAction(string name, string command) : IActionWithCommand
    {
        public string Name { get; } = name;
        public string Description => "";
        public string Command { get; } = command;

        public Task<ActionResult> ExecuteAsync(IActionParams args) =>
            Task.FromResult(ActionResult.CreateSuccess("ok"));
    }
}
