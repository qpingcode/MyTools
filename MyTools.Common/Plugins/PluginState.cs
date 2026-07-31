using MyTools.Common.Plugins;

namespace MyTools.Common.Model.Plugins;

public class PluginState : IPluginState
{
    public bool IsEnabled { get; set; } = true;
}