using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using MyTools.Common.DependencyInjection;
using MyTools.Desktop.Components;
using MyTools.Plugins;

namespace MyTools.Common;

public static class ResultItemExtensions
{
    public static async Task ExecuteAction(this ResultItem item, string? command)
    {
        var action = command == null ? 
            item.AllowedActions.FirstOrDefault() : 
            item.AllowedActions.FirstOrDefault(a => a.Command == command);
        
        if (action == null)
        {
            throw new NotSupportedException("Action not found with command: " + command);
        }

        await ExecuteAction(item, action).ConfigureAwait(false);
    }

    public static async Task ExecuteAction(this ResultItem item, IActionWithCommand action)
    {
        var actionResult = await action.ExecuteAsync(item.Args).ConfigureAwait(false);
        if (actionResult.Success)
        {
            var history = ServiceLocator.GetRequiredService<SearchHistoryDbHelper>();
            history.RecordSelection(item.SearchQuery, item.SourcePluginId, item.ResultKey);

            var searcher = ServiceLocator.GetRequiredService<Searcher>();
            searcher.InvalidateHomePageCache();
        }
        
        if (actionResult.ActionType == ActionTypeEnum.Close)
        {
            WeakReferenceMessenger.Default.Send(new SearchWindowCloseMessage());
        }

        if (!actionResult.Success)
        {
            MessageBox.Show($"Cannot Execute {action.Name}: {actionResult.Message} ", "Action Execute Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    public static void LoadPreviewContentIfNeeded(this ResultItem item)
    {
        if (item.Content.Length > 0)
        {
            return;
        }

        if (item.Args is IPreviewContentProvider previewProvider)
        {
            var (previewContentType, previewContent) = previewProvider.GetPreviewContent();
            item.Content = previewContent;
            item.PreviewContentType = previewContentType;
        }
    }
}