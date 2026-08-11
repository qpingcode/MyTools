using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Theming;
using MyTools.Desktop.Components;
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
            .AddSingleton<IThemeService, TestThemeService>()
            .AddSingleton<ILogger<NodePluginDetailView>>(NullLogger<NodePluginDetailView>.Instance)
            .BuildServiceProvider();
        ServiceProviderField.SetValue(null, services);
    }

    [TearDown]
    public void TearDown()
    {
        ServiceProviderField.SetValue(null, originalServiceProvider);
    }

    [Test]
    public void TitleBar_AllocatesCaptionButtonsAndBoundsLongIdentityAtMinimumWidth()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var viewModel = new PluginViewModel(services)
        {
            PluginName = "A very long plugin name that should not push caption buttons out of view",
            PluginVersion = "2026.08.11-preview-build-with-extra-metadata"
        };

        var window = new PluginWindow(viewModel);
        ArrangeWindowContent(window, PluginWindowLayoutMetrics.MinimumWindowWidth);

        var titleBarGrid = (Grid?)window.FindName("TitleBarGrid");
        var leadingDragRegion = titleBarGrid?.Children
            .OfType<Border>()
            .SingleOrDefault(child => Grid.GetColumn(child) == 0);
        var titleIdentityRegion = (Border?)window.FindName("TitleIdentityRegion");
        var pluginNameTextBlock = (TextBlock?)window.FindName("PluginNameTextBlock");
        var pluginVersionTextBlock = (TextBlock?)window.FindName("PluginVersionTextBlock");
        var captionButtonsPanel = (StackPanel?)window.FindName("CaptionButtonsPanel");

        Assert.Multiple(() =>
        {
            Assert.That(titleBarGrid, Is.Not.Null);
            Assert.That(leadingDragRegion, Is.Not.Null);
            Assert.That(titleIdentityRegion, Is.Not.Null);
            Assert.That(pluginNameTextBlock, Is.Not.Null);
            Assert.That(pluginVersionTextBlock, Is.Not.Null);
            Assert.That(captionButtonsPanel, Is.Not.Null);
            Assert.That(window.MinWidth, Is.EqualTo(PluginWindowLayoutMetrics.MinimumWindowWidth).Within(0.1));
            Assert.That(leadingDragRegion!.ActualWidth, Is.EqualTo(PluginWindowLayoutMetrics.LeadingDragRegionWidth).Within(0.5));
            Assert.That(captionButtonsPanel!.ActualWidth, Is.EqualTo(PluginWindowLayoutMetrics.CaptionButtonsWidth).Within(0.5));
            Assert.That(titleIdentityRegion!.ActualWidth, Is.EqualTo(PluginWindowLayoutMetrics.MinimumTitleIdentityRegionWidth).Within(1.0));
            Assert.That(leadingDragRegion.ActualWidth + titleIdentityRegion.ActualWidth + captionButtonsPanel.ActualWidth,
                Is.EqualTo(titleBarGrid!.ActualWidth).Within(1.0));
            Assert.That(pluginNameTextBlock!.ActualWidth, Is.LessThan(MeasureUnconstrainedWidth(pluginNameTextBlock)));
            Assert.That(pluginVersionTextBlock!.ActualWidth, Is.LessThan(MeasureUnconstrainedWidth(pluginVersionTextBlock)));
            Assert.That(pluginVersionTextBlock.ActualWidth, Is.LessThanOrEqualTo(pluginVersionTextBlock.MaxWidth).Within(0.5));
            Assert.That(MeasureRenderedTextGap(pluginNameTextBlock, pluginVersionTextBlock, titleIdentityRegion),
                Is.EqualTo(4).Within(0.5));
            Assert.That(pluginNameTextBlock.TextWrapping, Is.EqualTo(TextWrapping.NoWrap));
            Assert.That(pluginNameTextBlock.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
            Assert.That(pluginVersionTextBlock.TextWrapping, Is.EqualTo(TextWrapping.NoWrap));
            Assert.That(pluginVersionTextBlock.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
        });
    }

    [Test]
    public void TitleBar_CollapsesVersionTextAndKeepsNameBoundedWhenVersionMissing()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var viewModel = new PluginViewModel(services)
        {
            PluginName = "A very long plugin name that should stay bounded and leave a wide draggable title area",
            PluginVersion = null
        };

        var window = new PluginWindow(viewModel);
        ArrangeWindowContent(window, 1020);

        var titleIdentityRegion = (Border?)window.FindName("TitleIdentityRegion");
        var pluginNameTextBlock = (TextBlock?)window.FindName("PluginNameTextBlock");
        var pluginVersionTextBlock = (TextBlock?)window.FindName("PluginVersionTextBlock");

        Assert.Multiple(() =>
        {
            Assert.That(titleIdentityRegion, Is.Not.Null);
            Assert.That(pluginNameTextBlock, Is.Not.Null);
            Assert.That(pluginVersionTextBlock, Is.Not.Null);
            Assert.That(pluginVersionTextBlock!.Visibility, Is.EqualTo(Visibility.Collapsed));
            Assert.That(pluginNameTextBlock!.ActualWidth,
                Is.Positive.And.LessThanOrEqualTo(320));
            Assert.That(pluginNameTextBlock.TextWrapping, Is.EqualTo(TextWrapping.NoWrap));
            Assert.That(pluginNameTextBlock.TextTrimming, Is.EqualTo(TextTrimming.CharacterEllipsis));
            Assert.That(pluginNameTextBlock.ActualWidth, Is.LessThan(MeasureUnconstrainedWidth(pluginNameTextBlock)));
        });
    }

    [Test]
    public void PluginIdentity_AppearsOnlyInTitleAndUsesCompactVersionSpacing()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var viewModel = new PluginViewModel(services)
        {
            PluginName = "Settings",
            PluginVersion = "1.2.3"
        };

        var window = new PluginWindow(viewModel);
        ArrangeWindowContent(window, PluginWindowLayoutMetrics.MinimumWindowWidth);

        var pluginVersionTextBlock = (TextBlock?)window.FindName("PluginVersionTextBlock");
        var statusBarContentGrid = (Grid?)window.FindName("StatusBarContentGrid");
        var statusTextBlock = (TextBlock?)window.FindName("StatusTextBlock");
        var statusActions = (ItemsControl?)window.FindName("StatusActions");

        Assert.Multiple(() =>
        {
            Assert.That(pluginVersionTextBlock, Is.Not.Null);
            Assert.That(pluginVersionTextBlock!.Margin.Left, Is.EqualTo(4));
            Assert.That(statusBarContentGrid, Is.Not.Null);
            Assert.That(statusBarContentGrid?.ColumnDefinitions.Count, Is.EqualTo(3));
            Assert.That(statusBarContentGrid?.Children.OfType<StackPanel>(), Is.Empty);
            Assert.That(statusTextBlock, Is.Not.Null);
            Assert.That(statusTextBlock is null ? -1 : Grid.GetColumn(statusTextBlock), Is.EqualTo(1));
            Assert.That(statusActions, Is.Not.Null);
            Assert.That(statusActions is null ? -1 : Grid.GetColumn(statusActions), Is.EqualTo(2));
        });
    }

    [Test]
    public void TitleBar_PlacesVersionImmediatelyAfterPluginName()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var viewModel = new PluginViewModel(services)
        {
            PluginName = "Settings",
            PluginVersion = "1.2.3"
        };

        var window = new PluginWindow(viewModel);
        ArrangeWindowContent(window, PluginWindowLayoutMetrics.MinimumWindowWidth);

        var titleIdentityRegion = (Border?)window.FindName("TitleIdentityRegion");
        var pluginNameTextBlock = (TextBlock?)window.FindName("PluginNameTextBlock");
        var pluginVersionTextBlock = (TextBlock?)window.FindName("PluginVersionTextBlock");

        Assert.Multiple(() =>
        {
            Assert.That(titleIdentityRegion, Is.Not.Null);
            Assert.That(pluginNameTextBlock, Is.Not.Null);
            Assert.That(pluginVersionTextBlock, Is.Not.Null);
            Assert.That(MeasureRenderedTextGap(pluginNameTextBlock!, pluginVersionTextBlock!, titleIdentityRegion!),
                Is.EqualTo(4).Within(0.5));
        });
    }

    private static void ArrangeWindowContent(Window window, double width)
    {
        window.Width = width;
        window.Height = 624;

        if (window.Content is not FrameworkElement root)
        {
            Assert.Fail("PluginWindow content root was not a FrameworkElement.");
            return;
        }

        root.Measure(new Size(width, window.Height));
        root.Arrange(new Rect(0, 0, width, window.Height));
        root.UpdateLayout();
    }

    private static double MeasureUnconstrainedWidth(TextBlock source)
    {
        var measurement = new TextBlock
        {
            FontFamily = source.FontFamily,
            FontSize = source.FontSize,
            FontStretch = source.FontStretch,
            FontStyle = source.FontStyle,
            FontWeight = source.FontWeight,
            Text = source.Text
        };

        measurement.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        return measurement.DesiredSize.Width;
    }

    private static double MeasureRenderedTextGap(
        TextBlock name,
        TextBlock version,
        FrameworkElement relativeTo)
    {
        var renderedNameWidth = Math.Min(name.ActualWidth, MeasureUnconstrainedWidth(name));
        var nameRight = name.TranslatePoint(new Point(renderedNameWidth, 0), relativeTo).X;
        var versionLeft = version.TranslatePoint(new Point(0, 0), relativeTo).X;
        return versionLeft - nameRight;
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

    private sealed class TestThemeService : IThemeService
    {
        public ThemeKind CurrentTheme => ThemeKind.Dark;

        public event EventHandler<ThemeChangedEventArgs>? ThemeChanged
        {
            add { }
            remove { }
        }

        public void SetTheme(ThemeKind theme)
        {
        }
    }
}
