using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using MyTools.Desktop.Components;
using MyTools.Desktop.Services;
using MyTools.Desktop.Utils;
using MyTools.Desktop.ViewModels;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using System.Runtime.InteropServices;

namespace MyTools.Desktop.Views;

/// <summary>
/// 插件独立窗口。与 <see cref="SearchWindow"/> 不同：
/// - 无 singletonLock，允许同一应用同时存在多个 PluginWindow（每种插件一个）。
/// - 无顶部搜索框，内容区直接渲染插件的详情页。
/// </summary>
public partial class PluginWindow
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const int WmNcLButtonDown = 0x00A1;
    private static readonly IntPtr HtCaption = new(0x0002);
    private const uint MonitorDefaultToNearest = 0x00000002;
    private readonly PluginViewModel viewModel;
    private HwndSource? hwndSource;
    private Point? pendingTitleBarDragStartPoint;
    private UIElement? pendingTitleBarDragRegion;

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
        SourceInitialized += Window_OnSourceInitialized;
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

        if (!ShouldForwardPluginNavigationKey(
                e.Key,
                Keyboard.Modifiers,
                Keyboard.FocusedElement,
                PluginContentView.IsKeyboardFocusWithin))
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

    internal static bool ShouldForwardPluginNavigationKey(
        Key key,
        ModifierKeys modifiers,
        IInputElement? focusedElement,
        bool isPluginContentFocused)
    {
        if (modifiers != ModifierKeys.None)
        {
            return false;
        }

        if (key != Key.Tab && key != Key.Enter)
        {
            return false;
        }

        if (isPluginContentFocused)
        {
            return false;
        }

        return focusedElement is not ButtonBase;
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        if (hwndSource != null)
        {
            hwndSource.RemoveHook(WndProc);
            hwndSource = null;
        }

        SourceInitialized -= Window_OnSourceInitialized;
        StateChanged -= Window_OnStateChanged;
        PreviewKeyDown -= Window_PreviewKeyDown;
        Closed -= Window_OnClosed;
        Loaded -= PluginWindow_Loaded;
        viewModel.Dispose();
    }

    private void Window_OnSourceInitialized(object? sender, EventArgs e)
    {
        var handle = new WindowInteropHelper(this).Handle;
        hwndSource = HwndSource.FromHwnd(handle);
        hwndSource?.AddHook(WndProc);
    }

    private void Window_OnStateChanged(object? sender, EventArgs e)
    {
        ApplyWindowChromeState();
    }

    private void TitleBarDragRegion_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        var isInteractiveControlSource = IsInteractiveTitleBarSource(e.OriginalSource as DependencyObject);
        var action = PluginWindowTitleBarDragBehavior.ResolveMouseLeftButtonDownAction(
            e.ChangedButton,
            e.ClickCount,
            isInteractiveControlSource);
        if (action == PluginWindowTitleBarDragAction.ToggleMaximizeRestore)
        {
            ClearPendingTitleBarDrag(releaseMouseCapture: true);
            ToggleMaximizeRestore();
            e.Handled = true;
            return;
        }

        if (sender is UIElement region
            && PluginWindowTitleBarDragBehavior.ShouldCaptureForPotentialDrag(
                e.ChangedButton,
                e.ClickCount,
                isInteractiveControlSource))
        {
            pendingTitleBarDragStartPoint = e.GetPosition(this);
            pendingTitleBarDragRegion = region;
            if (pendingTitleBarDragRegion.CaptureMouse())
            {
                e.Handled = true;
            }
            else
            {
                ClearPendingTitleBarDrag(releaseMouseCapture: false);
            }
        }
    }

    private void TitleBarDragRegion_OnMouseMove(object sender, MouseEventArgs e)
    {
        if (pendingTitleBarDragStartPoint is null || pendingTitleBarDragRegion == null)
        {
            return;
        }

        var action = PluginWindowTitleBarDragBehavior.ResolveMouseMoveAction(
            pendingTitleBarDragStartPoint.Value,
            e.GetPosition(this),
            WindowState,
            e.LeftButton,
            SystemParameters.MinimumHorizontalDragDistance,
            SystemParameters.MinimumVerticalDragDistance);
        if (action == PluginWindowTitleBarDragAction.None)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                ClearPendingTitleBarDrag(releaseMouseCapture: true);
            }

            return;
        }

        ClearPendingTitleBarDrag(releaseMouseCapture: true);
        BeginTitleBarDrag(action);
        e.Handled = true;
    }

    private void TitleBarDragRegion_OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        ClearPendingTitleBarDrag(releaseMouseCapture: true);
    }

    private void TitleBarDragRegion_OnLostMouseCapture(object sender, MouseEventArgs e)
    {
        ClearPendingTitleBarDrag(releaseMouseCapture: false);
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
        var maximizeRestoreCaption = state.ShowRestoreIcon
            ? LanguageService.GetCaption("PluginWindow.Restore", "Restore")
            : LanguageService.GetCaption("PluginWindow.Maximize", "Maximize");
        MaximizeRestoreButton.ToolTip = maximizeRestoreCaption;
        AutomationProperties.SetName(MaximizeRestoreButton, maximizeRestoreCaption);
    }

    private void BeginTitleBarDrag(PluginWindowTitleBarDragAction action)
    {
        switch (action)
        {
            case PluginWindowTitleBarDragAction.NativeCaptionDrag:
                BeginNativeCaptionDrag();
                return;
            case PluginWindowTitleBarDragAction.DragMove:
                try
                {
                    DragMove();
                }
                catch (InvalidOperationException)
                {
                }

                return;
            default:
                return;
        }
    }

    private void BeginNativeCaptionDrag()
    {
        var handle = hwndSource?.Handle ?? new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        if (!Native.GetCursorPos(out var cursorPosition))
        {
            return;
        }

        var lParam = new IntPtr(PluginWindowCaptionDragLParam.PackScreenCoordinates(cursorPosition.x, cursorPosition.y));
        ReleaseCapture();
        SendMessage(handle, WmNcLButtonDown, HtCaption, lParam);
    }

    private void ClearPendingTitleBarDrag(bool releaseMouseCapture)
    {
        var region = pendingTitleBarDragRegion;
        pendingTitleBarDragStartPoint = null;
        pendingTitleBarDragRegion = null;

        if (releaseMouseCapture && region?.IsMouseCaptured == true)
        {
            region.ReleaseMouseCapture();
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        handled = TryUpdateMaximizedBounds(hwnd, lParam);
        return IntPtr.Zero;
    }

    private static bool TryUpdateMaximizedBounds(IntPtr hwnd, IntPtr lParam)
    {
        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
        {
            return false;
        }

        var monitorInfo = MonitorInfo.Create();
        if (!GetMonitorInfo(monitor, ref monitorInfo))
        {
            return false;
        }

        var minMaxInfo = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var bounds = PluginWindowMaximizedBounds.FromMonitorInfo(
            new PluginWindowNativeRect(
                monitorInfo.MonitorArea.Left,
                monitorInfo.MonitorArea.Top,
                monitorInfo.MonitorArea.Right,
                monitorInfo.MonitorArea.Bottom),
            new PluginWindowNativeRect(
                monitorInfo.WorkArea.Left,
                monitorInfo.WorkArea.Top,
                monitorInfo.WorkArea.Right,
                monitorInfo.WorkArea.Bottom));

        minMaxInfo.MaxPosition = new NativePoint(bounds.PositionX, bounds.PositionY);
        minMaxInfo.MaxSize = new NativePoint(bounds.Width, bounds.Height);
        Marshal.StructureToPtr(minMaxInfo, lParam, false);
        return true;
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

    private static bool IsInteractiveTitleBarSource(DependencyObject? source)
    {
        while (source != null)
        {
            if (source is ButtonBase)
            {
                return true;
            }

            source = source is Visual
                ? VisualTreeHelper.GetParent(source)
                : null;
        }

        return false;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public uint Size;
        public MonitorRect MonitorArea;
        public MonitorRect WorkArea;
        public uint Flags;

        public static MonitorInfo Create()
        {
            return new MonitorInfo
            {
                Size = (uint)Marshal.SizeOf<MonitorInfo>()
            };
        }
    }
}
