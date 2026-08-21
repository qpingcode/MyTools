using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using Microsoft.Extensions.DependencyInjection;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Desktop.Components;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(ApartmentState.STA)]
public class ResultActionBarOverflowChromeTests
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
    public void OverflowMenu_DoesNotUseSystemIconGutter()
    {
        var menu = (ContextMenu?)new ResultActionBar().FindName("OverflowMenu");

        Assert.That(menu, Is.Not.Null);
        Assert.That(menu!.OverridesDefaultStyle, Is.True);
        Assert.That(menu.Template, Is.Not.Null);

        var root = menu.Template.LoadContent();
        Assert.That(FindRectangles(root), Is.Empty, "Default ContextMenu chrome draws a 28px white icon gutter Rectangle.");
    }

    [Test]
    public void OverflowMenuItems_UseKeycapHotkeys()
    {
        var menu = (ContextMenu?)new ResultActionBar().FindName("OverflowMenu");
        Assert.That(menu?.ItemContainerStyle, Is.Not.Null);

        var templateSetter = menu!.ItemContainerStyle.Setters
            .OfType<Setter>()
            .FirstOrDefault(setter => setter.Property == Control.TemplateProperty);
        Assert.That(templateSetter?.Value, Is.InstanceOf<ControlTemplate>());

        var root = ((ControlTemplate)templateSetter!.Value!).LoadContent();
        Assert.That(FindHotkeyKeycaps(root), Is.Not.Empty);
    }

    private static List<HotkeyKeycaps> FindHotkeyKeycaps(DependencyObject root)
    {
        var found = new List<HotkeyKeycaps>();
        WalkHotkeyKeycaps(root, found);
        return found;
    }

    private static void WalkHotkeyKeycaps(DependencyObject current, List<HotkeyKeycaps> found)
    {
        if (current is HotkeyKeycaps keycaps)
        {
            found.Add(keycaps);
        }

        foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
        {
            WalkHotkeyKeycaps(child, found);
        }
    }

    private static List<Rectangle> FindRectangles(DependencyObject root)
    {
        var found = new List<Rectangle>();
        Walk(root, found);
        return found;
    }

    private static void Walk(DependencyObject current, List<Rectangle> found)
    {
        if (current is Rectangle rectangle)
        {
            found.Add(rectangle);
        }

        var count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(current);
        if (count > 0)
        {
            for (var i = 0; i < count; i++)
            {
                Walk(System.Windows.Media.VisualTreeHelper.GetChild(current, i), found);
            }

            return;
        }

        foreach (var child in LogicalTreeHelper.GetChildren(current).OfType<DependencyObject>())
        {
            Walk(child, found);
        }
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
