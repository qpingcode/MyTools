using System.Diagnostics;
using System.IO;
using System.Text.Json;
using MyTools.Common;
using MyTools.Plugins.Param;

namespace MyTools.Plugins;

public class RunCommandAction : IAction
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public string Name => "Run";
    public string Description => "Run command";

    public async Task<ActionResult> ExecuteAsync(IActionParams args)
    {
        if (args is not IActionStringParam stringParam)
        {
            return ActionResult.CreateFailure("Invalid parameters for RunCommand action");
        }

        try
        {
            var config = JsonSerializer.Deserialize<CommandSpec>(stringParam.GetValue(), JsonOptions);

            string command;
            string commandArgs;
            var workDirectory = config?.WorkingDirectory
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (config?.IsBashScript == true)
            {
                var tempFilePath = Path.GetTempFileName();
                var tempFilePathWithExtension = Path.ChangeExtension(tempFilePath, ".bat");
                await File.WriteAllLinesAsync(tempFilePathWithExtension, config.Scripts ?? []);

                command = "cmd.exe";
                commandArgs = $"/k \"{tempFilePathWithExtension}\"";
            }
            else
            {
                command = config?.Command ?? string.Empty;
                commandArgs = config?.Args ?? string.Empty;
            }

            if (string.IsNullOrWhiteSpace(command))
            {
                return ActionResult.CreateFailure("Command is empty");
            }

            if (ExplorerShellLauncher.TryLaunch(
                    command,
                    commandArgs,
                    workDirectory,
                    config?.RunAsAdmin == true,
                    out _))
            {
                return ActionResult.CreateSuccess("Command executed");
            }
            Console.WriteLine("[shell-launch] RunCommandAction fallback Process.Start command={0}", command);

            var processStartInfo = new ProcessStartInfo
            {
                FileName = command,
                Arguments = commandArgs,
                UseShellExecute = true,
                Verb = config?.RunAsAdmin == true ? "runas" : null,
                WorkingDirectory = workDirectory
            };

            using var process = new Process();
            process.StartInfo = processStartInfo;
            process.Start();
            Console.WriteLine("[shell-launch] RunCommandAction fallback Process.Start success command={0}", command);

            return ActionResult.CreateSuccess("Command executed");
        }
        catch (Exception ex)
        {
            return ActionResult.CreateFailure($"Failed to execute command: {ex.Message}");
        }
    }

    private sealed class CommandSpec
    {
        public string Command { get; set; } = string.Empty;

        public string Args { get; set; } = string.Empty;

        public bool RunAsAdmin { get; set; }

        public bool IsBashScript { get; set; }

        public List<string>? Scripts { get; set; }

        public string? WorkingDirectory { get; set; }
    }
}
