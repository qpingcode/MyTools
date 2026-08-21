using System.Windows;
using System.Windows.Controls;

namespace MyTools.Desktop.Components;

public partial class HotkeyKeycaps : UserControl
{
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command),
            typeof(string),
            typeof(HotkeyKeycaps));

    public HotkeyKeycaps()
    {
        InitializeComponent();
    }

    public string? Command
    {
        get => (string?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
}
