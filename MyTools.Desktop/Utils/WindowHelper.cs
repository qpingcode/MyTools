using System.Windows;
using MyTools.Common.DependencyInjection;
using MyTools.Desktop.Views;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.Utils;

public static class WindowHelper
{
    public static void ShowSearchWindow(IPlugin? plugin = null, string? text = null)
    {
        string previousSearchText = string.Empty;
        
        if (FindExistingSearchWindow(out var existing))
        {
            if (existing!.CurrentPlugin == plugin)
            {
                existing.SetPluginWindow(plugin);
                existing.Activate();
                existing.Refresh();
                if (text != null)
                {
                    existing.SearchTextBox.Text = text;
                }
                existing.SearchTextBox.Focus();
                return;
            }

            previousSearchText = existing.SearchTextBox.Text;
            existing.Close();
        }

        var searchWindow = ServiceLocator.GetRequiredService<SearchWindow>();
        if (plugin != null)
        {
            searchWindow.SetPluginWindow(plugin);
        }

        var searchText = text ?? previousSearchText;
        searchWindow.Show();
        searchWindow.Activate();
        searchWindow.SearchTextBox.Text = searchText;
        searchWindow.SearchTextBox.Focus();
        searchWindow.SearchTextBox.CaretIndex = searchText.Length;
    }

    public static bool TryFocusNodePluginDetail(NodePlugin nodePlugin)
    {
        if (!FindExistingSearchWindow(out var existing) || existing?.CurrentNodePluginDetailId != nodePlugin.Id)
        {
            return false;
        }

        if (existing.WindowState == WindowState.Minimized)
        {
            existing.WindowState = WindowState.Normal;
        }

        existing.Activate();
        existing.Focus();
        _ = existing.FocusNodePluginPrimaryInputAsync();
        return true;
    }

    public static void FocusCurrentNodePluginDetailInput()
    {
        if (FindExistingSearchWindow(out var existing))
        {
            _ = existing!.FocusNodePluginPrimaryInputAsync();
        }
    }

    static bool FindExistingSearchWindow(out SearchWindow? existingWindow)
    {
        existingWindow = Application.Current.Windows.OfType<SearchWindow>().FirstOrDefault();
        return existingWindow != null;
    }
}