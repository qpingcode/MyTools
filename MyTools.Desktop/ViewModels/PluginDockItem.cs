using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTools.Common;
using MyTools.Common.Plugins;
using MyTools.Desktop.Views;

namespace MyTools.Desktop.ViewModels;

public sealed partial class PluginDockItem : ObservableObject
{
    public PluginDockItem(
        PluginId pluginId,
        PluginWindow window,
        string displayName,
        Icon icon,
        Action<PluginId> toggle)
    {
        PluginId = pluginId;
        Window = window;
        DisplayName = displayName;
        Icon = icon;
        ToggleCommand = new RelayCommand(() => toggle(pluginId));
        RefreshOpenState();
    }

    public PluginId PluginId { get; }

    public PluginWindow Window { get; }

    public IRelayCommand ToggleCommand { get; }

    [ObservableProperty]
    private string displayName;

    [ObservableProperty]
    private Icon icon;

    [ObservableProperty]
    private string actionCaption = string.Empty;

    [ObservableProperty]
    private bool isOpen;

    public void RefreshOpenState()
    {
        IsOpen = Window.IsWindowOpen;
    }
}
