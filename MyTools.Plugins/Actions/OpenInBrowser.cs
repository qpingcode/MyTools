using System.Diagnostics;
using System.IO;
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
    
    private static void OpenBrowser(string url)
    {
        using var process = Process.Start(CreateProcessStartInfo(
            url,
            Environment.GetFolderPath,
            File.Exists));
    }

    internal static ProcessStartInfo CreateProcessStartInfo(
        string url,
        Func<Environment.SpecialFolder, string> getFolderPath,
        Func<string, bool> fileExists)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            !uri.Scheme.Equals("chrome-extension", StringComparison.OrdinalIgnoreCase))
        {
            return new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
        }

        var chromePath = new[]
            {
                Environment.SpecialFolder.LocalApplicationData,
                Environment.SpecialFolder.ProgramFiles,
                Environment.SpecialFolder.ProgramFilesX86
            }
            .Select(getFolderPath)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.Combine(path, "Google", "Chrome", "Application", "chrome.exe"))
            .FirstOrDefault(fileExists)
            ?? throw new FileNotFoundException(
                "Google Chrome was not found. A chrome-extension link must be opened by Chrome.");

        var startInfo = new ProcessStartInfo
        {
            FileName = chromePath,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(url);
        return startInfo;
    }

    private string[] SplitUrls(string args)
    {
        return args.Split(SplitStr);
    }
}