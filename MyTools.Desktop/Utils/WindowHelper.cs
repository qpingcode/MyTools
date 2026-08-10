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

    static bool FindExistingSearchWindow(out SearchWindow? existingWindow)
    {
        existingWindow = Application.Current.Windows.OfType<SearchWindow>().FirstOrDefault();
        return existingWindow != null;
    }
}