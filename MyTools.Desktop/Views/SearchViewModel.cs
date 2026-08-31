using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Lucene.Net.Util;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Common.Config.Interfaces;
using MyTools.Desktop.Components;
using MyTools.Desktop.Services;
using MyTools.Plugins;
using MyTools.Plugins.NodePlugins;
using Application = System.Windows.Application;
using Key = System.Windows.Input.Key;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Keyboard = System.Windows.Input.Keyboard;

namespace MyTools.Desktop.ViewModels
{
    public partial class SearchViewModel : ObservableObject, ISearchViewModelCallback, IDisposable
    {
        [ObservableProperty]
        private IPlugin? selectedPlugin;

        [ObservableProperty]
        private string searchText = string.Empty;

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
        private ObservableCollection<IActionWithHotkey> selectedResultActions = new();
        
        private readonly SearchDebouncer searchDebouncer;
        private readonly IKeywordRegistry keywordRegistry;
        private readonly IServiceProvider serviceProvider;
        private readonly ILogger<SearchViewModel> logger;
        private readonly NodePluginDetailNavigator nodePluginDetailNavigator;
        private bool disposed;
        private NodePluginDetailContext? selectedNodeDetailContext;
         
        public IPlugin? ForcePlugin { get; set; }

        public string? CurrentNodePluginDetailId => selectedNodeDetailContext?.PluginId;

        public SearchViewModel(
            IKeywordRegistry keywordRegistry,
            IServiceProvider serviceProvider,
            ILogger<SearchViewModel> logger,
            NodePluginDetailNavigator nodePluginDetailNavigator,
            IConfigurationRegistry configurationRegistry)
        {
            this.keywordRegistry = keywordRegistry ?? throw new ArgumentNullException(nameof(keywordRegistry));
            this.serviceProvider = serviceProvider;
            this.logger = logger;
            this.nodePluginDetailNavigator = nodePluginDetailNavigator;
            this.nodePluginDetailNavigator.DetailRequested += OnNodePluginDetailRequested;

            CurrentViewModel = CreateViewModel<BasicListViewModel>();
            searchDebouncer = new SearchDebouncer(
                configurationRegistry,
                PerformSearch,
                action => Application.Current.Dispatcher.Invoke(action));
            searchDebouncer.Restart();
        }
        
        partial void OnSearchTextChanged(string value)
        {
            searchDebouncer.Restart();
        }
        
        private void PerformSearch()
        {
            if (disposed)
            {
                logger.LogWarning("SearchViewModel has been disposed, ignoring PerformSearch.");
                return;
            }
            
            SelectedPlugin = null;
            if (selectedNodeDetailContext != null && !selectedNodeDetailContext.Plugin.CanHandleSearchText(SearchText))
            {
                selectedNodeDetailContext = null;
            }

            string searchTextWithoutPrefix;
            if (ForcePlugin != null)
            {
                SelectedPlugin = ForcePlugin;
                searchTextWithoutPrefix = SearchText;
            }
            else if (selectedNodeDetailContext != null)
            {
                searchTextWithoutPrefix = selectedNodeDetailContext.Plugin.GetQueryWithoutKeyword(SearchText);
            }
            else
            {
                if (keywordRegistry.TryFindPlugin(SearchText, out searchTextWithoutPrefix, out var plugin))
                {
                    if (!plugin.IsEnabled)
                    {
                        return;
                    }

                    SelectedPlugin = plugin;

                    if (plugin is NodePlugin nodePlugin && nodePlugin.ShouldOpenDetailOnKeywordRoute(SearchText))
                    {
                        selectedNodeDetailContext = nodePlugin.CreateKeywordDetailContext(SearchText, searchTextWithoutPrefix);
                    }
                }
            }

            if (selectedNodeDetailContext != null)
            {
                SelectedPlugin = selectedNodeDetailContext.Plugin;
            }
            
            ResetViewModelIfNeeded(SelectedPlugin);
            ((ISwitchableViewModel)CurrentViewModel).PerformSearch(SelectedPlugin, searchTextWithoutPrefix);
        }

        public void ResetViewModelIfNeeded(IPlugin? _selectPlugin)
        {
            var requiredViewModelType = selectedNodeDetailContext != null
                ? ViewModelType.NodeDetail
                : _selectPlugin?.ViewModelType ?? ViewModelType.Basic;
            var viewmodel = GetOrCreateViewModel(requiredViewModelType, CurrentViewModel);

            if (viewmodel is NodePluginDetailViewModel nodePluginDetailViewModel)
            {
                nodePluginDetailViewModel.SetContext(selectedNodeDetailContext);
            }

            var currentPluginName = _selectPlugin is NodePlugin np ? np.GetDisplayName() : _selectPlugin?.Name;
            
            if (currentPluginName != null)
            {
                PluginName = currentPluginName;
            }
            else
            {
                PluginName = null;
            }

            var currentPluginVersion = selectedNodeDetailContext?.Version;
            PluginVersion = string.IsNullOrWhiteSpace(currentPluginVersion) ? null : currentPluginVersion;
            
            CurrentViewModel = viewmodel;
        }

        private ISwitchableViewModel GetOrCreateViewModel(ViewModelType required, object viewModel)
        {
            var switchableViewModel = viewModel as ISwitchableViewModel;
            if (switchableViewModel == null)
            {
                throw new NotSupportedException("ViewModel does not implement ISwitchableViewModel");
            }
            
            if(switchableViewModel.GetViewModelType() == required)
            {
                return switchableViewModel;
            }
            
            switchableViewModel.Dispose();
            ClearStatusBar();
            
            ISwitchableViewModel newVm;
            if (required == ViewModelType.Detail)
            {
                newVm = CreateViewModel<DetailedListViewModel>();
            }
            else if (required == ViewModelType.Basic)
            {
                newVm = CreateViewModel<BasicListViewModel>();
            }
            else if (required == ViewModelType.NodeDetail)
            {
                newVm = CreateViewModel<NodePluginDetailViewModel>();
            }
            else
            {
                throw new NotSupportedException("Not supported ViewModelType: " + required);
            }
            
            return newVm;
        }

        private T CreateViewModel<T>() where T : ISwitchableViewModel
        {
            return ActivatorUtilities.CreateInstance<T>(serviceProvider, this);
        }

        public void HandlePreviewKeyDown(KeyEventArgs e)
        {
            if (disposed)
            {
                logger.LogWarning("SearchViewModel has been disposed, ignoring HandlePreviewKeyDown.");
                return;
            }
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            var isHandled = ((ISwitchableViewModel)CurrentViewModel).HandleKeyDown(key, Keyboard.Modifiers);
            if (isHandled)
            {
                e.Handled = true;
            }
        }

        public void HandlePreviewKeyUp(KeyEventArgs e)
        {
            if (disposed)
            {
                logger.LogWarning("SearchViewModel has been disposed, ignoring HandlePreviewKeyUp.");
                return;
            }
            NotifyKeyUp(e.Key);
        }

        public void NotifyKeyUp(Key key)
        {
            if (disposed)
            {
                return;
            }

            ((ISwitchableViewModel)CurrentViewModel).HandleKeyUp(key);
        }
        
        #region ISearchViewModelCallback
        
        public void OnSearchTextChange(string text)
        {
            if (disposed)
            {
                logger.LogWarning("SearchViewModel has been disposed, ignoring SearchTextChangeMessage.");
                return;
            }
            if(SelectedPlugin != null && ForcePlugin == null){
                var keyword = keywordRegistry.GetKeyword(SelectedPlugin);
                if(keyword != null){
                    SearchText = keyword + " " + text;
                    return;
                }
            }
            
            SearchText = text;
        }
        
        private void ClearStatusBar()
        {
            Status = UpdateStatus.Idle;
            StatusText = string.Empty;
            DetailedStatusText = string.Empty;
            SelectedResultActions.Clear();
        }

        public void OnUpdateStatusBar(UpdateStatus status, string message)
        {
            if (disposed)
            {
                return;
            }
            Status = status;
            StatusText = GetStatusText(status);
            DetailedStatusText = message;
        }

        public void OnRequestClose()
        {
            WeakReferenceMessenger.Default.Send(new SearchWindowCloseMessage());
        }

        private string GetStatusText(UpdateStatus messageStatus)
        {
            switch (messageStatus)
            {
                case UpdateStatus.Pending:
                    return "Searching...";
                case UpdateStatus.Success:
                    return "Success";
                case UpdateStatus.Failed:
                    return "Failed...";
                default:
                    return "";
            }
        }

        public void OnUpdateSelectedActions(List<IActionWithHotkey>? actions)
        {
            SelectedResultActions.Clear();
            if (actions != null)
            {
                SelectedResultActions.AddRange(actions);
            }
        }

        #endregion
        
        [RelayCommand]
        private void ExecuteAction(IActionWithHotkey? action)
        {
            if (disposed)
            {
                logger.LogWarning("SearchViewModel has been disposed, ignoring ExecuteAction.");
                return;
            }

            if (action == null)
            {
                logger.LogWarning("ExecuteAction called without an action.");
                return;
            }
            
            ((ISwitchableViewModel)CurrentViewModel).ExecuteAction(action);
        }

        public void Dispose()
        {
            disposed = true;
            nodePluginDetailNavigator.DetailRequested -= OnNodePluginDetailRequested;
            searchDebouncer.Dispose();
            (CurrentViewModel as IDisposable)?.Dispose();
        }

        public void Refresh()
        {
            PerformSearch();
        }

        private void OnNodePluginDetailRequested(NodePluginDetailContext context)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                selectedNodeDetailContext = context;
                SearchText = context.SearchText;
                ResetViewModelIfNeeded(null);
                ((ISwitchableViewModel)CurrentViewModel).PerformSearch(null, context.Query);
            });
        }
    }
}
