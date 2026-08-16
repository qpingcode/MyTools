using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using MyTools.Common.DependencyInjection;
using MyTools.Desktop.Components;
using MyTools.Desktop.Services;
using MyTools.Desktop.ViewModels;
using MyTools.Plugins;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace MyTools.Desktop.Views
{
    public partial class SearchWindow : IRecipient<SearchWindowCloseMessage>
    {
        private static readonly object singletonLock = new();
        private readonly SearchViewModel viewModel;
        private HwndSource? hwndSource;

        public SearchWindow(SearchViewModel searchViewModel)
        {
            if (!Monitor.TryEnter(singletonLock))
            {
                Shutdown();
            }
            InitializeComponent();

            viewModel = searchViewModel;
            DataContext = viewModel;

            SearchTextBox.PreviewKeyDown += SearchTextBox_PreviewKeyDown;
            SearchTextBox.PreviewKeyDown += (s, e) => viewModel.HandlePreviewKeyDown(e);
            SearchTextBox.Focus();

            PreviewKeyDown += (s, e) => viewModel.HandlePreviewKeyDown(e);
            PreviewKeyUp += (sender, e) => viewModel.HandlePreviewKeyUp(e);;
            KeyDown += Window_KeyDown;
            Closed += Window_OnClosed;
            SourceInitialized += Window_OnSourceInitialized;

            MouseLeftButtonDown += (s, e) => DragMove();
            
            WeakReferenceMessenger.Default.Register(this);
        }

        public void Refresh()
        {
            viewModel.Refresh();
        }

        public IPlugin? CurrentPlugin => viewModel.ForcePlugin;

        public string? CurrentNodePluginDetailId => viewModel.CurrentNodePluginDetailId;

        public void SetPluginWindow(IPlugin? plugin)
        {
            viewModel.ForcePlugin = plugin;
            viewModel.ResetViewModelIfNeeded(plugin);
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
            Close();
        }
        
        private void Window_OnClosed(object? sender, EventArgs e)
        {
            if (hwndSource != null)
            {
                hwndSource.RemoveHook(WndProc);
                hwndSource = null;
            }

            SourceInitialized -= Window_OnSourceInitialized;
            WeakReferenceMessenger.Default.UnregisterAll(this);
            viewModel.Dispose();
        }

        private void Window_OnSourceInitialized(object? sender, EventArgs e)
        {
            var handle = new WindowInteropHelper(this).Handle;
            hwndSource = HwndSource.FromHwnd(handle);
            hwndSource?.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (WindowSystemMenuFilter.ShouldSuppress(msg, wParam, AreHotKeysSuspended()))
            {
                handled = true;
            }

            return IntPtr.Zero;
        }

        private static bool AreHotKeysSuspended()
        {
            try
            {
                return ServiceLocator.GetService<HotKeyManager>()?.AreHotKeysSuspended == true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
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

        private async void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (Keyboard.Modifiers != ModifierKeys.None)
            {
                return;
            }

            if (e.Key != Key.Tab && e.Key != Key.Enter)
            {
                return;
            }
            
            var detailView = FindVisualChild<NodePluginDetailView>(CurrentSearchResultView);
            if (detailView == null)
            {
                return;
            }

            e.Handled = true;
            if (e.Key == Key.Tab)
            {
                await detailView.FocusPrimaryInputAsync();
                return;
            }

            detailView.SendHostKey("Enter");
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
    }
}