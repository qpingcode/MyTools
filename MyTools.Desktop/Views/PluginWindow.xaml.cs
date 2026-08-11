using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using MyTools.Desktop.Components;
using MyTools.Desktop.ViewModels;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MyTools.Desktop.Views;

/// <summary>
/// 插件独立窗口。与 <see cref="SearchWindow"/> 不同：
/// - 无 singletonLock，允许同一应用同时存在多个 PluginWindow（每种插件一个）。
/// - 无顶部搜索框，内容区直接渲染插件的详情页。
/// </summary>
public partial class PluginWindow
{
    private readonly PluginViewModel viewModel;

    public PluginWindow(PluginViewModel viewModel)
    {
        InitializeComponent();
        StateChanged += Window_OnStateChanged;
        ApplyWindowChromeState();

        this.viewModel = viewModel;
        DataContext = viewModel;

        PreviewKeyDown += Window_PreviewKeyDown;
        Closed += Window_OnClosed;

        Loaded += PluginWindow_Loaded;
    }

    public string? PluginId { get; private set; }

    /// <summary>
    /// 设置插件并应用详情上下文。窗口首次创建与重复刷新（复用）时都会调用。
    /// </summary>
    public void SetPlugin(NodePlugin plugin, NodePluginDetailContext? context)
    {
        PluginId = plugin.PluginId;
        viewModel.SetPlugin(plugin, context);
    }

    /// <summary>
    /// 激活窗口并把焦点放入插件详情页的主输入框。
    /// </summary>
    public async Task ActivatePluginAsync()
    {
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();
        Focus();

        // 与 SearchWindow.FocusNodePluginPrimaryInputAsync 一致：等待 dispatcher 空闲，
        // 让 WebView2 初始化 / 导航有机会完成后再聚焦。
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await viewModel.FocusPrimaryInputAsync(PluginContentView);
    }

    private async void PluginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await viewModel.FocusPrimaryInputAsync(PluginContentView);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W) || e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (Keyboard.Modifiers != ModifierKeys.None)
        {
            return;
        }

        // Tab / Enter 转发到插件详情页（与 SearchWindow 行为一致）
        if (e.Key != Key.Tab && e.Key != Key.Enter)
        {
            return;
        }

        var detailView = FindVisualChild<NodePluginDetailView>(PluginContentView);
        if (detailView == null)
        {
            return;
        }

        e.Handled = true;
        if (e.Key == Key.Tab)
        {
            _ = detailView.FocusPrimaryInputAsync();
            return;
        }

        detailView.SendHostKey("Enter");
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        viewModel.Dispose();
    }

    private void Window_OnStateChanged(object? sender, EventArgs e)
    {
        ApplyWindowChromeState();
    }

    private void TitleBarDragRegion_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
            e.Handled = true;
            return;
        }

        try
        {
            DragMove();
            e.Handled = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e)
    {
        SystemCommands.MinimizeWindow(this);
    }

    private void MaximizeRestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        SystemCommands.CloseWindow(this);
    }

    private void ToggleMaximizeRestore()
    {
        if (ResizeMode is not (ResizeMode.CanResize or ResizeMode.CanResizeWithGrip))
        {
            return;
        }

        if (WindowState == WindowState.Maximized)
        {
            SystemCommands.RestoreWindow(this);
            return;
        }

        SystemCommands.MaximizeWindow(this);
    }

    private void ApplyWindowChromeState()
    {
        var state = PluginWindowChromeState.From(WindowState);
        WindowFrame.Margin = state.FrameMargin;
        WindowFrame.CornerRadius = state.CornerRadius;
        WindowShadow.Opacity = state.ShowShadow ? 0.5 : 0;
        MaximizeIcon.Visibility = state.ShowRestoreIcon ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = state.ShowRestoreIcon ? Visibility.Visible : Visibility.Collapsed;
        MaximizeRestoreButton.ToolTip = state.ShowRestoreIcon ? "Restore" : "Maximize";
    }

    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null)
        {
            return null;
        }

        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
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
}
