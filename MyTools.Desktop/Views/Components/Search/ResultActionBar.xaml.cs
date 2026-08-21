using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyTools.Common;
using MyTools.Desktop.ViewModels;

namespace MyTools.Desktop.Components;

public partial class ResultActionBar : UserControl
{
    public static readonly DependencyProperty ActionsProperty =
        DependencyProperty.Register(
            nameof(Actions),
            typeof(IEnumerable),
            typeof(ResultActionBar),
            new PropertyMetadata(null, OnActionsChanged));

    public static readonly DependencyProperty DefaultActionProperty =
        DependencyProperty.Register(
            nameof(DefaultAction),
            typeof(IActionWithCommand),
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
        OverflowActions = Array.Empty<IActionWithCommand>();
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

    public IActionWithCommand? DefaultAction
    {
        get => (IActionWithCommand?)GetValue(DefaultActionProperty);
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
        if (bar is not { HasOverflow: true })
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
        var actions = Actions?.OfType<IActionWithCommand>();
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
        OverflowMenu.Focus();
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

        var command = ResultActionBarHotkeys.ToCommand(e.Key, Keyboard.Modifiers);
        if (command == null || !HasActionCommand(command))
        {
            return;
        }

        OverflowMenu.IsOpen = false;
        Execute(command);
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
        if (sender is not MenuItem { DataContext: IActionWithCommand action })
        {
            return;
        }

        OverflowMenu.IsOpen = false;
        Execute(action.Command);
    }

    private bool HasActionCommand(string command)
    {
        return Actions?.OfType<IActionWithCommand>().Any(action =>
                   string.Equals(action.Command, command, StringComparison.Ordinal))
               == true;
    }

    private void NotifyHostCtrlReleased()
    {
        if (Window.GetWindow(this)?.DataContext is SearchViewModel search)
        {
            search.NotifyKeyUp(Key.LeftCtrl);
        }
    }

    private void Execute(string command)
    {
        var window = Window.GetWindow(this);
        switch (window?.DataContext)
        {
            case SearchViewModel search:
                search.ExecuteActionCommand.Execute(command);
                break;
            case PluginViewModel plugin:
                plugin.ExecuteActionCommand.Execute(command);
                break;
        }
    }
}
