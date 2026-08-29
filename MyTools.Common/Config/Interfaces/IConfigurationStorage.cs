using MyTools.Common.Plugins;

namespace MyTools.Common.Config.Interfaces;

/// <summary>
/// 配置存储接口。宿主设置 <paramref name="pluginId"/> 为 null；
/// 插件设置必须传入所属 <see cref="PluginId"/>，由实现路由到该插件的数据目录。
/// </summary>
public interface IConfigurationStorage
{
    void Store(string name, string value, PluginId? pluginId = null);

    string? Retrieve(string name, PluginId? pluginId = null);

    bool Exists(string name, PluginId? pluginId = null);

    void Delete(string name, PluginId? pluginId = null);

    void Clear();

    IEnumerable<string> GetAllNames(PluginId? pluginId = null);

    void Initialize();

    void Dispose();
}
