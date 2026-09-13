using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MyTools.Common;
using MyTools.Desktop.Views;
using Microsoft.Extensions.Logging;
using MyTools.Common.DependencyInjection;

namespace MyTools.Desktop.Components;

public partial class BasicListView : IVisibleItemProvider
{
    private Point dragStart;
    private ResultItem? dragCandidate;
    private BasicListViewModel viewModel => (DataContext as BasicListViewModel)!;
    
    public static readonly DependencyProperty IconVisibleProperty =
        DependencyProperty.Register(
            nameof(IconVisible),
            typeof(bool),
            typeof(BasicListView),
            new PropertyMetadata(true));
   
    public bool IconVisible
    {
        get => (bool)GetValue(IconVisibleProperty);
        set => SetValue(IconVisibleProperty, value);
    }

    public static readonly DependencyProperty SingleLineTextProperty =
        DependencyProperty.Register(
            nameof(SingleLineText),
            typeof(bool),
            typeof(BasicListView),
            new PropertyMetadata(false));

    public bool SingleLineText
    {
        get => (bool)GetValue(SingleLineTextProperty);
        set => SetValue(SingleLineTextProperty, value);
    }

    public static readonly DependencyProperty IconColumnWidthProperty =
        DependencyProperty.Register(
            nameof(IconColumnWidth),
            typeof(GridLength),
            typeof(BasicListView),
            new PropertyMetadata(new GridLength(50)));

    public GridLength IconColumnWidth
    {
        get => (GridLength)GetValue(IconColumnWidthProperty);
        set => SetValue(IconColumnWidthProperty, value);
    }

    public static readonly DependencyProperty IconMarginProperty =
        DependencyProperty.Register(
            nameof(IconMargin),
            typeof(Thickness),
            typeof(BasicListView),
            new PropertyMetadata(new Thickness(0, 0, 8, 0)));

    public Thickness IconMargin
    {
        get => (Thickness)GetValue(IconMarginProperty);
        set => SetValue(IconMarginProperty, value);
    }

    public static readonly DependencyProperty IconContainerStyleProperty =
        DependencyProperty.Register(
            nameof(IconContainerStyle),
            typeof(Style),
            typeof(BasicListView),
            new PropertyMetadata(null));

    public Style? IconContainerStyle
    {
        get => (Style?)GetValue(IconContainerStyleProperty);
        set => SetValue(IconContainerStyleProperty, value);
    }
    
    public BasicListView()
    {
        InitializeComponent();
        ResultsListBox.PreviewMouseLeftButtonDown += ResultsListBox_PreviewMouseLeftButtonDown;
        ResultsListBox.PreviewMouseMove += ResultsListBox_PreviewMouseMove;
        ResultsListBox.PreviewMouseLeftButtonUp += (_, _) => dragCandidate = null;
        ResultsListBox.Unloaded += (_, _) => dragCandidate = null;
        DataContextChanged += BasicListView_DataContextChanged;
    }

    private void ResultsListBox_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragCandidate = null;
        if (e.ClickCount != 1 || e.OriginalSource is not DependencyObject source) return;
        var container = ItemsControl.ContainerFromElement(ResultsListBox, source) as ListBoxItem;
        dragCandidate = container?.DataContext as ResultItem;
        dragStart = e.GetPosition(ResultsListBox);
    }

    private void ResultsListBox_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            dragCandidate = null;
            return;
        }
        if (dragCandidate == null) return;
        var position = e.GetPosition(ResultsListBox);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        var candidate = dragCandidate;
        dragCandidate = null;
        try
        {
            var data = ResultFileDragSource.CreateDataObject(candidate.Args);
            if (data == null) return;
            e.Handled = true;
            var searchWindow = Window.GetWindow(this) as SearchWindow;
            using (searchWindow?.SuppressAutoHide())
            {
                DragDrop.DoDragDrop(ResultsListBox, data, DragDropEffects.Copy);
            }
        }
        catch (Exception ex)
        {
            ServiceLocator.GetRequiredService<ILogger<BasicListView>>()
                .LogWarning(ex, "Failed to drag result files.");
        }
    }

    private void BasicListView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (viewModel != null)
        {
            viewModel.VisibleItemProvider = this;
        }
    }

    private void ResultsListBox_Loaded(object sender, RoutedEventArgs e)
    {
        var scrollViewer = FindVisualChild<ScrollViewer>(ResultsListBox);
        if (scrollViewer != null)
        {
            scrollViewer.ScrollChanged += ScrollViewer_ScrollChanged;
        }
    }
        
    private void ScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (viewModel.IsCtrlPressed)
        {
            viewModel.UpdateNumberLabels();
        }
    }

    private void ResultsListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count <= 0 || e.AddedItems[0] is not ResultItem)
        {
            return;
        };
        viewModel.SelectedResult = (ResultItem)e.AddedItems[0]!;
        EnsureSelectedItemVisible();
    }

    private void EnsureSelectedItemVisible()
    {
        if (ResultsListBox.SelectedItem != null)
        {
            ResultsListBox.ScrollIntoView(ResultsListBox.SelectedItem);
        }
    }

    private void ResultsListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (viewModel.SelectedResult != null)
        {
            viewModel.ExecuteAction(null);
        }
    }
    
    public void Receive(GetVisibleItemMessage message)
    {
        var visibleItems = GetVisibleItems();
        message.Reply(visibleItems);
    }

    public List<ResultItem> GetVisibleItems()
    {
        var visibleItems = new List<ResultItem>();
        var scrollViewer = FindVisualChild<ScrollViewer>(ResultsListBox);
        if (scrollViewer == null) return visibleItems;

        var presenter = FindVisualChild<ScrollContentPresenter>(scrollViewer);
        var viewportHeight = presenter?.ActualHeight ?? scrollViewer.ViewportHeight;
        var topThreshold = scrollViewer.VerticalOffset;
        var bottomThreshold = topThreshold + viewportHeight;

        for (var i = 0; i < ResultsListBox.Items.Count; i++)
        {
            var container = ResultsListBox.ItemContainerGenerator.ContainerFromIndex(i) as FrameworkElement;
            if (container == null) continue;

            var transform = container.TransformToAncestor(scrollViewer);
            var position = transform.Transform(new Point(0, 0));
            var itemTop = position.Y;
            var itemBottom = itemTop + container.ActualHeight;

            if (itemTop < bottomThreshold && itemBottom > topThreshold)
            {
                if (ResultsListBox.Items[i] is ResultItem resultItem)
                {
                    visibleItems.Add(resultItem);
                }
            }
        }

        return visibleItems;
    }
    
    private static T? FindVisualChild<T>(DependencyObject? parent) where T : DependencyObject
    {
        if (parent == null) return null;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result)
                return result;
            var descendant = FindVisualChild<T>(child);
            if (descendant != null)
                return descendant;
        }
        return null;
    }
}
