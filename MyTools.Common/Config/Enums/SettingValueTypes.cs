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
    /// 全局键盘快捷键（由设置页的热键选择器编辑）
    /// </summary>
    HotKey,

    /// <summary>
    /// 日志级别
    /// </summary>
    LogLevel,

    /// <summary>
    /// 浮点数值
    /// </summary>
    Double,

    /// <summary>
    /// JSON 数组（由 plugin.json schema 定义元素结构）
    /// </summary>
    Array,

    /// <summary>
    /// 文件/目录路径（由设置页的路径选择器编辑）
    /// </summary>
    Path,

    /// <summary>
    /// 自定义类型（需要序列化器）
    /// </summary>
    Custom
}


