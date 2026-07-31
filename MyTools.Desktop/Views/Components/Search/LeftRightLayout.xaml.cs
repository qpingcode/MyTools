using System.Windows;
using System.Windows.Controls;

namespace MyTools.Desktop.Components;

public partial class LeftRightLayout
{
    private LeftRightLayoutViewModel viewModel => (DataContext as LeftRightLayoutViewModel)!;
    
    public LeftRightLayout()
    {
        InitializeComponent();
    }
}
