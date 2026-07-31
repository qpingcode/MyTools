using System.Diagnostics;
using System.IO;
using MyTools.Common;

namespace MyTools.Plugins;

public class OpenInExplorer : IAction
{
    public string Name => "Open in Explorer";
    public string Description => "Open the directory or locate the file in Windows Explorer";

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return Task.FromResult(ActionResult.CreateFailure("Invalid parameters for OpenInExplorer action"));
        }

        try
        {
            var path = stringParam.GetValue();
            
            if (string.IsNullOrEmpty(path))
            {
                return Task.FromResult(ActionResult.CreateFailure("Path cannot be empty"));
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = Path.IsPathRooted(path) ? $"/select,\"{path}\"" : $"\"{path}\"",
                UseShellExecute = true
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();

            return Task.FromResult(ActionResult.CreateSuccess($"Opened in Explorer: {path}"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.CreateFailure($"Failed to open in Explorer: {ex.Message}"));
        }
    }
} 