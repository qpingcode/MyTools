using System.Windows;
using System.Windows.Controls;
using MyTools.Plugins;

namespace MyTools.Desktop.Converters;


public class IconTemplateSelector : DataTemplateSelector
{
    public DataTemplate StringIconTemplate { get; set; } = null!;
    public DataTemplate ImageIconTemplate { get; set; } = null!;
    public DataTemplate MdiIconTemplate { get; set; } = null!;

    public override DataTemplate SelectTemplate(object? item, DependencyObject container)
    {
        if (item is MdiIcon)
        {
            return MdiIconTemplate;
        }

        if (item is StringIcon)
        {
            return StringIconTemplate;
        }

        if (item is ImageIcon)
        {
            return ImageIconTemplate;
        }

        return base.SelectTemplate(item, container) ?? throw new InvalidOperationException();
    }
}
