using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using MyTools.Common;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using Key = System.Windows.Input.Key;
using ModifierKeys = System.Windows.Input.ModifierKeys;

namespace MyTools.Desktop.Components;

public partial class NodePluginDetailViewModel : ObservableObject, ISwitchableViewModel
{
    private readonly ISearchViewModelCallback callback;
    private List<IActionWithHotkey> detailActions = [];

    [ObservableProperty]
    private NodePluginDetailContext? currentContext;

    [ObservableProperty]
    private string currentQuery = string.Empty;

    [ObservableProperty]
    private string currentStateJson = "{}";

    public NodePluginDetailViewModel(ISearchViewModelCallback callback)
    {
        this.callback = callback;
    }

    /// <summary>
    /// Node action 显式返回 web outcome 时，由 View 转成 host.event.detailAction 发给网页。
    /// </summary>
    public event Action<JsonElement>? WebActionRequested;

    public void SetContext(NodePluginDetailContext? context)
    {
        if (CurrentContext?.Plugin != null)
        {
            CurrentContext.Plugin.ActionsChanged -= OnActionsChanged;
        }
        CurrentContext = context;
        if (context?.Plugin != null)
        {
            context.Plugin.ActionsChanged += OnActionsChanged;
        }
        CurrentQuery = context?.Query ?? string.Empty;
        CurrentStateJson = context == null ? "{}" : context.InitialState.GetRawText();
        detailActions = BuildDetailActions(context);
        callback.OnUpdateSelectedActions(detailActions.Count == 0 ? null : detailActions);
        callback.OnUpdateStatusBar(UpdateStatus.Success, string.Empty);
    }

    public ViewModelType GetViewModelType()
    {
        return ViewModelType.NodeDetail;
    }

    public void PerformSearch(IPlugin? plugin, string searchText)
    {
        CurrentQuery = searchText;
        callback.OnUpdateSelectedActions(detailActions.Count == 0 ? null : detailActions);
        callback.OnUpdateStatusBar(UpdateStatus.Success, string.Empty);
    }

    public bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        var hotkey = ResultActionBarHotkeys.ToHotkey(key, modifiers);
        if (hotkey == null || detailActions.All(action => action.Hotkey != hotkey.Value))
        {
            return false;
        }

        ExecuteAction(detailActions.First(action => action.Hotkey == hotkey.Value));
        return true;
    }

    public bool HandleKeyUp(Key key)
    {
        return false;
    }

    public async void ExecuteAction(IActionWithHotkey? action)
    {
        if (action == null)
        {
            return;
        }

        var context = CurrentContext;
        var result = await action.ExecuteAsync(new NodePluginActionArgs(context?.ItemId ?? string.Empty, CurrentQuery));
        callback.OnUpdateStatusBar(
            result.Success ? UpdateStatus.Success : UpdateStatus.Failed,
            result.Message);
        if (result.ActionType == ActionTypeEnum.Close)
        {
            callback.OnRequestClose();
        }
    }

    /// <summary>
    /// 详情页默认拥有本 entry 在 initialize 注册的全部 action。
    /// </summary>
    private List<IActionWithHotkey> BuildDetailActions(NodePluginDetailContext? context)
    {
        return context?.Plugin.BuildActions(forwardToWeb: ForwardToWeb) ?? [];
    }

    private void ForwardToWeb(JsonElement payload)
    {
        WebActionRequested?.Invoke(payload);
    }

    private void OnActionsChanged(object? sender, EventArgs e)
    {
        void Refresh()
        {
            detailActions = BuildDetailActions(CurrentContext);
            callback.OnUpdateSelectedActions(detailActions.Count == 0 ? null : detailActions);
        }

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess()) Refresh();
        else dispatcher.Invoke(Refresh);
    }

    public void Dispose()
    {
        if (CurrentContext?.Plugin != null)
        {
            CurrentContext.Plugin.ActionsChanged -= OnActionsChanged;
        }
        WebActionRequested = null;
    }
}
