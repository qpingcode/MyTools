using System.IO;
using System.Text;
using System.Windows;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Config.Enums;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.Model.Plugins;
using MyTools.Common.Plugins;
using MyTools.Common.Utils;
using MyTools.Common.WindowsMessageHandler;
using MyTools.Plugins.Param;
using CommunityToolkit.Mvvm.Messaging;
using Timer = System.Timers.Timer;

namespace MyTools.Plugins;

public class ClipBoardPlugin(ILogger<ClipBoardPlugin> logger) : PluginBase, IWindowMessageHandler
{
    public const string DefaultHotKey = "Ctrl+Shift+V";
    public const string DefaultSequentialPasteHotKey = "Ctrl+Alt+V";
    public const int DefaultMaxHistoryDays = 10;
    public const int DefaultMaxHistoryCount = 500;

    public override string PluginId => "ClipBoard";
    protected override string SettingsCategoryName => GetCaption(
        "Plugin.ClipBoard.Settings.Category.Name", "Clipboard enhancement");
    protected override string SettingsCategoryDescription => GetCaption(
        "Plugin.ClipBoard.Settings.Category.Description", "Configure clipboard history and sequential paste");
    private ClipBoardDbHelper? _dbHelper;
    private string _dbPath = Path.Combine(ConfigPath.DatabasePath, "clipboard_history.db");
    private Timer? _cleanupTimer;
    private ConfigurationSetting? maxHistoryDaysSetting;
    private ConfigurationSetting? maxHistoryCountSetting;
    private readonly SemaphoreSlim sequentialPasteLock = new(1, 1);

    public override string Name => GetCaption("Plugin.ClipBoard.Name", "Clipboard History");
    public override string Description => GetCaption("Plugin.ClipBoard.Description", "Clipboard history management plugin");
    public override List<IActionWithHotkey> Actions =>
    [
        WellKnownActions.CopyAndPaste.WithDefaultHotkey(),
        new CopyPlainTextAndPasteAction().WithHotkey(Hotkey.Ctrl(HotkeyKey.Enter)),
        WellKnownActions.Copy.WithHotkey(Hotkey.Ctrl(HotkeyKey.E))
    ];
    public override ViewModelType ViewModelType => ViewModelType.Detail;

    protected override void AddPluginSettings(
        ConfigurationCategory pluginCategory,
        IConfigurationRegistry configurationRegistry)
    {
        configurationRegistry.AddSetting(
            pluginCategory,
            "HotKey",
            GetCaption("Plugin.ClipBoard.Settings.HotKey.Title", "Shortcut"),
            GetCaption("Plugin.ClipBoard.Settings.HotKey.Description", "Keyboard shortcut that opens clipboard history"),
            DefaultHotKey,
            valueType: SettingValueTypes.HotKey);
        configurationRegistry.AddSetting(
            pluginCategory,
            "SequentialPasteHotKey",
            GetCaption("Plugin.ClipBoard.Settings.SequentialPasteHotKey.Title", "Sequential paste shortcut"),
            GetCaption("Plugin.ClipBoard.Settings.SequentialPasteHotKey.Description", "Paste and remove the newest clipboard history entry without opening MyTools"),
            DefaultSequentialPasteHotKey,
            valueType: SettingValueTypes.HotKey);
        maxHistoryDaysSetting = configurationRegistry.AddSetting(
            pluginCategory,
            "MaxHistoryDays",
            GetCaption("Plugin.ClipBoard.Settings.MaxHistoryDays.Title", "Maximum retention days"),
            GetCaption("Plugin.ClipBoard.Settings.MaxHistoryDays.Description", "Delete clipboard entries older than this many days"),
            DefaultMaxHistoryDays);
        maxHistoryCountSetting = configurationRegistry.AddSetting(
            pluginCategory,
            "MaxHistoryCount",
            GetCaption("Plugin.ClipBoard.Settings.MaxHistoryCount.Title", "Maximum history entries"),
            GetCaption("Plugin.ClipBoard.Settings.MaxHistoryCount.Description", "Keep at most this many clipboard entries"),
            DefaultMaxHistoryCount);
    }

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

    public void ApplyRetentionSettings()
    {
        try
        {
            _dbHelper?.CleanupOldHistory(GetPositiveSetting(maxHistoryDaysSetting, DefaultMaxHistoryDays),
                GetPositiveSetting(maxHistoryCountSetting, DefaultMaxHistoryCount));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to clean old clipboard history.");
        }
    }

    private void CleanOldHistory() => ApplyRetentionSettings();

    private static int GetPositiveSetting(ConfigurationSetting? setting, int defaultValue) =>
        setting?.CurrentValue is int value && value > 0 ? value : defaultValue;

    public Task<ActionResult> AddTextHistoryAsync(IEnumerable<string> values)
    {
        if (_dbHelper == null)
        {
            return Task.FromResult(ActionResult.CreateFailure("Clipboard DB not initialized"));
        }

        var texts = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToList();
        if (texts.Count == 0)
        {
            return Task.FromResult(ActionResult.CreateFailure("No text was provided for clipboard history."));
        }

        // Sequential paste consumes newest-first. Insert in reverse so values are pasted in the
        // same order in which the generating plugin displayed them.
        foreach (var text in texts.AsEnumerable().Reverse())
        {
            var dataObject = new DataObject();
            dataObject.SetText(text, TextDataFormat.UnicodeText);
            var content = DataObjectSerializer.SerializeIDataObject(dataObject);
            _dbHelper.AddHistory(
                content,
                CollapseToSingleLine(text),
                HashHelper.ComputeSha256Hash(content),
                ClipboardContentKind.Text,
                byteSize: Encoding.UTF8.GetByteCount(text));
        }

        WeakReferenceMessenger.Default.Send(new ClipboardHistoryChangedMessage());
        return Task.FromResult(ActionResult.CreateSuccess($"Added {texts.Count} items to clipboard history."));
    }


    public override async Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        if (_dbHelper == null)
            return Result.CreateFailure("Clipboard DB not initialized", null);
        
        var items = _dbHelper.Search(
            query,
            max: GetPositiveSetting(maxHistoryCountSetting, DefaultMaxHistoryCount),
            includeContent: false);
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
                CreatedAt = item.Timestamp,
                IgnoreSelectionHistoryBoost = true
            });
        }
        var showEmptyHistory = resultItems.Count == 0 && string.IsNullOrWhiteSpace(query);
        return await Task.FromResult(Result.CreateSuccessResult(
            resultItems,
            showEmptyHistory
                ? GetCaption("Plugin.ClipBoard.Empty.Title", "Clipboard history is empty")
                : null,
            showEmptyHistory
                ? GetCaption(
                    "Plugin.ClipBoard.Empty.Description",
                    "Copy some text, an image, or a file and it will appear here.")
                : null));
    }

    public async Task<bool> PasteLatestAndRemoveAsync(IKeyboardHelper keyboardHelper)
    {
        if (!await sequentialPasteLock.WaitAsync(0))
        {
            return false;
        }

        try
        {
            if (_dbHelper == null)
            {
                logger.LogWarning("Sequential clipboard paste skipped because the clipboard database is not initialized.");
                return false;
            }

            // WM_HOTKEY is delivered while Ctrl/Alt may still be physically held. Wait for their
            // release so the synthetic Ctrl+V does not become Ctrl+Alt+V in the target process.
            for (var attempt = 0; attempt < 20 && System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None; attempt++)
            {
                await Task.Delay(25);
            }
            if (System.Windows.Input.Keyboard.Modifiers != System.Windows.Input.ModifierKeys.None)
            {
                logger.LogWarning("Sequential clipboard paste skipped because the shortcut modifiers were not released.");
                return false;
            }

            var item = _dbHelper.GetLatestHistory();
            if (item == null)
            {
                logger.LogInformation("Sequential clipboard paste skipped because history is empty.");
                return false;
            }

            var (dataObject, _, _) = DataObjectSerializer.DeserializeToIDataObject(item.Content);
            ClipboardAccess.Execute(() => Clipboard.SetDataObject(dataObject, true));
            keyboardHelper.Paste();

            if (!_dbHelper.DeleteHistory(item.Id))
            {
                logger.LogWarning("Sequential clipboard paste completed but history item {ItemId} was already removed.", item.Id);
            }
            WeakReferenceMessenger.Default.Send(new ClipboardHistoryChangedMessage());
            logger.LogDebug("Sequentially pasted and removed clipboard history item {ItemId}.", item.Id);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Sequential clipboard paste failed; the history entry was not removed.");
            return false;
        }
        finally
        {
            sequentialPasteLock.Release();
        }
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
            ClipboardAccess.Execute(CaptureClipboardHistory);
        }
        catch (System.Runtime.InteropServices.COMException ex)
            when (ex.HResult == unchecked((int)0x800401D0))
        {
            logger.LogWarning(ex, "Clipboard history capture skipped because the clipboard remained busy after retries.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }
    }

    private void CaptureClipboardHistory()
    {
        if (_dbHelper == null || Clipboard.ContainsData(DataObjectSerializer.MyToolsNotSaveHisotryFormat))
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
