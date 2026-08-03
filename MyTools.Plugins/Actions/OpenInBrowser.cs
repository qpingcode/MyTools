using System.Diagnostics;
using MyTools.Common;
using MyTools.Common.Localization;

namespace MyTools.Plugins;

public class OpenInBrowser : IAction
{
    public static string SplitStr = ",";
    public string Name => ActionText.Get("Action.OpenInBrowser.Name", "Open in Browser");
    public string Description => ActionText.Get("Action.OpenInBrowser.Description", "Open URL in the default browser");
    public Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenInBrowser.InvalidParameters", "Invalid parameters for Open in Browser action")));
        }
        
        try
        {
            var urls = SplitUrls(stringParam.GetValue());
            foreach (var url in urls)
            {
                OpenBrowser(url);
            }
            return Task.FromResult(ActionResult.CreateSuccess(new LocalizedMessage(
                "Action.OpenInBrowser.Success", "Opened in browser")));
        }
        catch (Exception ex)
        {
            return Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "Action.OpenInBrowser.Failed", "Failed to open in browser: {{message}}", new { message = ex.Message })));
        }
    }
    
    private void OpenBrowser(string url)
    {
        var processStartInfo = new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        };

        using var process = new Process();
        process.StartInfo = processStartInfo;
        process.Start();
    }

    private string[] SplitUrls(string args)
    {
        return args.Split(SplitStr);
    }
}