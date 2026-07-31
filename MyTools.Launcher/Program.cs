using System.Diagnostics;

// MyTools.Launcher — 中介进程，用于打破提权子进程的父子链条。
// 调用方式: MyTools.Launcher.exe "<filePath>" ["<workDir>"]
// 本进程以普通权限启动，再通过 runas verb 启动目标，之后立即退出。
// 由于目标进程是由本进程（而非 MyTools 主进程）以 ShellExecute/runas 启动，
// UAC 会让目标进程脱离 MyTools 的 Job Object，从而在 MyTools 退出后继续存活。

var args = Environment.GetCommandLineArgs();
if (args.Length < 2)
    return;

var filePath = args[1];
var workDir = args.Length > 2 ? args[2] : (Path.GetDirectoryName(filePath) ?? string.Empty);
var ext = Path.GetExtension(filePath).ToLowerInvariant();

ProcessStartInfo startInfo;
if (ext == ".ps1")
{
    startInfo = new ProcessStartInfo
    {
        FileName = "powershell.exe",
        Arguments = $"-ExecutionPolicy Bypass -File \"{filePath}\"",
        UseShellExecute = true,
        Verb = "runas",
        WorkingDirectory = workDir,
    };
}
else
{
    startInfo = new ProcessStartInfo
    {
        FileName = filePath,
        UseShellExecute = true,
        Verb = "runas",
        WorkingDirectory = workDir,
    };
}

try
{
    var process = Process.Start(startInfo);
    process?.Dispose();
}
catch (Exception ex)
{
    Console.Error.WriteLine($"MyTools.Launcher: failed to start process: {ex.Message}");
}
