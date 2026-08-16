using MyTools.Common;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Plugins;
using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins;

[TestFixture]
public class PluginRegistryTest
{
    [Test]
    public void TryFindPlugin_ShouldMatchKeywordSeparatedByUnicodeWhitespace()
    {
        IKeywordRegistry registry = new PluginRegistry();
        var plugin = new TestPlugin();
        registry.Register("hello", plugin);

        var found = registry.TryFindPlugin($"hello\u3000", out var queryWithoutPrefix, out var matchedPlugin);

        Assert.That(found, Is.True);
        Assert.That(queryWithoutPrefix, Is.EqualTo(string.Empty));
        Assert.That(matchedPlugin, Is.SameAs(plugin));
    }

    [Test]
    public void TryFindPlugin_ShouldTrimAllLeadingWhitespaceFromQuery()
    {
        IKeywordRegistry registry = new PluginRegistry();
        var plugin = new TestPlugin();
        registry.Register("hello", plugin);

        var found = registry.TryFindPlugin("hello   world", out var queryWithoutPrefix, out var matchedPlugin);

        Assert.That(found, Is.True);
        Assert.That(queryWithoutPrefix, Is.EqualTo("world"));
        Assert.That(matchedPlugin, Is.SameAs(plugin));
    }

    [Test]
    public void TryFindPlugin_ShouldNotMatchWithoutWhitespaceSeparator()
    {
        IKeywordRegistry registry = new PluginRegistry();
        registry.Register("hello", new TestPlugin());

        var found = registry.TryFindPlugin("hello123", out _, out _);

        Assert.That(found, Is.False);
    }

    [Test]
    public void UnregisterPlugin_ShouldRemoveOnlyThatPluginKeywords()
    {
        IKeywordRegistry registry = new PluginRegistry();
        var keep = new TestPlugin();
        var drop = new TestPlugin();
        registry.Register("keep", keep);
        registry.Register("drop", drop);

        registry.UnregisterPlugin(drop);

        Assert.That(registry.TryFindPlugin("keep ", out _, out var kept), Is.True);
        Assert.That(kept, Is.SameAs(keep));
        Assert.That(registry.TryFindPlugin("drop ", out _, out _), Is.False);
    }

    private sealed class TestPlugin : IPlugin
    {
        public string PluginId => "test";
        public string Name => "Test";
        public string Description => "Test plugin";
        public List<IActionWithCommand> Actions { get; } = [];
        public bool IsEnabled => true;
        public ViewModelType ViewModelType => ViewModelType.Basic;
        public bool IsGlobalSearchPlugin => false;
        public Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
            => Task.FromResult(Result.CreateSuccessResult([]));
        public Task InitializeAsync() => Task.CompletedTask;
        public void RegisterSettings(IConfigurationRegistry configurationRegistry)
        {
        }
    }
}


