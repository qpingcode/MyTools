using System.Diagnostics;
using System.IO;
using MyTools.Common;
using MyTools.Common.Localization;

namespace MyTools.Plugins;

/// <summary>Opens a file through the Windows shell using its registered default application.</summary>
public sealed class OpenFile : IAction
{
    public string Name => ActionText.Get("Action.OpenFile.Name", "Open");
    public string Description => ActionText.Get(
        "Action.OpenFile.Description", "Open the file with its default application");

    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenFile.InvalidParameters", "Invalid parameters for Open action")));
        }

        var path = stringParam.GetValue();
        if (string.IsNullOrWhiteSpace(path))
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenFile.EmptyPath", "File path is empty")));
        }

        try
        {
            using var process = Process.Start(CreateStartInfo(path));
            return Task.FromResult(ActionResult.CreateSuccess(new LocalizedMessage(
                "Action.OpenFile.Success", "Opened: {{path}}", new { path })));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenFile.Failed", "Failed to open: {{message}}", new { message = ex.Message })));
        }
    }

    internal static ProcessStartInfo CreateStartInfo(string path) => new()
    {
        FileName = path,
        UseShellExecute = true,
        Verb = "open",
        WorkingDirectory = Path.GetDirectoryName(path) ?? string.Empty
    };
}
