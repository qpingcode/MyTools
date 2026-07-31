using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MyTools.Desktop.ViewModels;
using MyTools.Common.Config.Models;

namespace MyTools.Desktop.Views;

/// <summary>
/// ConfigurationWindow.xaml 的交互逻辑
/// </summary>
public partial class ConfigurationWindow : Window
{
    private readonly ConfigurationViewModel _viewModel;
    
    /// <summary>
    /// 构造函数
    /// </summary>
    public ConfigurationWindow()
    {
        InitializeComponent();
        _viewModel = new ConfigurationViewModel();
        DataContext = _viewModel;
        
        // 窗口关闭时保存配置
        Closing += ConfigurationWindow_Closing;
    }
    
    /// <summary>
    /// 搜索框文本变更事件
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">事件参数</param>
    private void SearchTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _viewModel.PerformSearch();
    }
    
    /// <summary>
    /// 搜索框按键事件
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">事件参数</param>
    private void SearchTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SearchTextBox.Clear();
            SearchTextBox.Focus();
        }
    }
    
    /// <summary>
    /// 分类树选择变更事件
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">事件参数</param>
    private void CategoryTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is ConfigurationCategory category)
        {
            _viewModel.SelectCategory(category);
        }
    }
    
    /// <summary>
    /// 窗口关闭事件
    /// </summary>
    /// <param name="sender">事件源</param>
    /// <param name="e">事件参数</param>
    private void ConfigurationWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // 检查是否有未保存的配置变更
        if (_viewModel.HasUnsavedChanges)
        {
            var result = MessageBox.Show(
                "有未保存的配置变更，是否保存？",
                "保存配置",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            
            switch (result)
            {
                case MessageBoxResult.Yes:
                    _viewModel.SaveCommand.Execute(null);
                    break;
                case MessageBoxResult.Cancel:
                    e.Cancel = true;
                    return;
                case MessageBoxResult.No:
                    // 不保存，直接关闭
                    break;
            }
        }
    }
}


