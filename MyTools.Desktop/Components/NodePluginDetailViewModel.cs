using System.Text.Json;
using CommunityToolkit.Mvvm.ComponentModel;
using MyTools.Common;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using Key = System.Windows.Input.Key;

namespace MyTools.Desktop.Components;

public partial class NodePluginDetailViewModel : ObservableObject, ISwitchableViewModel
{
    private readonly ISearchViewModelCallback callback;

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

    public void SetContext(NodePluginDetailContext? context)
    {
        CurrentContext = context;
        CurrentQuery = context?.Query ?? string.Empty;
        CurrentStateJson = context == null ? "{}" : context.InitialState.GetRawText();
        callback.OnUpdateSelectedActions(null);
        callback.OnUpdateStatusBar(UpdateStatus.Success, string.Empty);
    }

    public async Task<string?> HandleDetailEventAsync(string eventName, string payloadJson)
    {
        if (CurrentContext == null)
        {
            return null;
        }

        JsonElement? payload = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            using var document = JsonDocument.Parse(payloadJson);
            payload = document.RootElement.Clone();
        }

        var state = await CurrentContext.Plugin.HandleDetailEventAsync(CurrentContext.ItemId, eventName, payload, CurrentQuery);
        if (state == null)
        {
            return null;
        }

        var stateJson = state.Value.GetRawText();
        CurrentStateJson = stateJson;
        return stateJson;
    }

    public async Task<JsonElement?> HandleToolCallAsync(string action, string payloadJson)
    {
        if (CurrentContext == null)
        {
            return null;
        }

        JsonElement? payload = null;
        if (!string.IsNullOrWhiteSpace(payloadJson))
        {
            using var document = JsonDocument.Parse(payloadJson);
            payload = document.RootElement.Clone();
        }

        return await CurrentContext.Plugin.HandleDetailCallAsync(CurrentContext.ItemId, action, payload, CurrentQuery);
    }

    public ViewModelType GetViewModelType()
    {
        return ViewModelType.NodeDetail;
    }

    public void PerformSearch(IPlugin? plugin, string searchText)
    {
        CurrentQuery = searchText;
        callback.OnUpdateSelectedActions(null);
        callback.OnUpdateStatusBar(UpdateStatus.Success, string.Empty);
    }

    public bool HandleKeyDown(Key key)
    {
        return false;
    }

    public bool HandleKeyUp(Key key)
    {
        return false;
    }

    public void ExecuteAction(string? command)
    {
    }

    public void Dispose()
    {
    }
}