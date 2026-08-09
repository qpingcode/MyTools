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
    /// 应用显示语言
    /// </summary>
    Language,

    /// <summary>
    /// 应用主题（白天/黑夜）
    /// </summary>
    Theme,

    /// <summary>
    /// 日志级别
    /// </summary>
    LogLevel,

    /// <summary>
    /// 浮点数值
    /// </summary>
    Double,
    
    /// <summary>
    /// 自定义类型（需要序列化器）
    /// </summary>
    Custom
}


