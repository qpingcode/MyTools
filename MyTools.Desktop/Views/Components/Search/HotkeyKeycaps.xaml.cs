using System.Windows;
using System.Windows.Controls;
using MyTools.Common;

namespace MyTools.Desktop.Components;

public partial class HotkeyKeycaps : UserControl
{
    public static readonly DependencyProperty HotkeyProperty =
        DependencyProperty.Register(
            nameof(Hotkey),
            typeof(Hotkey),
            typeof(HotkeyKeycaps));

    public HotkeyKeycaps()
    {
        InitializeComponent();
    }

    public Hotkey Hotkey
    {
        get => (Hotkey)GetValue(HotkeyProperty);
        set => SetValue(HotkeyProperty, value);
    }
}
