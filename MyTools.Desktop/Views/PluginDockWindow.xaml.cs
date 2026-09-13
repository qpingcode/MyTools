using System.Windows;
using System.Windows.Input;

namespace MyTools.Desktop.Views;

public partial class PluginDockWindow
{
    public PluginDockWindow()
    {
        InitializeComponent();
    }

    private void DragHandle_OnPreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left)
        {
            return;
        }

        e.Handled = true;
        DragMove();
    }
}
