using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Desktop.Services;
using MyTools.Desktop.Utils;

namespace MyTools.Desktop.ViewModels;

public partial class ConfigurationViewModel : ObservableRecipient
{
    private readonly IConfigurationRegistry _registry;

    [ObservableProperty] private string searchText = string.Empty;

    [ObservableProperty] private ConfigurationCategory? currentCategory;

    [ObservableProperty] private ObservableCollection<ConfigurationSetting> _currentSettings = new();

    [ObservableProperty] private ObservableCollection<ConfigurationCategory> _filteredCategories = new();

    public bool HasUnsavedChanges => _registry.GetModifiedSettings().Any();

    public ConfigurationViewModel()
    {
        _registry = ServiceLocator.GetRequiredService<IConfigurationRegistry>();

        InitializeData();
    }

    private void InitializeData()
    {
        var rootCategories = _registry.GetRootCategories().ToList();
        SetAllCategoriesExpanded(rootCategories);
        FilteredCategories = new ObservableCollection<ConfigurationCategory>(rootCategories);

        if (FilteredCategories.Count > 0)
        {
            CurrentCategory = FilteredCategories[0];
        }
    }

    private void SetAllCategoriesExpanded(IEnumerable<ConfigurationCategory> categories)
    {
        foreach (var category in categories)
        {
            category.IsExpanded = true;
            if (category.Children.Count > 0)
            {
                SetAllCategoriesExpanded(category.Children);
            }
        }
    }

    public void PerformSearch()
    {
        if (string.IsNullOrWhiteSpace(SearchText))
        {
            var rootCategories = _registry.GetRootCategories().ToList();
            SetAllCategoriesExpanded(rootCategories);
            FilteredCategories = new ObservableCollection<ConfigurationCategory>(rootCategories);
        }
        else
        {
            var searchResults = _registry.Search(SearchText);
            var categoriesToShow = new HashSet<ConfigurationCategory>();

            foreach (var result in searchResults)
            {
                if (result is ConfigurationCategory category)
                {
                    categoriesToShow.Add(category);

                    var parent = category.Parent;
                    while (parent != null)
                    {
                        categoriesToShow.Add(parent);
                        parent = parent.Parent;
                    }
                }
                else if (result is ConfigurationSetting setting && setting.Category != null)
                {
                    categoriesToShow.Add(setting.Category);

                    var parent = setting.Category.Parent;
                    while (parent != null)
                    {
                        categoriesToShow.Add(parent);
                        parent = parent.Parent;
                    }
                }
            }

            var searchResultTree = BuildSearchResultTree(categoriesToShow);
            SetAllCategoriesExpanded(searchResultTree);
            FilteredCategories = new ObservableCollection<ConfigurationCategory>(searchResultTree);
        }
        
        if (CurrentCategory == null || FindFirstMatchingCategory(FilteredCategories, v => v == CurrentCategory) == null)
        {
            var selectableCategory = FindFirstMatchingCategory(FilteredCategories, v => v.IsSelectable);
            CurrentCategory = selectableCategory;
        }
    }
    
    private ConfigurationCategory? FindFirstMatchingCategory(
        IEnumerable<ConfigurationCategory> filteredCategories, 
        Predicate<ConfigurationCategory> predicate)
    {
        var queue = new Queue<ConfigurationCategory>();
        foreach (var root in filteredCategories)
        {
            queue.Enqueue(root);
        }

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (predicate(current)) return current;
            foreach (var child in current.Children)
            {
                queue.Enqueue(child);
            }
        }

        return null;
    }

    private List<ConfigurationCategory> BuildSearchResultTree(HashSet<ConfigurationCategory> categoriesToShow)
    {
        var result = new List<ConfigurationCategory>();

        var rootCategories = _registry.GetRootCategories();

        foreach (var rootCategory in rootCategories)
        {
            var filteredRoot = FilterCategoryForSearch(rootCategory, categoriesToShow);
            if (filteredRoot != null)
            {
                result.Add(filteredRoot);
            }
        }

        return result;
    }

    private ConfigurationCategory? FilterCategoryForSearch(ConfigurationCategory category,
        HashSet<ConfigurationCategory> categoriesToShow)
    {
        if (!categoriesToShow.Contains(category))
        {
            return null;
        }

        var filteredCategory = (ConfigurationCategory) category.Clone();

        foreach (var child in category.Children)
        {
            var filteredChild = FilterCategoryForSearch(child, categoriesToShow);
            if (filteredChild != null)
            {
                filteredChild.Parent = filteredCategory;
                filteredCategory.Children.Add(filteredChild);
            }
        }

        foreach (var setting in category.Settings)
        {
            filteredCategory.Settings.Add(setting);
        }

        return filteredCategory;
    }

    public void SelectCategory(ConfigurationCategory category)
    {
        if (category.IsSelectable)
        {
            CurrentCategory = category;
        }
    }


    [RelayCommand(CanExecute = nameof(SaveCanExecute))]
    private void Save()
    {
        try
        {
            var localization = ServiceLocator.GetRequiredService<ILocalizationService>();
            var languageService = ServiceLocator.GetRequiredService<LanguageService>();
            var languageSetting = _registry.FindSetting("General.Language");
            var requestedLocale = languageSetting is { IsDirty: true, CurrentValue: string locale }
                ? locale
                : null;
            if (requestedLocale != null && !languageService.SupportedCultures.Any(
                    culture => string.Equals(culture.Name, requestedLocale, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ArgumentException($"Unsupported locale: {requestedLocale}");
            }

            _registry.SaveChanges();

            // Apply the log level immediately if it was changed.
            ServiceLocator.GetRequiredService<LogLevelService>().ApplyFromSettings(_registry);

            // Apply the theme immediately (hot-swap, no restart).
            ServiceLocator.GetRequiredService<ThemeService>().ApplyFromSettings(_registry);

            var languageChanged = requestedLocale != null
                                  && languageService.SetLanguageForNextStartup(requestedLocale);
            OnPropertyChanged(nameof(HasUnsavedChanges));

            if (languageChanged)
            {
                var restart = TopmostMessageBox.Show(
                    localization.GetCaption("Language.RestartPrompt", "The display language has been saved. Restart MyTools now to apply it?"),
                    localization.GetCaption("Language.RestartTitle", "Restart required"),
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (restart == MessageBoxResult.Yes)
                {
                    ((MyTools.Desktop.App)Application.Current).Restart();
                }
                return;
            }

            MessageBox.Show(
                localization.GetCaption("Configuration.SaveSuccess", "Settings saved successfully."),
                localization.GetCaption("Success", "Success"),
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            var localization = ServiceLocator.GetRequiredService<ILocalizationService>();
            MessageBox.Show(
                localization.GetCaption("Configuration.SaveFailed", "Failed to save settings: {{message}}", new { message = ex.Message }),
                localization.GetCaption("Error", "Error"),
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private bool SaveCanExecute()
    {
        return true;
    }

    private void OnConfigurationChanged(object? sender, ConfigurationChangedEventArgs e)
    {
        OnPropertyChanged(nameof(HasUnsavedChanges));

        // 如果变更的配置项在当前分类中，刷新显示
        if (CurrentCategory != null && e.Setting.Category == CurrentCategory)
        {
            //UpdateCurrentSettings();
        }
    }
}