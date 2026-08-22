using MyTools.Common;

namespace MyTools.Desktop.Components;

/// <summary>
/// 子ViewModel通过此接口回调父ViewModel
/// </summary>
public interface ISearchViewModelCallback
{
    void OnSearchTextChange(string text);
    void OnUpdateStatusBar(UpdateStatus status, string message);
    void OnUpdateSelectedActions(List<IActionWithHotkey>? actions);
    void OnRequestClose();
}

