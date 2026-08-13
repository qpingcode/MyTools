using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using MyTools.Common;
using MyTools.Desktop.Components;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Desktop.ViewModels;

/// <summary>
/// 插件独立窗口专用的 ViewModel。
/// 与 <see cref="SearchViewModel"/> 不同，它只负责渲染单个插件的详情页，
/// 不包含关键词路由、防抖搜索、多 ViewModelType 切换等搜索主窗口逻辑。
/// </summary>
public partial class PluginViewModel : ObservableObject, ISearchViewModelCallback, IDisposable
{
    [ObservableProperty]
    private object currentViewModel;

    [ObservableProperty]
    private string? pluginName;

    [ObservableProperty]
    private string? pluginVersion;

    [ObservableProperty]
    private UpdateStatus status = UpdateStatus.Idle;

    [ObservableProperty]
    private string statusText = string.Empty;

    [ObservableProperty]
    private string detailedStatusText = string.Empty;

    [ObservableProperty]
    private ObservableCollection<IActionWithCommand> selectedResultActions = new();

    private readonly NodePluginDetailViewModel detailViewModel;
    private bool disposed;

    public string? CurrentNodePluginDetailId { get; private set; }

    public PluginViewModel(IServiceProvider serviceProvider)
    {
        // 与 SearchViewModel 一致：把自身作为 ISearchViewModelCallback 传入，
        // 避免 NodePluginDetailViewModel 与 callback 之间的循环依赖。
        detailViewModel = ActivatorUtilities.CreateInstance<NodePluginDetailViewModel>(serviceProvider, this);
        CurrentViewModel = detailViewModel;
    }

    /// <summary>
    /// 设置插件并应用详情上下文。可重复调用以刷新同一窗口。
    /// </summary>
    public void SetPlugin(NodePlugin plugin, NodePluginDetailContext? context)
    {
        PluginName = plugin.GetDisplayName();
        PluginVersion = string.IsNullOrWhiteSpace(context?.Version) ? null : context.Version;
        CurrentNodePluginDetailId = context?.PluginId;
        detailViewModel.SetContext(context);
    }

    public async Task FocusPrimaryInputAsync(System.Windows.DependencyObject? hostView)
    {
        if (hostView == null)
        {
            return;
        }

        var detailView = FindVisualChild<NodePluginDetailView>(hostView);
        if (detailView != null)
        {
            await detailView.FocusPrimaryInputAsync();
        }
    }

    private static T? FindVisualChild<T>(System.Windows.DependencyObject? parent) where T : System.Windows.DependencyObject
    {
        if (parent == null)
        {
            return null;
        }

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T match)
            {
                return match;
            }

            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
            {
                return descendant;
            }
        }

        return null;
    }

    #region ISearchViewModelCallback

    public void OnSearchTextChange(string text)
    {
        // 插件窗口没有顶部搜索框，无需处理
    }

    public void OnUpdateStatusBar(UpdateStatus status, string message)
    {
        Status = status;
        StatusText = GetStatusText(status);
        DetailedStatusText = message;
    }

    public void OnUpdateSelectedActions(List<IActionWithCommand>? actions)
    {
        SelectedResultActions.Clear();
        if (actions != null)
        {
            foreach (var action in actions)
            {
                SelectedResultActions.Add(action);
            }
        }
    }

    private static string GetStatusText(UpdateStatus status) => status switch
    {
        UpdateStatus.Pending => "Searching...",
        UpdateStatus.Success => "Success",
        UpdateStatus.Failed => "Failed...",
        _ => ""
    };

    #endregion

    [RelayCommand]
    private void ExecuteAction(string? command)
    {
        if (disposed || string.IsNullOrEmpty(command))
        {
            return;
        }

        ((ISwitchableViewModel)CurrentViewModel).ExecuteAction(command);
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        detailViewModel.Dispose();
    }
}
