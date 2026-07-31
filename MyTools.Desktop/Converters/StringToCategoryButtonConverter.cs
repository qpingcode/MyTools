using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MyTools.Desktop.Converters;

public class StringToCategoryButtonConverter : IValueConverter, IMultiValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string selectedCategory && parameter is string buttonCategory)
        {
            // 如果选中的分类与按钮分类相同，则禁用按钮（表示已选中）
            return selectedCategory != buttonCategory;
        }
        return true; // 默认启用
    }

    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length >= 2 && values[0] is string selectedCategory && values[1] is string buttonCategory)
        {
            // 如果选中的分类与按钮分类相同，则禁用按钮（表示已选中）
            return selectedCategory != buttonCategory;
        }
        return true; // 默认启用
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
} 