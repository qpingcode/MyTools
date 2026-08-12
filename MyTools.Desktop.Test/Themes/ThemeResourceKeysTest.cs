using System.IO;
using System.Windows;
using MyTools.Desktop.Themes;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Themes;

/// <summary>
/// Asserts that Light.xaml and Dark.xaml declare exactly the same set of
/// resource keys. A mismatch would leave some control unstyled after a theme
/// swap. Required by the design (§15 decision 5: hand-written + key-parity test).
/// </summary>
[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class ThemeResourceKeysTest
{
    private static ResourceDictionary LoadThemeDictionary(string name)
    {
        EnsureResourceAssemblyRegistered();
        // Use the component-relative pack URI so the resource resolves from the
        // Desktop assembly regardless of which assembly the test host runs in.
        var uri = new Uri(
            $"pack://application:,,,/MyTools.Desktop;component/Themes/{name}.xaml",
            UriKind.Absolute);
        return new ResourceDictionary { Source = uri };
    }

    private static void EnsureResourceAssemblyRegistered()
    {
        // ResourceDictionary.Source with a pack://application URI requires the
        // entry assembly to be known. In a test host there is no WPF Application
        // by default, so register the Desktop assembly explicitly.
        if (Application.ResourceAssembly == null)
        {
            Application.ResourceAssembly = typeof(App).Assembly;
        }
    }

    private static HashSet<string> Keys(ResourceDictionary dictionary)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in dictionary.Keys)
        {
            if (key is string s)
            {
                keys.Add(s);
            }
        }
        return keys;
    }

    [Test]
    public void LightAndDark_DeclareSameKeys()
    {
        var light = Keys(LoadThemeDictionary("Light"));
        var dark = Keys(LoadThemeDictionary("Dark"));

        Assert.Multiple(() =>
        {
            Assert.That(light, Is.Not.Empty, "Light.xaml declares no keys.");
            Assert.That(dark, Is.Not.Empty, "Dark.xaml declares no keys.");
            Assert.That(light.Except(dark), Is.Empty,
                "Keys only in Light.xaml (missing from Dark). Add them to Dark.xaml.");
            Assert.That(dark.Except(light), Is.Empty,
                "Keys only in Dark.xaml (missing from Light). Add them to Light.xaml.");
        });
    }

    [Test]
    public void EveryTokenKey_ExistsInBothThemes()
    {
        // Sanity check: a couple of must-have tokens from the design (§5.2).
        var mustHave = new[]
        {
            "WindowBackgroundBrush",
            "SurfaceBrush",
            "TextPrimaryBrush",
            "TextSecondaryBrush",
            "BorderBrush",
            "AccentBrush",
            "AccentForegroundBrush",
            "SelectionBrush",
        };

        var light = Keys(LoadThemeDictionary("Light"));
        var dark = Keys(LoadThemeDictionary("Dark"));

        foreach (var key in mustHave)
        {
            Assert.That(light, Does.Contain(key), $"Light.xaml missing token {key}.");
            Assert.That(dark, Does.Contain(key), $"Dark.xaml missing token {key}.");
        }
    }

    [Test]
    public void WebThemes_ExposeAccentForegroundToken()
    {
        Assert.Multiple(() =>
        {
            Assert.That(WebThemeTokens.Light, Contains.Key("--mt-accent-foreground"));
            Assert.That(WebThemeTokens.Dark, Contains.Key("--mt-accent-foreground"));
            Assert.That(WebThemeTokens.Light["--mt-accent-foreground"], Is.EqualTo("#FFFFFF"));
            Assert.That(WebThemeTokens.Dark["--mt-accent-foreground"], Is.EqualTo("#FFFFFF"));
        });
    }
}
