using MyTools.Plugins;

namespace MyTools.Common;

public interface IGlobalSearchRegistry
{
    void Register(IPlugin puglin);
    void UnregisterPlugin(IPlugin plugin);
    void Clear();
    IEnumerable<IPlugin> Plugins { get; }
}
