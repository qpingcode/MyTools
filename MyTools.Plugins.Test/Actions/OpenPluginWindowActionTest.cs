using MyTools.Common;
using MyTools.Plugins;
using MyTools.Plugins.Param;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Actions;

[TestFixture]
public class OpenPluginWindowActionTest
{
    [Test]
    public async Task ExecuteAsync_EmptyId_Fails()
    {
        var action = new OpenPluginWindowAction(new FakeLauncher(PluginLaunchKind.PluginWindow));
        var result = await action.ExecuteAsync(ActionStringParam.From("  "));
        Assert.That(result.Success, Is.False);
        Assert.That(result.ActionType, Is.EqualTo(ActionTypeEnum.None));
    }

    [Test]
    public async Task ExecuteAsync_PluginWindow_ClosesSearch()
    {
        var launcher = new FakeLauncher(PluginLaunchKind.PluginWindow);
        var action = new OpenPluginWindowAction(launcher);
        var result = await action.ExecuteAsync(ActionStringParam.From("settings:settings"));
        Assert.That(launcher.LastPluginId, Is.EqualTo("settings:settings"));
        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionType, Is.EqualTo(ActionTypeEnum.Close));
    }

    [Test]
    public async Task ExecuteAsync_SearchWindow_KeepsSearchOpen()
    {
        var launcher = new FakeLauncher(PluginLaunchKind.SearchWindow);
        var action = new OpenPluginWindowAction(launcher);
        var result = await action.ExecuteAsync(ActionStringParam.From("calculator:calculator"));
        Assert.That(result.Success, Is.True);
        Assert.That(result.ActionType, Is.EqualTo(ActionTypeEnum.None));
    }

    [Test]
    public async Task ExecuteAsync_NotFound_Fails()
    {
        var action = new OpenPluginWindowAction(new FakeLauncher(PluginLaunchKind.NotFound));
        var result = await action.ExecuteAsync(ActionStringParam.From("missing"));
        Assert.That(result.Success, Is.False);
    }

    private sealed class FakeLauncher : IPluginLauncher
    {
        private readonly PluginLaunchKind kind;

        public FakeLauncher(PluginLaunchKind kind)
        {
            this.kind = kind;
        }

        public string? LastPluginId { get; private set; }

        public PluginLaunchKind Open(string pluginId)
        {
            LastPluginId = pluginId;
            return kind;
        }

        public PluginLaunchKind Open(IPlugin plugin) => kind;
    }
}
