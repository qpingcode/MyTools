using System.Diagnostics;
using System.IO;
using MyTools.Common;
using MyTools.Common.Localization;
using MyTools.Plugins.Param;

namespace MyTools.Plugins;

public class Execute : IAction
{
    public virtual string Name => ActionText.Get("Action.Execute.Name", "Execute");
    public virtual string Description => ActionText.Get("Action.Execute.Description", "Execute a program or script");

    public async Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return ActionResult.CreateFailure(new LocalizedMessage(
                "Action.Execute.InvalidParameters", "Invalid parameters for Execute action"));
        }
        
        var filePath = stringParam.GetValue();
        
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ActionResult.CreateFailure(new LocalizedMessage(
                "Action.Execute.EmptyPath", "File path is empty"));
        }

        try
        {
            var extraArgs = args is ExecuteActionParams executeParams
                ? executeParams.Arguments
                : string.Empty;
            await ExecuteCoreAsync(filePath, extraArgs, false).ConfigureAwait(false);
            return ActionResult.CreateSuccess(new LocalizedMessage(
                "Action.Execute.Success", "Executed: {{path}}", new { path = filePath }));
        }
        catch (Exception ex)
        {
            return ActionResult.CreateFailure(new LocalizedMessage(
                "Action.Execute.Failed", "Failed to execute: {{message}}", new { message = ex.Message }));
        }
    }
    
    protected virtual async Task ExecuteCoreAsync(string filePath, string args, bool runAsAdmin)
    {
        var workDir = GetWorkDirectory(filePath);
        if (TryShellLaunch(filePath, args, runAsAdmin, workDir))
        {
            return;
        }
        Console.WriteLine("[shell-launch] fallback Process.Start file={0} runAsAdmin={1}", filePath, runAsAdmin);

        var startInfo = GetProcessStartInfo(filePath, runAsAdmin, workDir, args);

        try
        {
            var process = Process.Start(startInfo);
            process?.Dispose();
            Console.WriteLine("[shell-launch] fallback Process.Start success file={0}", filePath);
        }
        catch(Exception)
        {
            Console.WriteLine("[shell-launch] fallback Process.Start failed, trying FileLinkOpenHelper file={0}", filePath);
            await Task.Run(() => FileLinkOpenHelper.OpenLink(filePath, runAsAdmin));
            Console.WriteLine("[shell-launch] FileLinkOpenHelper success file={0}", filePath);
        }
    }

    private static bool TryShellLaunch(string filePath, string args, bool runAsAdmin, string workDir)
    {
        if (Path.GetExtension(filePath).Equals(".ps1", StringComparison.CurrentCultureIgnoreCase))
        {
            var powershellArgs = $"-ExecutionPolicy Bypass -File \"{filePath}\"";
            if (!string.IsNullOrWhiteSpace(args))
            {
                powershellArgs += " " + args;
            }

            return ExplorerShellLauncher.TryLaunch("powershell.exe", powershellArgs, workDir, runAsAdmin, out _);
        }

        return ExplorerShellLauncher.TryLaunch(filePath, args, workDir, runAsAdmin, out _);
    }
    
    static string GetWorkDirectory(string filePath)
    {
        var fileInfo = new FileInfo(filePath);
        if (fileInfo.Extension.Equals(".lnk", StringComparison.CurrentCultureIgnoreCase))
        {
            var targetDir = LnkParser.GetTargetDirectory(filePath);
            if (!string.IsNullOrEmpty(targetDir) && Directory.Exists(targetDir))
            {
                return targetDir;
            }
        }

        return Path.GetDirectoryName(filePath) ?? string.Empty;
    }

    /**
     * 因为 Windows 安全模型限制：
     *  runas 必须通过 Shell（UseShellExecute = true）
     *  UseShellExecute = true 创建的是子进程
     *  你无法在不持有句柄的情况下“启动并立即忘记”一个提权进程（安全限制）
     *
     * 所以必须通过中介来打破这个链。
     *
     * TODO 使用中介打破链条
     *
     *
     * NOTE:
     * 1. rider debug模式下, 不会出现Mytools关闭导致子进程关闭的问题, 但是普通模式下会出现
     */
    static ProcessStartInfo GetProcessStartInfo(string filePath, bool runAsAdmin = false, string workDir = "", string args = "")
    {
        var ext = Path.GetExtension(filePath);
        
        if (ext == ".ps1")
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = string.IsNullOrWhiteSpace(args)
                    ? $"-ExecutionPolicy Bypass -File \"{filePath}\""
                    : $"-ExecutionPolicy Bypass -File \"{filePath}\" {args}",
                UseShellExecute = true,
                Verb = runAsAdmin ? "runas" : "open",
                CreateNoWindow = false,
                WorkingDirectory = workDir,
            };
            return startInfo;
        }

        return new ProcessStartInfo
        {
            FileName = filePath,
            Arguments = args,
            UseShellExecute = true,
            Verb = runAsAdmin? "runas" : "open",
            WorkingDirectory = workDir
        };
    }
}