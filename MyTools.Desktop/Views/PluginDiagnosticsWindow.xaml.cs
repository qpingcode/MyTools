using MyTools.Desktop.ViewModels;

namespace MyTools.Desktop.Views;

public partial class PluginDiagnosticsWindow
{
    public PluginDiagnosticsWindow(PluginDiagnosticsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Closed += (_, _) => viewModel.Dispose();
    }
}
