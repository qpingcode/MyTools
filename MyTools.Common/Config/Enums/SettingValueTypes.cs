namespace MyTools.Common.Config.Enums;

/// <summary>
/// 配置项类型枚举
/// </summary>
public enum SettingValueTypes
{
    /// <summary>
    /// 布尔值
    /// </summary>
    Bool,
    
    /// <summary>
    /// 整数值
    /// </summary>
    Integer,
    
    /// <summary>
    /// 字符串值
    /// </summary>
    String,
    
    /// <summary>
    /// 浮点数值
    /// </summary>
    Double,
    
    /// <summary>
    /// 自定义类型（需要序列化器）
    /// </summary>
    Custom
}


