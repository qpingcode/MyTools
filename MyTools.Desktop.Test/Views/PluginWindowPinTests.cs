using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System.Globalization;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Plugins;
using MyTools.Common.Theming;
using MyTools.Desktop.Components;
using MyTools.Desktop.Services;
using MyTools.Desktop.ViewModels;
using MyTools.Desktop.Views;
using MyTools.Host.Core.Bus;
using MyTools.Host.Core.Capabilities;
using MyTools.Host.Core.Sessions;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class PluginWindowPinTests
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
        var bus = new MessageBus();
        var services = new ServiceCollection()
            .AddSingleton<ILocalizationService, TestLocalizationService>()
            .AddSingleton<IThemeService, TestThemeService>()
            .AddSingleton<ILogger<NodePluginDetailView>>(NullLogger<NodePluginDetailView>.Instance)
            .AddSingleton(bus)
            .AddSingleton(new PluginSessionManager(bus, new CapabilityGateway(), new UnusedProcessFactory()))
            .BuildServiceProvider();
        ServiceProviderField.SetValue(null, services);
    }

    [TearDown]
    public void TearDown()
    {
        ServiceProviderField.SetValue(null, originalServiceProvider);
    }

    [Test]
    public void TitleBar_PlacesPinButtonBeforeMinimize()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var window = new PluginWindow(new PluginViewModel(services));
        window.Measure(new Size(1020, 624));
        window.Arrange(new Rect(0, 0, 1020, 624));
        window.UpdateLayout();

        var captionButtonsPanel = (StackPanel?)window.FindName("CaptionButtonsPanel");
        var pinButton = (Button?)window.FindName("PinButton");

        Assert.Multiple(() =>
        {
            Assert.That(captionButtonsPanel, Is.Not.Null);
            Assert.That(pinButton, Is.Not.Null);
            Assert.That(captionButtonsPanel!.Children[0], Is.SameAs(pinButton));
            Assert.That(PluginWindowLayoutMetrics.CaptionButtonCount, Is.EqualTo(4));
        });
    }

    [Test]
    public void SetPinned_DocksImmediatelyAndUnpinRemovesTile()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var dock = CreateDock();
        var window = new PluginWindow(
            new PluginViewModel(services),
            NullLogger<PluginWindow>.Instance,
            dock);
        window.BindPluginIdentity("api-tester", "API Tester");

        window.SetPinned(true);

        Assert.Multiple(() =>
        {
            Assert.That(window.IsPinned, Is.True);
            Assert.That(dock.Items, Has.Count.EqualTo(1));
            Assert.That(dock.Items[0].PluginId.Value, Is.EqualTo("api-tester"));
            Assert.That(dock.Items[0].DisplayName, Is.EqualTo("API Tester"));
            Assert.That(dock.Items[0].IsOpen, Is.False);
            Assert.That(dock.Items[0].ActionCaption, Is.EqualTo("Show API Tester"));
        });

        window.SetPinned(false);

        Assert.Multiple(() =>
        {
            Assert.That(window.IsPinned, Is.False);
            Assert.That(dock.Items, Is.Empty);
        });
    }

    [Test]
    public void TryHidePinnedWindow_WhenPinned_HidesAndKeepsDockTile()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var dock = CreateDock();
        var window = new PluginWindow(
            new PluginViewModel(services),
            NullLogger<PluginWindow>.Instance,
            dock);
        window.BindPluginIdentity("api-tester", "API Tester");
        window.SetPinned(true);

        var hidden = window.TryHidePinnedWindow();

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.True);
            Assert.That(window.IsVisible, Is.False);
            Assert.That(window.IsWindowOpen, Is.False);
            Assert.That(dock.Items, Has.Count.EqualTo(1));
            Assert.That(dock.Items[0].IsOpen, Is.False);
            Assert.That(dock.Items[0].ActionCaption, Is.EqualTo("Show API Tester"));
        });
    }

    [Test]
    public void Toggle_WhenWindowIsHidden_KeepsDockOrder()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var dock = CreateDock();
        var first = new PluginWindow(
            new PluginViewModel(services),
            NullLogger<PluginWindow>.Instance,
            dock);
        first.BindPluginIdentity("api-tester", "API Tester");
        var second = new PluginWindow(
            new PluginViewModel(services),
            NullLogger<PluginWindow>.Instance,
            dock);
        second.BindPluginIdentity("settings", "Settings");
        first.SetPinned(true);
        second.SetPinned(true);

        first.HideFromDock();
        dock.Toggle(new PluginId("api-tester"));

        Assert.Multiple(() =>
        {
            Assert.That(dock.Items, Has.Count.EqualTo(2));
            Assert.That(dock.Items[0].PluginId.Value, Is.EqualTo("api-tester"));
            Assert.That(dock.Items[1].PluginId.Value, Is.EqualTo("settings"));
        });
    }

    [Test]
    public void TryHidePinnedWindow_WhenNotPinned_DoesNotHide()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var window = new PluginWindow(new PluginViewModel(services));
        window.BindPluginIdentity("api-tester", "API Tester");

        var hidden = window.TryHidePinnedWindow();

        Assert.Multiple(() =>
        {
            Assert.That(hidden, Is.False);
            Assert.That(window.IsPinned, Is.False);
        });
    }

    [Test]
    public void CloseAll_ClosesWindowsAndRemovesDockTiles()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var dock = CreateDock();
        var first = CreatePinnedWindow(services, dock, "api-tester", "API Tester");
        var second = CreatePinnedWindow(services, dock, "settings", "Settings");

        dock.CloseAll();

        Assert.Multiple(() =>
        {
            Assert.That(dock.Items, Is.Empty);
            Assert.That(first.IsPinned, Is.True);
            Assert.That(second.IsPinned, Is.True);
        });
    }

    [Test]
    public void HideAll_HidesWindowsAndKeepsDockTiles()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var dock = CreateDock();
        var first = CreatePinnedWindow(services, dock, "api-tester", "API Tester");
        var second = CreatePinnedWindow(services, dock, "settings", "Settings");

        dock.HideAll();

        Assert.Multiple(() =>
        {
            Assert.That(dock.Items, Has.Count.EqualTo(2));
            Assert.That(first.IsVisible, Is.False);
            Assert.That(second.IsVisible, Is.False);
            Assert.That(dock.Items[0].IsOpen, Is.False);
            Assert.That(dock.Items[1].IsOpen, Is.False);
            Assert.That(first.IsPinned, Is.True);
            Assert.That(second.IsPinned, Is.True);
        });
    }

    [Test]
    public void ShowAll_ShowsWindowsAndKeepsDockTiles()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var dock = CreateDock();
        var first = CreatePinnedWindow(services, dock, "api-tester", "API Tester");
        var second = CreatePinnedWindow(services, dock, "settings", "Settings");
        dock.HideAll();

        dock.ShowAll();

        Assert.Multiple(() =>
        {
            Assert.That(dock.Items, Has.Count.EqualTo(2));
            Assert.That(first.IsVisible, Is.True);
            Assert.That(second.IsVisible, Is.True);
            Assert.That(dock.Items[0].IsOpen, Is.True);
            Assert.That(dock.Items[1].IsOpen, Is.True);
            Assert.That(first.IsPinned, Is.True);
            Assert.That(second.IsPinned, Is.True);
        });

        dock.CloseAll();
    }

    [Test]
    public void ArrangeWindows_MatchesFirstSizeAndCascadesByStep()
    {
        using var services = new ServiceCollection().BuildServiceProvider();
        var dock = CreateDock();
        var first = CreatePinnedWindow(services, dock, "api-tester", "API Tester");
        var second = CreatePinnedWindow(services, dock, "settings", "Settings");
        var third = CreatePinnedWindow(services, dock, "store", "Store");
        first.Left = 120;
        first.Top = 80;
        first.Width = 480;
        first.Height = 360;
        second.Left = 10;
        second.Top = 10;
        second.Width = 200;
        second.Height = 180;
        third.Left = 40;
        third.Top = 50;
        third.Width = 220;
        third.Height = 190;

        dock.ArrangeWindows();

        Assert.Multiple(() =>
        {
            Assert.That(first.Left, Is.EqualTo(120));
            Assert.That(first.Top, Is.EqualTo(80));
            Assert.That(first.Width, Is.EqualTo(480));
            Assert.That(first.Height, Is.EqualTo(360));
            Assert.That(second.Left, Is.EqualTo(120 + PluginDockLayoutMetrics.ArrangeCascadeStep));
            Assert.That(second.Top, Is.EqualTo(80 + PluginDockLayoutMetrics.ArrangeCascadeStep));
            Assert.That(second.Width, Is.EqualTo(480));
            Assert.That(second.Height, Is.EqualTo(360));
            Assert.That(third.Left, Is.EqualTo(120 + PluginDockLayoutMetrics.ArrangeCascadeStep * 2));
            Assert.That(third.Top, Is.EqualTo(80 + PluginDockLayoutMetrics.ArrangeCascadeStep * 2));
            Assert.That(third.Width, Is.EqualTo(480));
            Assert.That(third.Height, Is.EqualTo(360));
            Assert.That(dock.Items, Has.Count.EqualTo(3));
            Assert.That(dock.Items[0].PluginId.Value, Is.EqualTo("api-tester"));
            Assert.That(dock.Items[1].PluginId.Value, Is.EqualTo("settings"));
            Assert.That(dock.Items[2].PluginId.Value, Is.EqualTo("store"));
        });

        dock.CloseAll();
    }

    private static PluginWindow CreatePinnedWindow(
        ServiceProvider services,
        PluginDockManager dock,
        string pluginId,
        string displayName)
    {
        var window = new PluginWindow(
            new PluginViewModel(services),
            NullLogger<PluginWindow>.Instance,
            dock);
        window.BindPluginIdentity(pluginId, displayName);
        window.SetPinned(true);
        return window;
    }

    private static PluginDockManager CreateDock()
    {
        return new PluginDockManager(
            new WindowPlacementService(NullLogger<WindowPlacementService>.Instance),
            new TestLocalizationService());
    }

    private sealed class TestLocalizationService : ILocalizationService
    {
        public string CurrentLocale => "en-US";

        public string GetCaption(string key, string defaultValue, object? values = null, string? translatorComment = null)
        {
            return LocalizedMessage.Format(
                defaultValue,
                LocalizedMessage.ToDictionary(values),
                CultureInfo.InvariantCulture);
        }

        public event EventHandler<LocaleChangedEventArgs>? LocaleChanged
        {
            add { }
            remove { }
        }
    }

    private sealed class UnusedProcessFactory : INodeProcessControllerFactory
    {
        public INodeProcessController Create(string nodeExePath, string nodeEntryFullPath)
            => throw new NotSupportedException("Pin tests do not start Node processes.");
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
