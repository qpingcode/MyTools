namespace MyTools.Common.Config.Enums;

/// <summary>
/// 配置项选项枚举
/// </summary>
[Flags]
public enum SettingOptions
{
    /// <summary>
    /// 无特殊选项
    /// </summary>
    None = 0,
    
    /// <summary>
    /// 隐藏配置
    /// </summary>
    Hidden = 1 << 0,
    
    /// <summary>
    /// 只读
    /// </summary>
    ReadOnly = 1 << 1,
    
    /// <summary>
    /// 高级选项（在高级模式下显示）
    /// </summary>
    Advanced = 1 << 2,
    
    /// <summary>
    /// 需要重启应用
    /// </summary>
    RequiresRestart = 1 << 3,
}


