using System.Windows;
using System.Windows.Automation;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private const uint MonitorDefaultToNearest = 0x00000002;
    private readonly PluginViewModel viewModel;
    private readonly ILogger<PluginWindow> logger;
    private HwndSource? hwndSource;
    private int activationAttempt;

    public PluginWindow(PluginViewModel viewModel)
        : this(viewModel, NullLogger<PluginWindow>.Instance)
    {
    }

    public PluginWindow(PluginViewModel viewModel, ILogger<PluginWindow> logger)
    {
        InitializeComponent();
        MinWidth = PluginWindowLayoutMetrics.MinimumWindowWidth;
        StateChanged += Window_OnStateChanged;
        ApplyWindowChromeState();

        this.viewModel = viewModel;
        this.logger = logger;
        DataContext = viewModel;
        viewModel.CloseRequested += ViewModel_OnCloseRequested;

        PreviewKeyDown += Window_PreviewKeyDown;
        Closed += Window_OnClosed;
        Loaded += PluginWindow_Loaded;
        SourceInitialized += Window_OnSourceInitialized;
    }

    public string? PluginId { get; private set; }
    internal bool HasPluginContent => viewModel.CurrentViewModel != null;

    /// <summary>
    /// 设置插件并应用详情上下文。窗口首次创建与重复刷新（复用）时都会调用。
    /// </summary>
    public void SetPlugin(NodePlugin plugin, NodePluginDetailContext? context)
    {
        PreparePluginShell(plugin, context);
        viewModel.SetPlugin(plugin, context);
    }

    internal void PreparePluginShell(NodePlugin plugin, NodePluginDetailContext? context)
    {
        PluginId = plugin.PluginId.Value;
        PluginStatusBar.Visibility = plugin.ShowStatusBarInPluginWindow
            ? Visibility.Visible
            : Visibility.Collapsed;
        ApplyPluginContentMargin();
        viewModel.SetPluginIdentity(plugin, context);
    }

    /// <summary>
    /// 激活窗口并把焦点放入插件详情页的主输入框。
    /// </summary>
    public async Task ActivatePluginAsync(bool requestForeground = true)
    {
        var attempt = ++activationAttempt;
        if (WindowState == WindowState.Minimized)
        {
            // Synchronous WPF restore preserves RestoreToMaximized before Activate/Focus run.
            WindowState = WindowState.Normal;
        }

        if (requestForeground) ActivateWindow();

        // Deferred page focus must not repeat a foreground request: the user may
        // have switched applications while the page was loading.
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        if (!IsVisible || attempt != activationAttempt) return;
        await viewModel.FocusPrimaryInputAsync(PluginContentView);
    }

    private void ActivateWindow()
    {
        WindowForeground.TryActivate(this);
    }

    internal void ActivateShellFromHotKey()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        WindowForeground.TryActivateFromHotKey(this, logger);
    }

    private async void PluginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
        await viewModel.FocusPrimaryInputAsync(PluginContentView);
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ResultActionBar.TryHandleOverflowHotkey(e, StatusActions))
        {
            return;
        }

        if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W) || e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
            return;
        }

        if (viewModel.TryExecuteHotkey(e.Key, e.SystemKey, Keyboard.Modifiers))
        {
            e.Handled = true;
            return;
        }

        if (!PluginDetailKeyForwarding.CanForward(
                Keyboard.FocusedElement,
                PluginContentView.IsKeyboardFocusWithin))
        {
            return;
        }

        var focusIntoPage = PluginDetailKeyForwarding.IsFocusIntoPageKey(e.Key, Keyboard.Modifiers);
        var hostKey = PluginDetailKeyForwarding.ResolveHostKey(e.Key, Keyboard.Modifiers);
        if (!focusIntoPage && hostKey == null)
        {
            return;
        }

        var detailView = FindVisualChild<NodePluginDetailView>(PluginContentView);
        if (detailView == null)
        {
            return;
        }

        e.Handled = true;
        if (focusIntoPage)
        {
            _ = detailView.FocusPrimaryInputAsync();
            return;
        }

        detailView.SendHostKey(hostKey!);
    }

    private void Window_OnClosed(object? sender, EventArgs e)
    {
        viewModel.CloseRequested -= ViewModel_OnCloseRequested;
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

    private void ViewModel_OnCloseRequested()
    {
        Close();
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
        WindowFrame.CornerRadius = state.CornerRadius;
        ApplyPluginContentMargin();
        MaximizeIcon.Visibility = state.ShowRestoreIcon ? Visibility.Collapsed : Visibility.Visible;
        RestoreIcon.Visibility = state.ShowRestoreIcon ? Visibility.Visible : Visibility.Collapsed;
        var maximizeRestoreCaption = state.ShowRestoreIcon
            ? LanguageService.GetCaption("PluginWindow.Restore", "Restore")
            : LanguageService.GetCaption("PluginWindow.Maximize", "Maximize");
        MaximizeRestoreButton.ToolTip = maximizeRestoreCaption;
        AutomationProperties.SetName(MaximizeRestoreButton, maximizeRestoreCaption);
    }

    private void ApplyPluginContentMargin()
    {
        PluginContentView.Margin = PluginWindowLayoutMetrics.GetPluginContentMargin(
            WindowState,
            PluginStatusBar.Visibility == Visibility.Visible);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != WmGetMinMaxInfo)
        {
            return IntPtr.Zero;
        }

        handled = TryUpdateMinMaxInfo(hwnd, lParam);
        return IntPtr.Zero;
    }

    private bool TryUpdateMinMaxInfo(IntPtr hwnd, IntPtr lParam)
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

        var (dpiScaleX, dpiScaleY) = GetCurrentDpiScale();
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
        minMaxInfo.MinTrackSize = new NativePoint(
            PluginWindowLayoutMetrics.DipToDevicePixels(MinWidth, dpiScaleX),
            PluginWindowLayoutMetrics.DipToDevicePixels(MinHeight, dpiScaleY));
            Marshal.StructureToPtr(minMaxInfo, lParam, false);
        return true;
    }

    private (double X, double Y) GetCurrentDpiScale()
    {
        var compositionTarget = hwndSource?.CompositionTarget;
        if (compositionTarget != null)
        {
            var transform = compositionTarget.TransformToDevice;
            if (transform.M11 > 0 && transform.M22 > 0)
            {
                return (transform.M11, transform.M22);
            }
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        return (dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1d, dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1d);
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

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfo lpmi);

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
