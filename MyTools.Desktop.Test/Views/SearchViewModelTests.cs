using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using MyTools.Common;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Localization;
using MyTools.Desktop.Services;
using MyTools.Desktop.ViewModels;
using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class SearchViewModelTests
{
    [Test]
    public void SetForcedPlugin_SearchesEveryTime_WhenPluginAndQueryAreUnchanged()
    {
        _ = Application.Current ?? new Application();

        var keywordRegistry = new Mock<IKeywordRegistry>();
        var actionRegistry = new Mock<IActionRegistry>();
        var searcher = new Mock<ISearcher>();
        var localization = new Mock<ILocalizationService>();
        var configurationRegistry = new Mock<IConfigurationRegistry>();
        var plugin = new Mock<IPlugin>();
        plugin.SetupGet(item => item.Name).Returns("Test plugin");
        plugin.SetupGet(item => item.ViewModelType).Returns(ViewModelType.Basic);
        searcher
            .Setup(item => item.SearchAsync(plugin.Object, string.Empty, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result.CreateEmpty());

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(actionRegistry.Object);
        services.AddSingleton(searcher.Object);
        services.AddSingleton(localization.Object);
        using var serviceProvider = services.BuildServiceProvider();
        using var viewModel = new SearchViewModel(
            keywordRegistry.Object,
            serviceProvider,
            serviceProvider.GetRequiredService<ILogger<SearchViewModel>>(),
            new NodePluginDetailNavigator(),
            configurationRegistry.Object);

        viewModel.SetForcedPlugin(plugin.Object);
        viewModel.SetForcedPlugin(plugin.Object);

        Assert.That(viewModel.ForcePlugin, Is.SameAs(plugin.Object));
        searcher.Verify(
            item => item.SearchAsync(plugin.Object, string.Empty, It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }
}
