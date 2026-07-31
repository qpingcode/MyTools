namespace MyTools.Common.Config.Interfaces;

/// <summary>
/// 配置存储接口
/// </summary>
public interface IConfigurationStorage
{
    /// <summary>
    /// 存储配置值
    /// </summary>
    /// <param name="name">配置项名称</param>
    /// <param name="value">配置值（字节数组）</param>
    void Store(string name, string value);
    
    /// <summary>
    /// 获取配置值
    /// </summary>
    /// <param name="name">配置项名称</param>
    /// <returns>配置值（字节数组），如果不存在返回null</returns>
    string? Retrieve(string name);
    
    /// <summary>
    /// 检查配置项是否存在
    /// </summary>
    /// <param name="name">配置项名称</param>
    /// <returns>是否存在</returns>
    bool Exists(string name);
    
    /// <summary>
    /// 删除配置项
    /// </summary>
    /// <param name="name">配置项名称</param>
    void Delete(string name);
    
    /// <summary>
    /// 清空所有配置
    /// </summary>
    void Clear();
    
    /// <summary>
    /// 获取所有配置项名称
    /// </summary>
    /// <returns>配置项名称集合</returns>
    IEnumerable<string> GetAllNames();
    
    /// <summary>
    /// 初始化存储
    /// </summary>
    void Initialize();
    
    /// <summary>
    /// 关闭存储
    /// </summary>
    void Dispose();
}


