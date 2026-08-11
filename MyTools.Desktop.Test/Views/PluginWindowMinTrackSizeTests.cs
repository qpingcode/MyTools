using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Extensions.DependencyInjection;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Desktop.ViewModels;
using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class PluginWindowMinTrackSizeTests
{
    private const int WmGetMinMaxInfo = 0x0024;

    private static readonly FieldInfo ServiceProviderField = typeof(ServiceLocator)
        .GetField("serviceProvider", BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not find ServiceLocator.serviceProvider field.");

    private static readonly MethodInfo WndProcMethod = typeof(PluginWindow)
        .GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)
        ?? throw new InvalidOperationException("Could not find PluginWindow.WndProc.");

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
    public void WmGetMinMaxInfo_UsesMinimumTrackWidthScaledForCurrentMonitor()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var viewModel = new PluginViewModel(services);
        var window = new PluginWindow(viewModel)
        {
            ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Left = -10000,
            Top = -10000
        };

        var windowHandle = new WindowInteropHelper(window).EnsureHandle();
        var source = HwndSource.FromHwnd(windowHandle);
        var dpiScaleX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1d;
        var dpiScaleY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1d;

        var minMaxInfo = new TestMinMaxInfo();
        var buffer = Marshal.AllocHGlobal(Marshal.SizeOf<TestMinMaxInfo>());

        try
        {
            Marshal.StructureToPtr(minMaxInfo, buffer, false);
            var args = new object[] { windowHandle, WmGetMinMaxInfo, IntPtr.Zero, buffer, false };
            _ = WndProcMethod.Invoke(window, args);

            var handled = (bool)args[4];
            var updated = Marshal.PtrToStructure<TestMinMaxInfo>(buffer);

            Assert.Multiple(() =>
            {
                Assert.That(window.MinWidth, Is.EqualTo(PluginWindowLayoutMetrics.MinimumWindowWidth).Within(0.1));
                Assert.That(handled, Is.True);
                Assert.That(updated.MinTrackSize.X, Is.EqualTo((int)Math.Ceiling(PluginWindowLayoutMetrics.MinimumWindowWidth * dpiScaleX)));
                Assert.That(updated.MinTrackSize.Y, Is.EqualTo((int)Math.Ceiling(window.MinHeight * dpiScaleY)));
            });
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
            window.Close();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestMinMaxInfo
    {
        public TestPoint Reserved;
        public TestPoint MaxSize;
        public TestPoint MaxPosition;
        public TestPoint MinTrackSize;
        public TestPoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TestPoint
    {
        public int X;
        public int Y;
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
