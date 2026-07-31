using System.Diagnostics;
using System.IO;
using MyTools.Common;

namespace MyTools.Plugins;

public class Execute : IAction
{
    public virtual string Name => "Execute";
    public virtual string Description => "Execute a program or script";

    public async Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return ActionResult.CreateFailure("Invalid parameters for Execute action");
        }
        
        var filePath = stringParam.GetValue();
        
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return ActionResult.CreateFailure("File path is empty");
        }

        try
        {
            await ExecuteCoreAsync(filePath, string.Empty, false).ConfigureAwait(false);
            return ActionResult.CreateSuccess($"Executed: {filePath}");
        }
        catch (Exception ex)
        {
            return ActionResult.CreateFailure($"Failed to execute: {ex.Message}");
        }
    }
    
    protected virtual async Task ExecuteCoreAsync(string filePath, string args, bool runAsAdmin)
    {
        var workDir = GetWorkDirectory(filePath);
        var startInfo = GetProcessStartInfo(filePath, runAsAdmin, workDir);

        try
        {
            var process = Process.Start(startInfo);
            process?.Dispose();
        }
        catch(Exception)
        {
            await Task.Run(() => FileLinkOpenHelper.OpenLink(filePath, runAsAdmin));
        }
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
    static ProcessStartInfo GetProcessStartInfo(string filePath, bool runAsAdmin = false, string workDir = "")
    {
        var ext = Path.GetExtension(filePath);
        
        if (ext == ".ps1")
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{filePath}\"",
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
            UseShellExecute = true,
            Verb = runAsAdmin? "runas" : "open",
            WorkingDirectory = workDir
        };
    }
}