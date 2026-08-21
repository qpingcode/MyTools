using NUnit.Framework;
using System.Windows;

namespace MyTools.Desktop.Test;

[SetUpFixture]
[Apartment(ApartmentState.STA)]
internal sealed class TestEnvironment
{
    private Application? application;

    [OneTimeSetUp]
    public void InitializeWpfTestHost()
    {
        EnsureWindowsDirectoryEnvironmentVariable();

        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = typeof(App).Assembly;
        }

        if (Application.Current == null)
        {
            application = new Application();
            application.Resources.MergedDictionaries.Add(LoadDesktopResource("Themes/Shared.xaml"));
            application.Resources.MergedDictionaries.Add(LoadDesktopResource("Themes/Dark.xaml"));
        }
    }

    private static ResourceDictionary LoadDesktopResource(string relativePath)
        => new()
        {
            Source = new Uri(
                $"pack://application:,,,/MyTools.Desktop;component/{relativePath}",
                UriKind.Absolute)
        };

    private static void EnsureWindowsDirectoryEnvironmentVariable()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("windir")))
        {
            return;
        }

        var windowsDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windowsDirectory))
        {
            Environment.SetEnvironmentVariable("windir", windowsDirectory);
        }
    }
}
