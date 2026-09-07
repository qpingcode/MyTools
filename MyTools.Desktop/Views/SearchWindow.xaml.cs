using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using MyTools.Desktop.Components;
using MyTools.Desktop.Services;
using MyTools.Desktop.ViewModels;
using MyTools.Plugins;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MyTools.Desktop.Views
{
    public partial class SearchWindow :
        IRecipient<SearchWindowCloseMessage>,
        IRecipient<SearchRefreshMessage>,
        IRecipient<ClipboardHistoryChangedMessage>
    {
        private readonly SearchViewModel viewModel;
        private readonly AutoHideSuppressionState autoHideSuppression = new();
        private bool persistentShellPrepared;

        public SearchWindow(SearchViewModel searchViewModel)
        {
            InitializeComponent();
            //WindowFocusTopmost.Attach(this);

            viewModel = searchViewModel;
            DataContext = viewModel;

            SearchTextBox.PreviewKeyDown += SearchTextBox_PreviewKeyDown;
            SearchTextBox.PreviewKeyDown += SearchTextBox_HandleViewModelPreviewKeyDown;
            SearchTextBox.Focus();

            PreviewKeyDown += Window_HandlePreviewKeyDown;
            PreviewKeyUp += (sender, e) => viewModel.HandlePreviewKeyUp(e);;
            KeyDown += Window_KeyDown;
            Closing += Window_OnClosing;
            Closed += Window_OnClosed;
            Deactivated += Window_OnDeactivated;

            MouseLeftButtonDown += (s, e) => DragMove();
            
            WeakReferenceMessenger.Default.Register<SearchWindowCloseMessage>(this);
            WeakReferenceMessenger.Default.Register<SearchRefreshMessage>(this);
            WeakReferenceMessenger.Default.Register<ClipboardHistoryChangedMessage>(this);
        }

        public void Refresh()
        {
            viewModel.Refresh();
        }

        /// <summary>
        /// Prevents focus changes caused by an explicitly scoped dialog or picker
        /// from hiding the search window. The returned scope may be nested.
        /// </summary>
        internal IDisposable SuppressAutoHide() => autoHideSuppression.Enter();

        private void SearchDragHandle_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Left)
            {
                return;
            }

            e.Handled = true;
            DragMove();
        }

        public void Receive(ClipboardHistoryChangedMessage message)
        {
            Dispatcher.Invoke(viewModel.Refresh);
        }

        public IPlugin? CurrentPlugin => viewModel.ForcePlugin;

        public string? CurrentNodePluginDetailId => viewModel.CurrentNodePluginDetailId;

        public void SetPluginWindow(IPlugin? plugin)
        {
            viewModel.SetForcedPlugin(plugin);
        }

        internal void PreparePersistentShell(WindowPlacementService placement)
        {
            if (persistentShellPrepared)
            {
                return;
            }

            placement.Track(this, WindowPlacementService.SearchKey);
            _ = new WindowInteropHelper(this).EnsureHandle();
            ApplyTemplate();
            Measure(new Size(Width, Height));
            Arrange(new Rect(0, 0, Width, Height));
            UpdateLayout();
            persistentShellPrepared = true;
        }

        public async Task FocusNodePluginPrimaryInputAsync()
        {
            await Dispatcher.Yield(DispatcherPriority.ApplicationIdle);
            var detailView = FindVisualChild<NodePluginDetailView>(CurrentSearchResultView);
            if (detailView != null)
            {
                await detailView.FocusPrimaryInputAsync();
            }
        }

        private void Shutdown()
        {
            Hide();
        }

        private void Window_OnClosing(object? sender, CancelEventArgs e)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished)
            {
                return;
            }

            e.Cancel = true;
            Hide();
        }
        
        private void Window_OnClosed(object? sender, EventArgs e)
        {
            Closing -= Window_OnClosing;
            Deactivated -= Window_OnDeactivated;
            WeakReferenceMessenger.Default.UnregisterAll(this);
            viewModel.Dispose();
        }

        private void Window_OnDeactivated(object? sender, EventArgs e)
        {
            // Let the new foreground window and any explicit suppression scope settle.
            _ = Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle, () =>
            {
                if (IsVisible && !IsActive && !autoHideSuppression.IsSuppressed)
                {
                    Hide();
                }
            });
        }
        
        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.A)
            {
                if (!SearchTextBox.IsKeyboardFocusWithin)
                {
                    return;
                }

                e.Handled = true;
                SearchTextBox.SelectAll();
                return;
            }
            
            if ((Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.W) || e.Key == Key.Escape)
            {
                e.Handled = true;
                Shutdown();
                return;
            }

            switch (e.Key)
            {
                case Key.Escape:
                    Shutdown();
                    break;
                case Key.Delete:
                case Key.Back:
                {
                    if (!SearchTextBox.IsFocused)
                    {
                        SearchTextBox.Focus();
                    }
                    break;
                }
            }
        }

        private void Window_HandlePreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (ResultActionBar.TryHandleOverflowHotkey(e, StatusActionBar))
            {
                return;
            }

            viewModel.HandlePreviewKeyDown(e);
        }

        private void SearchTextBox_HandleViewModelPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Handled)
            {
                return;
            }

            viewModel.HandlePreviewKeyDown(e);
        }

        private async void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var focusIntoPage = PluginDetailKeyForwarding.IsFocusIntoPageKey(e.Key, Keyboard.Modifiers);
            var hostKey = PluginDetailKeyForwarding.ResolveHostKey(e.Key, Keyboard.Modifiers);
            if (!focusIntoPage && hostKey == null)
            {
                return;
            }

            var detailView = FindVisualChild<NodePluginDetailView>(CurrentSearchResultView);
            if (detailView == null)
            {
                return;
            }

            e.Handled = true;
            if (focusIntoPage)
            {
                await detailView.FocusPrimaryInputAsync();
                return;
            }

            detailView.SendHostKey(hostKey!);
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

        public void Receive(SearchWindowCloseMessage message)
        {
            Dispatcher.Invoke(Shutdown);
        }

        public void Receive(SearchRefreshMessage message)
        {
            Dispatcher.Invoke(viewModel.Refresh);
        }
    }
}
