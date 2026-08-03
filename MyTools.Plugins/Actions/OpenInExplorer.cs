using System.Diagnostics;
using System.IO;
using MyTools.Common;
using MyTools.Common.Localization;

namespace MyTools.Plugins;

public class OpenInExplorer : IAction
{
    public string Name => ActionText.Get("Action.OpenInExplorer.Name", "Open in Explorer");
    public string Description => ActionText.Get(
        "Action.OpenInExplorer.Description", "Open the directory or locate the file in Windows Explorer");

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenInExplorer.InvalidParameters", "Invalid parameters for Open in Explorer action")));
        }

        try
        {
            var path = stringParam.GetValue();
            
            if (string.IsNullOrEmpty(path))
            {
                return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                    "Action.OpenInExplorer.EmptyPath", "Path cannot be empty")));
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = Path.IsPathRooted(path) ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            return Task.FromResult(ActionResult.CreateSuccess(new LocalizedMessage(
                "Action.OpenInExplorer.Success", "Opened in Explorer: {{path}}", new { path })));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenInExplorer.Failed", "Failed to open in Explorer: {{message}}", new { message = ex.Message })));
        }
    }
} 