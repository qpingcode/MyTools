using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using Lucene.Net.Util;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Plugins;
using MyTools.Desktop.Utils;

namespace MyTools.Desktop.Components;

public partial class BasicListViewModel : ObservableObject, ISwitchableViewModel
{
    [ObservableProperty]
    private bool isCtrlPressed = false;
    
    partial void OnIsCtrlPressedChanged(bool value)
    {
        if (value)
        {
            UpdateNumberLabels();
        }
        else
        {
            numberToResultMap.Clear();
        }
    }
    
    [ObservableProperty]
    private ResultItem? selectedResult;
        
    [ObservableProperty]
    private ObservableCollection<ResultItem> searchResults = new();
        
    private CancellationTokenSource? searchCancellation;
    private readonly Dictionary<int, ResultItem> numberToResultMap = new();
    private readonly IActionRegistry actionRegistry;
    private readonly ISearcher searcher;
    private readonly ILogger<BasicListViewModel> logger;
    private readonly ISearchViewModelCallback callback;

    // 使用 IVisibleItemProvider 来解耦ViewModel和View
    public IVisibleItemProvider? VisibleItemProvider { get; set; }                 

    public BasicListViewModel(ISearchViewModelCallback callback, IActionRegistry actionRegistry, ISearcher searcher, ILogger<BasicListViewModel> logger)
    {
        this.callback = callback;
        this.actionRegistry = actionRegistry;
        this.searcher = searcher;
        this.logger = logger;
    }


    private void UpdateSelectedResultActions()
    {
        if (SelectedResult == null)
        {
            callback?.OnUpdateSelectedActions(null);
            return;
        }

        var actionsWithHotkey = SelectedResult.AllowedActions.ToList();
        callback?.OnUpdateSelectedActions(actionsWithHotkey);
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
            
            SearchResults.Clear();
            SearchResults.AddRange(result.Items);
            SelectedResult = SearchResults.FirstOrDefault();
            var statusText = result.ErrorMessage ?? string.Empty;
            UpdateStatusText(result.Success ? UpdateStatus.Success : UpdateStatus.Failed, statusText);
        }
        catch (Exception ex)
        {
            UpdateStatusText(UpdateStatus.Failed, ex.Message);
        }
    }

    private void UpdateStatusText(UpdateStatus status, string detailedStatusText)
    {
        callback?.OnUpdateStatusBar(status, detailedStatusText);
    }

    private static string GetResourceString(string key)
    {
        var resource = Application.Current.TryFindResource(key);
        return resource as string ?? key;
    }
   
    public bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        var isHandled = false;
        switch (key)
        {
            case Key.LeftAlt:
            case Key.System:
                // handle alt as double alt will open system menu, which is not what we want
                isHandled = true;
                break;
            case Key.Up:
                SelectUp();
                isHandled = true;
                break;
            case Key.Down:
                SelectDown();
                isHandled = true;
                break;
            case Key.LeftCtrl:
            case Key.RightCtrl:
                IsCtrlPressed = true;
                break;
        }

        if (KeyUtils.IsNumber(key) && modifiers == ModifierKeys.Control)
        {
            isHandled = true;
            var number = key == Key.D0 ? 10 : key - Key.D0;
            if (!numberToResultMap.TryGetValue(number, out var result)) return isHandled;
            SelectedResult = result;
            ExecuteAction(null);
        }
        
        var isReservedEditingShortcut = modifiers == ModifierKeys.Control && KeyUtils.IsSystemCommlyUsedKey(key);
        var mayInvokeAction = key == Key.Enter || modifiers != ModifierKeys.None;
        if (!isReservedEditingShortcut && mayInvokeAction)
        {
            var hotkey = ResultActionBarHotkeys.ToHotkey(key, modifiers);
            if (hotkey != null && CanExecuteHotkey(hotkey.Value))
            {
                isHandled = true;
                ExecuteAction(SelectedResult?.AllowedActions.First(action => action.Hotkey == hotkey.Value));
            }
        }
    
        return isHandled;
    }
    
    private void SelectDown()
    {
        if (SearchResults.Count <= 0) return;
        if (SelectedResult == null)
        {
            SelectedResult = SearchResults[0];
        }
        else
        {
            var currentIndex = SearchResults.IndexOf(SelectedResult);
            if (currentIndex < SearchResults.Count - 1)
            {
                SelectedResult = SearchResults[currentIndex + 1];
            }
        }
    }

    private void SelectUp()
    {
        if (SearchResults.Count <= 0 || SelectedResult == null) return;
        var currentIndex = SearchResults.IndexOf(SelectedResult);
        if (currentIndex > 0)
        {
            SelectedResult = SearchResults[currentIndex - 1];
        }
    }
    
    public void UpdateNumberLabels()
    {
        numberToResultMap.Clear();
        var visibleItems = VisibleItemProvider?.GetVisibleItems();
        if (visibleItems == null)
        {
            return;
        }
        
        for (var i = 0; i < visibleItems.Count; i++)
        {
            var number = i + 1;
            var result = visibleItems[i];
            result.NumberLabel = $"{number}";
            numberToResultMap[number] = result;
        }
    }
    
    public async void ExecuteAction(IActionWithHotkey? action)
    {
        if (SelectedResult != null)
        {
            var selectedAction = action ?? SelectedResult.AllowedActions.FirstOrDefault();
            if (selectedAction != null)
            {
                await SelectedResult.ExecuteAction(selectedAction);
            }
        }
    }

    private bool CanExecuteHotkey(Hotkey hotkey)
    {
        return SelectedResult?.AllowedActions.Any(a => a.Hotkey == hotkey) == true;
    }
    
    partial void OnSelectedResultChanged(ResultItem? value)
    {
        UpdateSelectedResultActions();
        if (value != null)
        {
            value.LoadPreviewContentIfNeeded();
        }
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
        return ViewModelType.Basic;
    }

    #endregion
}
