using System.Collections.ObjectModel;
using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using MyTools.Common;
using MyTools.Plugins;

namespace MyTools.Desktop.Components;

public partial class DetailedListViewModel : ObservableObject, ISwitchableViewModel
{
    private const int MaxVisibleCategories = 8; // 最多显示8个分类按钮

    private BasicListViewModel basicListViewModel;
    
    public DetailedListViewModel(ISearchViewModelCallback callback, IActionRegistry actionRegistry, ISearcher searcher, ILogger<DetailedListViewModel> logger, ILogger<BasicListViewModel> logger2)
    {
        basicListViewModel = new BasicListViewModel(callback, actionRegistry, searcher, logger2);
    }

    public BasicListViewModel BasicViewModel => basicListViewModel;
    
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMoreCategories))]
    [NotifyPropertyChangedFor(nameof(VisibleCategories))]
    [NotifyPropertyChangedFor(nameof(HiddenCategories))]
    private ObservableCollection<string> categories = new ();

    public bool HasMoreCategories => Categories.Count > MaxVisibleCategories;
    
    public IEnumerable<string> VisibleCategories => Categories.Take(Math.Min(MaxVisibleCategories, Categories.Count));

    public IEnumerable<string> HiddenCategories => MaxVisibleCategories < Categories.Count() ? Categories.Skip(MaxVisibleCategories) : Enumerable.Empty<string>();
    
    [ObservableProperty]
    private string selectedCategory = string.Empty;
    
    [ObservableProperty]
    private ResultItem? selectedItem;
    
    [RelayCommand]
    private void SelectCategory(string category)
    {
        SelectedCategory = category ?? string.Empty;
    }
    
    private void UpdateCategories()
    {
        var uniqueCategories = basicListViewModel.SearchResults
            .Where(item => !string.IsNullOrEmpty(item.Category))
            .Select(item => item.Category)
            .Distinct()
            .OrderBy(cat => cat)
            .ToList();
        
        Categories.Clear();
        foreach (var category in uniqueCategories)
        {
            Categories.Add(category);
        }
    }


    public void PerformSearch(IPlugin? plugin, string searchText)
    {
        basicListViewModel.PerformSearch(plugin, searchText);
    }

    public bool HandleKeyDown(Key key, ModifierKeys modifiers)
    {
        return basicListViewModel.HandleKeyDown(key, modifiers);
    }

    public bool HandleKeyUp(Key key)
    {
        return basicListViewModel.HandleKeyUp(key);
    }

    public void ExecuteAction(IActionWithHotkey? action)
    {
        basicListViewModel.ExecuteAction(action);
    }

    public void Dispose()
    {
        basicListViewModel.Dispose();
    }

    public ViewModelType GetViewModelType()
    {
        return ViewModelType.Detail;
    }
}
