using System.IO;
using System.Windows;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Model.Plugins;
using MyTools.Common.Plugins;
using MyTools.Common.WindowsMessageHandler;
using MyTools.Plugins.Param;
using Timer = System.Timers.Timer;

namespace MyTools.Plugins;

public class ClipBoardPlugin(ILogger<ClipBoardPlugin> logger) : PluginBase, IWindowMessageHandler
{
    public override string PluginId => "ClipBoard";
    private ClipBoardDbHelper? _dbHelper;
    private string _dbPath = Path.Combine(ConfigPath.DatabasePath, "clipboard_history.db");
    private Timer? _cleanupTimer;

    public override string Name => GetCaption("Plugin.ClipBoard.Name", "Clipboard History");
    public override string Description => GetCaption("Plugin.ClipBoard.Description", "Clipboard history management plugin");
    public override List<IActionWithCommand> Actions => [WellKnownActions.CopyAndPaste.WithDefaultCommand()];
    public override ViewModelType ViewModelType => ViewModelType.Detail;

    public override async Task InitializeAsync()
    {
        _dbHelper = new ClipBoardDbHelper(_dbPath);
        _cleanupTimer = new Timer(60 * 60 * 1000); // 1 hour
        _cleanupTimer.Elapsed += (s, e) =>
        {
            CleanOldHistory();
        };
        _cleanupTimer.AutoReset = true;
        _cleanupTimer.Start();

        CleanOldHistory();

        await Task.CompletedTask;
    }

    private void CleanOldHistory()
    {
        try
        {
            _dbHelper?.CleanupOldHistory();
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message, ex);
        }
    }

    // todo 增加右击某一项目时，弹出对话框，可以增加/修改/删除 category
    public override async Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        if (_dbHelper == null)
            return Result.CreateFailure("Clipboard DB not initialized", null);
        
        var items = _dbHelper.Search(query, includeContent: false);
        var resultItems = new List<ResultItem>();
        
        foreach (var item in items)
        {
            var lazyParam = new LazyClipboardParam(_dbHelper, item.Id);
            var title = ClipboardItemMeta.FormatListTitle(
                CollapseToSingleLine(item.Summary),
                item.PixelWidth,
                item.PixelHeight);
            resultItems.Add(new ResultItem(MdiIcon.ForClipboardKind(item.Kind), title, string.Empty, lazyParam, ResultItemPriorities.Medium)
            {
                ResultKey = item.Id.ToString(),
                CreatedAt = item.Timestamp
            });
        }
        return await Task.FromResult(Result.CreateSuccessResult(resultItems));
    }

    IEnumerable<WindowsMessageType> IWindowMessageHandler.Messages => [WindowsMessageType.ClipboardUpdate];

    void IWindowMessageHandler.Handle(int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        handled = true;
        if (_dbHelper == null)
        {
            return;
        }
        try
        {
            if (Clipboard.ContainsData(DataObjectSerializer.MyToolsNotSaveHisotryFormat))
            {
                return;
            }
            var title = CollapseToSingleLine(getTitleFromClipboard());
            var kind = ClipboardContentKindClassifier.FromClipboard();
            var (width, height, imageBytes) = ClipboardItemMeta.ReadClipboardImageMeta();
            var content = DataObjectSerializer.SerializeIDataObject();
            var hash = HashHelper.ComputeSha256Hash(content);
            _dbHelper.AddHistory(content, title, hash, kind, width, height, imageBytes);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }
    }

    private string getTitleFromClipboard()
    {
        if (Clipboard.ContainsText())
        {
            return Clipboard.GetText();
        }

        if (Clipboard.ContainsImage())
        {
            return "[Image]";
        }

        if (Clipboard.ContainsFileDropList())
        {
            return "[File]";
        }

        return "[Unknown]";
    }

    private static string CollapseToSingleLine(string text)
    {
        return string.Join(' ',
            text.Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
    }
    
    int IWindowMessageHandler.Priority => IWindowMessageHandler.LowPriority;
}