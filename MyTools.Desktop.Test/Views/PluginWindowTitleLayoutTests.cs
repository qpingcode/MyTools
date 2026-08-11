using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Desktop.ViewModels;
using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class PluginWindowTitleLayoutTests
{
    private static readonly FieldInfo ServiceProviderField = typeof(ServiceLocator)
        .GetField("serviceProvider", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not find ServiceLocator.serviceProvider field.");

    private IServiceProvider? originalServiceProvider;

    [SetUp]
    public void SetUp()
    {
        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = typeof(MyTools.Desktop.App).Assembly;
        }

        originalServiceProvider = (IServiceProvider?)ServiceProviderField.GetValue(null);
        var services = new ServiceCollection()
            .AddSingleton<ILocalizationService, TestLocalizationService>()
            .BuildServiceProvider();
        ServiceProviderField.SetValue(null, services);
    }

    [TearDown]
    public void TearDown()
    {
        ServiceProviderField.SetValue(null, originalServiceProvider);
    }

    [Test]
    public void TitleBar_UsesBoundedIdentityRegionAndPreservesCaptionButtons()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var viewModel = new PluginViewModel(services)
        {
            PluginName = "A very long plugin name that should not push caption buttons out of view",
            PluginVersion = "2026.08.11-preview-build-with-extra-metadata"
        };

        var window = new PluginWindow(viewModel);
        var titleBarGrid = (Grid?)window.FindName("TitleBarGrid");
        var titleIdentityRegion = (Border?)window.FindName("TitleIdentityRegion");
        var pluginNameTextBlock = (TextBlock?)window.FindName("PluginNameTextBlock");
        var pluginVersionTextBlock = (TextBlock?)window.FindName("PluginVersionTextBlock");
        var captionButtonsPanel = (StackPanel?)window.FindName("CaptionButtonsPanel");

        Assert.Multiple(() =>
        {
            Assert.That(titleBarGrid, Is.Not.Null);
            Assert.That(titleIdentityRegion, Is.Not.Null);
            Assert.That(pluginNameTextBlock, Is.Not.Null);
            Assert.That(pluginVersionTextBlock, Is.Not.Null);
            Assert.That(captionButtonsPanel, Is.Not.Null);

            Assert.That(titleBarGrid!.ColumnDefinitions.Select(column => column.Width).ToArray(), Is.EqualTo(new[]
            {
                GridLength.Auto,
                new GridLength(1, GridUnitType.Star),
                GridLength.Auto
            }));

            Assert.That(Grid.GetColumn(titleIdentityRegion!), Is.EqualTo(1));
            Assert.That(Grid.GetColumn(captionButtonsPanel!), Is.EqualTo(2));

            Assert.That(pluginNameTextBlock!.TextWrapping, Is.EqualTo(TextWrapping.NoWrap));
            Assert.That(pluginNameTextBlock.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
            Assert.That(pluginVersionTextBlock!.TextWrapping, Is.EqualTo(TextWrapping.NoWrap));
            Assert.That(pluginVersionTextBlock.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
            Assert.That(pluginVersionTextBlock.MaxWidth, Is.EqualTo(160).Within(0.1));
        });
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public string CurrentLocale => "en-US";

        public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null)
            => defaultValue;

        public event EventHandler<LocaleChangedEventArgs>? LocaleChanged
        {
            add { }
            remove { }
        }
    }
}
