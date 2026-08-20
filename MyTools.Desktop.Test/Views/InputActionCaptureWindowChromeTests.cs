using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Common.Theming;
using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class InputActionCaptureWindowChromeTests
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
            .BuildServiceProvider();
        ServiceProviderField.SetValue(null, services);
    }

    [TearDown]
    public void TearDown()
    {
        ServiceProviderField.SetValue(null, originalServiceProvider);
    }

    [Test]
    public void CloseButton_SitsFlushToTopRightAndKeepsWindowCornerRadius()
    {
        var window = new InputActionCaptureWindow(new TestLocalizationService(), new TestThemeService());
        ArrangeWindowContent(window, 480);

        var windowFrame = (Border?)window.FindName("WindowFrame");
        var titleBarGrid = (Grid?)window.FindName("TitleBarGrid");
        var closeButton = (Button?)window.FindName("CloseButton");
        Assert.That(windowFrame, Is.Not.Null);
        Assert.That(titleBarGrid, Is.Not.Null);
        Assert.That(closeButton, Is.Not.Null);

        closeButton!.ApplyTemplate();
        var closeButtonBorder = closeButton.Template.FindName("CloseButtonBorder", closeButton) as Border;

        Assert.Multiple(() =>
        {
            Assert.That(windowFrame!.CornerRadius, Is.EqualTo(new CornerRadius(12)));
            Assert.That(titleBarGrid!.TranslatePoint(new Point(0, 0), windowFrame).Y, Is.EqualTo(0).Within(0.5));
            Assert.That(closeButton.TranslatePoint(new Point(0, 0), windowFrame).Y, Is.EqualTo(0).Within(0.5));
            Assert.That(MeasureRightGap(closeButton, windowFrame), Is.EqualTo(0).Within(0.5));
            Assert.That(closeButtonBorder, Is.Not.Null);
            Assert.That(closeButtonBorder!.CornerRadius, Is.EqualTo(new CornerRadius(0, 12, 0, 0)));
        });
    }

    private static void ArrangeWindowContent(Window window, double width)
    {
        window.Width = width;
        window.Height = 320;

        if (window.Content is not FrameworkElement root)
        {
            Assert.Fail("InputActionCaptureWindow content root was not a FrameworkElement.");
            return;
        }

        root.Measure(new Size(width, window.Height));
        root.Arrange(new Rect(0, 0, width, window.Height));
        root.UpdateLayout();
    }

    private static double MeasureRightGap(FrameworkElement inner, FrameworkElement outer)
    {
        var innerRight = inner.TranslatePoint(new Point(inner.ActualWidth, 0), outer).X;
        return outer.ActualWidth - innerRight;
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
