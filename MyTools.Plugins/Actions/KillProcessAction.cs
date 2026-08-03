using System.Diagnostics;
using MyTools.Common;
using MyTools.Common.Localization;

namespace MyTools.Plugins;

public class KillProcessAction : IAction
{
    public string Name => ActionText.Get("Action.KillProcess.Name", "Kill Process");
    public string Description => ActionText.Get("Action.KillProcess.Description", "Terminate the selected process");

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.KillProcess.InvalidParameters", "Invalid parameters for Kill Process action")));
        }
        
        try
        {
            var processId = int.Parse(stringParam.GetValue());
            var process = Process.GetProcessById(processId);
            process.Kill();
            return Task.FromResult(ActionResult.CreateSuccess(new LocalizedMessage(
                "Action.KillProcess.Success",
                "Process {{processName}} (PID: {{processId}}) terminated successfully",
                new { processName = process.ProcessName, processId })));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.KillProcess.Failed", "Failed to kill process: {{message}}", new { message = ex.Message })));
        }
    }
} 