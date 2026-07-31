using MyTools.Plugins;

namespace MyTools.Common;

public interface IHotKeyRegistry
{
    bool Register(string hotkey, IPlugin plugin);
}