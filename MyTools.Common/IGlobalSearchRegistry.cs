using MyTools.Plugins;

namespace MyTools.Common;

public interface IGlobalSearchRegistry
{
    void Register(IPlugin puglin);
    IEnumerable<IPlugin> Plugins { get; }
}