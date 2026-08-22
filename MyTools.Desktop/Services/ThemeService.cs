using MyTools.Common.Config.Interfaces;
using MyTools.Common.Theming;

namespace MyTools.Desktop.Services;

/// <summary>
/// Host implementation of <see cref="IThemeService"/>. Single source of truth for
/// the current theme. Reads the persisted value on construction; <see cref="SetTheme"/>
/// applies the new value immediately (hot-swap, no restart) and raises
/// <see cref="IThemeService.ThemeChanged"/>.
/// </summary>
public class ThemeService : IThemeService
{
    public const string ThemeSettingPath = GeneralSettings.ThemePath;
    private readonly IConfigurationStorage storage;

    public ThemeService(IConfigurationStorage storage)
    {
        this.storage = storage;
        CurrentTheme = ThemeKindExtensions.Parse(
            storage.Retrieve(ThemeSettingPath) ?? GeneralSettings.DefaultTheme);
    }

    public ThemeKind CurrentTheme { get; private set; }

    public event EventHandler<ThemeChangedEventArgs>? ThemeChanged;

    public void SetTheme(ThemeKind theme)
    {
        if (theme == CurrentTheme)
        {
            return;
        }

        var previous = CurrentTheme;
        CurrentTheme = theme;
        storage.Store(ThemeSettingPath, theme.ToWireString());
        ThemeChanged?.Invoke(this, new ThemeChangedEventArgs(previous, theme));
    }

    /// <summary>
    /// Reads the configured theme from the registry and applies it at runtime.
    /// Used after settings are loaded so a value changed via the config UI takes
    /// effect without restart.
    /// </summary>
    public void ApplyFromSettings(IConfigurationRegistry registry)
    {
        var stored = registry.FindSetting(ThemeSettingPath)?.GetValue<string>();
        SetTheme(ThemeKindExtensions.Parse(stored));
    }
}
