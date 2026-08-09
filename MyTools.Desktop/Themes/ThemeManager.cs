using System.Windows;
using MyTools.Common.Theming;

namespace MyTools.Desktop.Themes;

/// <summary>
/// Pure static helper that swaps the WPF theme <see cref="ResourceDictionary"/>.
/// It must NOT hold an <see cref="IThemeService"/> reference: the service raises
/// <see cref="IThemeService.ThemeChanged"/> and calls <see cref="ApplyTheme"/>,
/// keeping the DI graph (ValidateOnBuild) clean.
///
/// Relies on <c>DynamicResource</c> references in XAML: changing the dictionary
/// <see cref="ResourceDictionary.Source"/> automatically refreshes every bound
/// control without walking the visual tree.
/// </summary>
public static class ThemeManager
{
    private const string ThemePathFragment = "Themes/";
    private static readonly Uri LightSource = new("pack://application:,,,/Themes/Light.xaml");
    private static readonly Uri DarkSource = new("pack://application:,,,/Themes/Dark.xaml");

    /// <summary>
    /// Swaps the WPF theme <see cref="ResourceDictionary"/> for <paramref name="theme"/>.
    /// Implemented as remove-old-then-add-new rather than mutating a dictionary's
    /// <see cref="ResourceDictionary.Source"/>: WPF reliably propagates
    /// <c>DynamicResource</c> refreshes across already-realized controls only when the
    /// merged-dictionary collection itself changes. Must NOT hold an
    /// <see cref="IThemeService"/> reference (see design §7.7).
    /// </summary>
    public static void ApplyTheme(ThemeKind theme)
    {
        var desired = new ResourceDictionary { Source = theme == ThemeKind.Light ? LightSource : DarkSource };
        var merged = Application.Current?.Resources.MergedDictionaries;
        if (merged == null)
        {
            return;
        }

        // Remove every theme dictionary (Light/Dark), keep Shared.xaml and any others.
        for (var i = merged.Count - 1; i >= 0; i--)
        {
            var source = merged[i].Source?.OriginalString;
            if (source == null
                || !source.Contains(ThemePathFragment, StringComparison.OrdinalIgnoreCase)
                || source.EndsWith("Shared.xaml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            merged.RemoveAt(i);
        }

        merged.Add(desired);
    }

}
