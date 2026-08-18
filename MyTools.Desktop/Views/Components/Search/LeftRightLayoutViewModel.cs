using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Plugins;
using MyTools.Desktop.Utils;

namespace MyTools.Desktop.Components;

public partial class LeftRightLayoutViewModel : ObservableObject, ISwitchableViewModel
{
    [ObservableProperty]
    private bool isCtrlPressed;
    
    [ObservableProperty]
    private string leftTextArea = string.Empty;
    
    private bool isUpdatingFromSearch;
    
    [ObservableProperty]
    private bool isWordWrapEnabled;
    
    [ObservableProperty]
    private ResultItem? selectedResult;
    
    private CancellationTokenSource? searchCancellation;
    private readonly ISearcher searcher;
    private readonly IActionRegistry actionRegistry;
    private readonly ILogger<LeftRightLayoutViewModel> logger;
    private readonly ISearchViewModelCallback callback;

    public LeftRightLayoutViewModel(ISearchViewModelCallback callback, ISearcher searcher, IActionRegistry actionRegistry, ILogger<LeftRightLayoutViewModel> logger)
    {
        this.callback = callback;
        this.searcher = searcher;
        this.actionRegistry = actionRegistry;
        this.logger = logger;
    }
    
    partial void OnSelectedResultChanged(ResultItem? value)
    {
        if (value != null)
        {
            value.LoadPreviewContentIfNeeded();
        }
        UpdateSelectedResultActions();
    }
    
    #region ISwitchableViewModel

    public async void PerformSearch(IPlugin? plugin, string searchText)
    {
        try
        {
            searchCancellation?.Cancel();
            searchCancellation = new CancellationTokenSource();
            SelectedResult = null;
            
            UpdateStatusText(UpdateStatus.Pending, GetResourceString("Searching"));
            
            // 设置左边TextArea的内容为搜索文本
            isUpdatingFromSearch = true;
            LeftTextArea = searchText;
            isUpdatingFromSearch = false;
            
            Result result;
            try
            {
                result = await searcher.SearchAsync(plugin, searchText, searchCancellation.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            if (result.Exception is OperationCanceledException)
            {
                return;
            }
            
            SelectedResult = result.Items.FirstOrDefault();
            UpdateStatusText(result.Success ? UpdateStatus.Success : UpdateStatus.Failed, result.ErrorMessage ?? "");
        }
        catch (Exception ex)
        {
            UpdateStatusText(UpdateStatus.Failed, ex.Message);
        }
    }
    
    private static string GetResourceString(string key)
    {
        var resource = Application.Current.TryFindResource(key);
        return resource as string ?? key;
    }
    
    private void UpdateStatusText(UpdateStatus status, string detailedStatusText = "")
    {
        callback?.OnUpdateStatusBar(status, detailedStatusText);
    }
    
    private void UpdateSelectedResultActions()
    {
        if (SelectedResult == null)
        {
            callback?.OnUpdateSelectedActions(null);
            return;
        }
            
        var actionsWithCommand = SelectedResult.AllowedActions.ToList();
        callback?.OnUpdateSelectedActions(actionsWithCommand);
    }
    
    partial void OnLeftTextAreaChanged(string value)
    {
        // 避免循环更新：当从搜索消息更新时不触发搜索
        if (isUpdatingFromSearch) return;
        callback?.OnSearchTextChange(value);
    }

    public bool HandleKeyDown(Key key)
    {
        var isHandled = false;
        
        switch (key)
        {
            case Key.System:
            case Key.LeftAlt:
                isHandled = true;
                break;
            case Key.LeftCtrl:
            case Key.RightCtrl:
                IsCtrlPressed = true;
                break;
        }
        
        if (!KeyUtils.IsSystemCommlyUsedKey(key) && KeyUtils.IsLetterOrEnter(key))
        {
            if (IsCtrlPressed)
            {
                var command = key == Key.Enter ? "Ctrl+Enter" : $"Ctrl+{key}";
                if (CanExecuteCommand(command))
                {
                    isHandled = true;
                    ExecuteAction(command);
                }
            }
            else if (key == Key.Enter)
            {
                if (CanExecuteCommand(Commands.DefaultCommand))
                {
                    isHandled = true;
                    ExecuteAction(Commands.DefaultCommand);
                }
            }
        }
        
        return isHandled;
    }
    
    public async void ExecuteAction(string? command)
    {
        if (SelectedResult != null)
        {
           await SelectedResult.ExecuteAction(command);
        }
    }

    private bool CanExecuteCommand(string command)
    {
        return SelectedResult?.AllowedActions.Any(a => a.Command == command) == true;
    }

    public bool HandleKeyUp(Key key)
    {
        if (key == Key.LeftCtrl || key == Key.RightCtrl)
        {
            IsCtrlPressed = false;
        }
        return false;
    }

    public void Dispose()
    {
        searchCancellation?.Dispose();
        searchCancellation = null;
    }

    public ViewModelType GetViewModelType()
    {
        return ViewModelType.LeftRight;
    }

    #endregion
}
