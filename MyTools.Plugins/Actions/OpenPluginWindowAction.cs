using MyTools.Common;
using MyTools.Common.DependencyInjection;
using MyTools.Common.Localization;
using MyTools.Plugins.Param;

namespace MyTools.Plugins;

public sealed class OpenPluginWindowAction : IAction
{
    private readonly IPluginLauncher? launcher;

    public OpenPluginWindowAction()
    {
    }

    public OpenPluginWindowAction(IPluginLauncher launcher)
    {
        this.launcher = launcher;
    }

    public string Name => ActionText.Get("Action.OpenPlugin.Name", "Open Plugin");
    public string Description => ActionText.Get("Action.OpenPlugin.Description", "Open the plugin window");

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenPlugin.InvalidParameters", "Invalid parameters for Open Plugin action")));
        }

        var pluginId = stringParam.GetValue().Trim();
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenPlugin.EmptyId", "Plugin id is empty")));
        }

        try
        {
            var resolved = launcher ?? ServiceLocator.GetRequiredService<IPluginLauncher>();
            var kind = resolved.Open(pluginId);
            if (kind == PluginLaunchKind.NotFound)
            {
                return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                    "Action.OpenPlugin.NotFound",
                    "Plugin not found: {{pluginId}}",
                    new { pluginId })));
            }

            var actionType = kind == PluginLaunchKind.PluginWindow
                ? ActionTypeEnum.Close
                : ActionTypeEnum.None;
            return Task.FromResult(ActionResult.CreateSuccess(new LocalizedMessage(
                "Action.OpenPlugin.Success",
                "Opened {{pluginId}}",
                new { pluginId }), actionType));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenPlugin.Failed",
                "Failed to open plugin: {{message}}",
                new { message = ex.Message })));
        }
    }
}
