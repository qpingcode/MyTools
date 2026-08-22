using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MyTools.Common;
using MyTools.Desktop.ViewModels;

namespace MyTools.Desktop.Components;

public partial class ResultActionBar : UserControl
{
    public static readonly Hotkey OverflowHotkey = Hotkey.Ctrl(HotkeyKey.K);
    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(
            nameof(Actions),
            typeof(IEnumerable),
            typeof(ResultActionBar),
            new PropertyMetadata(null, OnActionsChanged));

    public static readonly DependencyProperty DefaultActionProperty =
        DependencyProperty.Register(
            nameof(DefaultAction),
            typeof(IActionWithHotkey),
            typeof(ResultActionBar));

    public static readonly DependencyProperty OverflowActionsProperty =
        DependencyProperty.Register(
            nameof(OverflowActions),
            typeof(IList),
            typeof(ResultActionBar));

    public static readonly DependencyProperty HasOverflowProperty =
        DependencyProperty.Register(
            nameof(HasOverflow),
            typeof(bool),
            typeof(ResultActionBar));

    private INotifyCollectionChanged? subscribedActions;

    public ResultActionBar()
    {
        InitializeComponent();
        OverflowActions = Array.Empty<IActionWithHotkey>();
        OverflowMenu.PreviewKeyDown += OverflowMenu_OnPreviewKeyDown;
        OverflowMenu.PreviewKeyUp += OverflowMenu_OnPreviewKeyUp;
        OverflowMenu.Opened += OverflowMenu_OnOpened;
        Unloaded += (_, _) => DetachActions();
    }

    public IEnumerable? Actions
    {
        get => (IEnumerable?)GetValue(ActionsProperty);
        set => SetValue(ActionsProperty, value);
    }

    public IActionWithHotkey? DefaultAction
    {
        get => (IActionWithHotkey?)GetValue(DefaultActionProperty);
        set => SetValue(DefaultActionProperty, value);
    }

    public IList OverflowActions
    {
        get => (IList)GetValue(OverflowActionsProperty);
        set => SetValue(OverflowActionsProperty, value);
    }

    public bool HasOverflow
    {
        get => (bool)GetValue(HasOverflowProperty);
        set => SetValue(HasOverflowProperty, value);
    }

    public static bool TryHandleOverflowHotkey(KeyEventArgs e, ResultActionBar? bar)
    {
        if (bar == null)
        {
            return false;
        }

        // Window-level PreviewKeyDown runs before the ContextMenu receives Escape. Consume it
        // here so the first Escape dismisses the topmost UI layer instead of closing the window.
        if (e.Key == Key.Escape && bar.OverflowMenu.IsOpen)
        {
            bar.OverflowMenu.IsOpen = false;
            e.Handled = true;
            return true;
        }

        if (!bar.HasOverflow)
        {
            return false;
        }

        if (!ResultActionBarHotkeys.IsOverflowToggle(e.Key, e.SystemKey, Keyboard.Modifiers))
        {
            return false;
        }

        bar.OpenOverflow();
        e.Handled = true;
        return true;
    }

    public void OpenOverflow()
    {
        if (!HasOverflow)
        {
            return;
        }

        OverflowMenu.PlacementTarget = OverflowButton;
        OverflowMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Top;
        var opening = !OverflowMenu.IsOpen;
        OverflowMenu.IsOpen = opening;
        if (opening)
        {
            NotifyHostCtrlReleased();
        }
    }

    private static void OnActionsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var bar = (ResultActionBar)d;
        bar.DetachActions();
        if (e.NewValue is INotifyCollectionChanged incc)
        {
            bar.subscribedActions = incc;
            incc.CollectionChanged += bar.OnActionsCollectionChanged;
        }

        bar.RefreshSplit();
    }

    private void OnActionsCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RefreshSplit();
    }

    private void DetachActions()
    {
        if (subscribedActions == null)
        {
            return;
        }

        subscribedActions.CollectionChanged -= OnActionsCollectionChanged;
        subscribedActions = null;
    }

    private void RefreshSplit()
    {
        var actions = Actions?.OfType<IActionWithHotkey>();
        var (primary, overflow) = ResultActionBarSplit.Split(actions);
        DefaultAction = primary;
        var overflowList = overflow.ToList();
        OverflowActions = overflowList;
        HasOverflow = overflowList.Count > 0;
        if (OverflowMenu != null)
        {
            OverflowMenu.ItemsSource = overflowList;
        }
    }

    private void OverflowButton_OnClick(object sender, RoutedEventArgs e)
    {
        OpenOverflow();
    }

    private void OverflowMenu_OnOpened(object sender, RoutedEventArgs e)
    {
        // ContextMenu opened by setting IsOpen does not reliably choose an initial MenuItem,
        // especially with our custom template. Wait until its item containers exist, then make
        // keyboard selection visible and deterministic.
        _ = OverflowMenu.Dispatcher.BeginInvoke(
            () => FocusOverflowItem(0),
            DispatcherPriority.Input);
    }

    private void OverflowMenu_OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (ResultActionBarHotkeys.IsCtrlKey(e.Key))
        {
            return;
        }

        if (e.Key == Key.Escape
            || ResultActionBarHotkeys.IsOverflowToggle(e.Key, e.SystemKey, Keyboard.Modifiers))
        {
            OverflowMenu.IsOpen = false;
            e.Handled = true;
            return;
        }

        if (e.Key is Key.Down or Key.Up)
        {
            var count = OverflowMenu.Items.Count;
            if (count == 0)
            {
                return;
            }

            var currentIndex = GetFocusedOverflowIndex();
            var nextIndex = e.Key == Key.Down
                ? (currentIndex + 1 + count) % count
                : (currentIndex <= 0 ? count : currentIndex) - 1;
            FocusOverflowItem(nextIndex);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && GetFocusedOverflowAction() is { } selectedAction)
        {
            OverflowMenu.IsOpen = false;
            Execute(selectedAction);
            e.Handled = true;
            return;
        }

        var hotkey = ResultActionBarHotkeys.ToHotkey(e.Key, Keyboard.Modifiers);
        if (hotkey == null || !HasActionHotkey(hotkey.Value))
        {
            return;
        }

        OverflowMenu.IsOpen = false;
        Execute(Actions!.OfType<IActionWithHotkey>().First(action => action.Hotkey == hotkey.Value));
        e.Handled = true;
    }

    private void OverflowMenu_OnPreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (!ResultActionBarHotkeys.IsCtrlKey(e.Key))
        {
            return;
        }

        NotifyHostCtrlReleased();
        e.Handled = true;
    }

    private void OverflowMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: IActionWithHotkey action })
        {
            return;
        }

        OverflowMenu.IsOpen = false;
        Execute(action);
    }

    private int GetFocusedOverflowIndex()
    {
        for (var index = 0; index < OverflowMenu.Items.Count; index++)
        {
            if (OverflowMenu.ItemContainerGenerator.ContainerFromIndex(index) is MenuItem { IsKeyboardFocusWithin: true })
            {
                return index;
            }
        }

        return -1;
    }

    private IActionWithHotkey? GetFocusedOverflowAction()
    {
        var index = GetFocusedOverflowIndex();
        return index >= 0 ? OverflowMenu.Items[index] as IActionWithHotkey : null;
    }

    private void FocusOverflowItem(int index)
    {
        if (!OverflowMenu.IsOpen || index < 0 || index >= OverflowMenu.Items.Count)
        {
            return;
        }

        OverflowMenu.UpdateLayout();
        if (OverflowMenu.ItemContainerGenerator.ContainerFromIndex(index) is MenuItem item)
        {
            item.Focus();
            Keyboard.Focus(item);
        }
    }

    private bool HasActionHotkey(Hotkey hotkey)
    {
        return Actions?.OfType<IActionWithHotkey>().Any(action => action.Hotkey == hotkey)
               == true;
    }

    private void NotifyHostCtrlReleased()
    {
        if (Window.GetWindow(this)?.DataContext is SearchViewModel search)
        {
            search.NotifyKeyUp(Key.LeftCtrl);
        }
    }

    private void Execute(IActionWithHotkey action)
    {
        var window = Window.GetWindow(this);
        switch (window?.DataContext)
        {
            case SearchViewModel search:
                search.ExecuteActionCommand.Execute(action);
                break;
            case PluginViewModel plugin:
                plugin.ExecuteActionCommand.Execute(action);
                break;
        }
    }
}
