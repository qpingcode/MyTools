using System.Diagnostics;
using MyTools.Common;

namespace MyTools.Plugins;

public class KillProcessAction : IAction
{
    public string Name => "Kill Process";
    public string Description => "Terminate the selected process";

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return Task.FromResult(ActionResult.CreateFailure("Invalid parameters for Kill Process action"));
        }
        
        try
        {
            var processId = int.Parse(stringParam.GetValue());
            var process = Process.GetProcessById(processId);
            process.Kill();
            return Task.FromResult(ActionResult.CreateSuccess($"Process {process.ProcessName} (PID: {processId}) terminated successfully"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.CreateFailure($"Failed to kill process: {ex.Message}"));
        }
    }
} 