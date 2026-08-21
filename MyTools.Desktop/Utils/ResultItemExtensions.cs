using System.Windows;
using CommunityToolkit.Mvvm.Messaging;
using Microsoft.Extensions.Logging;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Desktop.Components;
using MyTools.Desktop.Utils;
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
        var logger = ServiceLocator.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ResultItemExtensions));
        logger.LogDebug(
            "Action result action={Action} success={Success} actionType={ActionType} message={Message}",
            action.Name,
            actionResult.Success,
            actionResult.ActionType,
            actionResult.Message);
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
            var localization = ServiceLocator.GetRequiredService<ILocalizationService>();
            var message = actionResult.LocalizedMessage?.Resolve(localization) ?? actionResult.Message;
            TopmostMessageBox.Show(
                localization.GetCaption(
                    "Action.ExecuteFailed.Message",
                    "Cannot execute {{action}}: {{message}}",
                    new { action = action.Name, message }),
                localization.GetCaption("Action.ExecuteFailed.Title", "Action execution failed"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
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