namespace MyTools.Common.Config.Models;

/// <summary>
/// 配置变更事件参数
/// </summary>
public class ConfigurationChangedEventArgs : EventArgs
{
    /// <summary>
    /// 变更的配置项
    /// </summary>
    public ConfigurationSetting Setting { get; }
    
    /// <summary>
    /// 旧值
    /// </summary>
    public object? OldValue { get; }
    
    /// <summary>
    /// 新值
    /// </summary>
    public object? NewValue { get; }
    
    /// <summary>
    /// 变更时间
    /// </summary>
    public DateTime Timestamp { get; }
    
    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="setting">变更的配置项</param>
    /// <param name="oldValue">旧值</param>
    /// <param name="newValue">新值</param>
    public ConfigurationChangedEventArgs(ConfigurationSetting setting, object? oldValue, object? newValue)
    {
        Setting = setting;
        OldValue = oldValue;
        NewValue = newValue;
        Timestamp = DateTime.UtcNow;
    }
}


