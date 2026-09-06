using System.Windows;
using System.Windows.Input;
using Microsoft.Extensions.Logging;
using MyTools.Common.DependencyInjection;
using MyTools.Desktop.Services;
using MyTools.Desktop.Views;
using MyTools.Plugins;

namespace MyTools.Desktop.Utils;

public static class WindowHelper
{
    public static void PrepareSearchWindow()
    {
        var searchWindow = ServiceLocator.GetRequiredService<SearchWindow>();
        Prepare(searchWindow);
    }

    public static void ShowSearchWindow(IPlugin? plugin = null, string? text = null)
    {
        var searchWindow = ServiceLocator.GetRequiredService<SearchWindow>();
        Prepare(searchWindow);
        RestorePlacement(searchWindow);
        if (searchWindow.CurrentPlugin != plugin)
        {
            searchWindow.SetPluginWindow(plugin);
        }

        WindowForeground.TryActivate(searchWindow);
        FocusSearchInput(searchWindow, text);
    }

    internal static void ActivateSearchWindowFromHotKey(IPlugin? plugin = null)
    {
        var searchWindow = ServiceLocator.GetRequiredService<SearchWindow>();
        Prepare(searchWindow);
        RestorePlacement(searchWindow);

        var logger = ServiceLocator.GetRequiredService<ILogger<SearchWindow>>();
        WindowForeground.TryActivateFromHotKey(searchWindow, logger);
        FocusSearchInput(searchWindow, text: null);
        if (searchWindow.CurrentPlugin != plugin)
        {
            _ = searchWindow.Dispatcher.BeginInvoke(() =>
            {
                searchWindow.SetPluginWindow(plugin);
                FocusSearchInput(searchWindow, text: null);
            });
        }
    }

    private static void Prepare(SearchWindow searchWindow)
    {
        var placement = ServiceLocator.GetRequiredService<WindowPlacementService>();
        searchWindow.PreparePersistentShell(placement);
    }

    private static void RestorePlacement(SearchWindow searchWindow)
    {
        var placement = ServiceLocator.GetRequiredService<WindowPlacementService>();
        placement.Restore(searchWindow, WindowPlacementService.SearchKey);
    }

    private static void FocusSearchInput(SearchWindow searchWindow, string? text)
    {
        if (text != null)
        {
            searchWindow.SearchTextBox.Text = text;
        }

        searchWindow.SearchTextBox.CaretIndex = searchWindow.SearchTextBox.Text.Length;
        searchWindow.SearchTextBox.Focus();
        Keyboard.Focus(searchWindow.SearchTextBox);
    }
}
