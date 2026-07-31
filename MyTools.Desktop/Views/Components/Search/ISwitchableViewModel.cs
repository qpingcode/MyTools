using System.Windows.Input;
using MyTools.Common;
using MyTools.Plugins;

namespace MyTools.Desktop.Components;

/// <summary>
/// 父ViewModel通过此接口调用子ViewModel
/// </summary>
public interface ISwitchableViewModel : IDisposable
{
    ViewModelType GetViewModelType();
    void PerformSearch(IPlugin? plugin, string searchText);
    bool HandleKeyDown(Key key);
    bool HandleKeyUp(Key key);
    void ExecuteAction(string? command);
}