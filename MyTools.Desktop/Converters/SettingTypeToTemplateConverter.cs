using System.Globalization;
using System.Windows;
using System.Windows.Data;
using MyTools.Common.Config.Enums;

namespace MyTools.Desktop.Converters;

/// <summary>
/// 配置类型到模板转换器
/// </summary>
public class SettingTypeToTemplateConverter : IValueConverter
{
    /// <summary>
    /// 转换配置类型到数据模板
    /// </summary>
    /// <param name="value">配置类型值</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">参数</param>
    /// <param name="culture">文化信息</param>
    /// <returns>数据模板</returns>
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is SettingValueTypes settingType)
        {
            var templateKey = settingType switch
            {
                SettingValueTypes.Bool => "BoolSettingTemplate",
                SettingValueTypes.Integer => "IntegerSettingTemplate",
                SettingValueTypes.String => "StringSettingTemplate",
                SettingValueTypes.Language => "LanguageSettingTemplate",
                SettingValueTypes.Theme => "ThemeSettingTemplate",
                SettingValueTypes.LogLevel => "LogLevelSettingTemplate",
                SettingValueTypes.Double => "DoubleSettingTemplate",
                SettingValueTypes.Custom => "CustomSettingTemplate",
                _ => "StringSettingTemplate"
            };
            
            // 尝试从当前激活的窗口获取资源
            try
            {
                // 获取当前激活的窗口
                var activeWindow = Application.Current?.Windows.OfType<Window>()
                    .FirstOrDefault(w => w.IsActive);
                
                if (activeWindow != null && activeWindow.Resources.Contains(templateKey))
                {
                    return activeWindow.Resources[templateKey];
                }
                
                // 如果找不到激活的窗口，尝试从所有窗口中找到配置窗口
                var configWindow = Application.Current?.Windows.OfType<Window>()
                    .FirstOrDefault(w => w.Title?.Contains("配置") == true || w.Title?.Contains("Configuration") == true);
                
                if (configWindow != null && configWindow.Resources.Contains(templateKey))
                {
                    return configWindow.Resources[templateKey];
                }
            }
            catch
            {
                // 忽略异常，继续尝试其他方法
            }
            
            // 如果parameter是FrameworkElement，尝试从其资源中查找
            if (parameter is FrameworkElement element)
            {
                try
                {
                    return element.FindResource(templateKey);
                }
                catch
                {
                    // 如果找不到资源，返回null，让WPF使用默认模板
                    return null;
                }
            }
        }
        
        return null;
    }
    
    /// <summary>
    /// 反向转换（不支持）
    /// </summary>
    /// <param name="value">值</param>
    /// <param name="targetType">目标类型</param>
    /// <param name="parameter">参数</param>
    /// <param name="culture">文化信息</param>
    /// <returns>转换结果</returns>
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException("SettingTypeToTemplateConverter不支持反向转换");
    }
}

