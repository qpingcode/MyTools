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
            var config = JsonSerializer.Deserialize<CommandConfig>(stringParam.GetValue(), JsonOptions);

            ProcessStartInfo processStartInfo;
            var workDirectory = config?.WorkingDirectory
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (config?.IsBashScript == true)
            {
                var tempFilePath = Path.GetTempFileName();
                var tempFilePathWithExtension = Path.ChangeExtension(tempFilePath, ".bat");
                await File.WriteAllLinesAsync(tempFilePathWithExtension, config.Scripts ?? []);

                processStartInfo = new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/k \"{tempFilePathWithExtension}\"",
                    UseShellExecute = true,
                    Verb = config.RunAsAdmin ? "runas" : null,
                    WorkingDirectory = workDirectory
                };
            }
            else
            {
                processStartInfo = new ProcessStartInfo
                {
                    FileName = config?.Command,
                    Arguments = config?.Args,
                    UseShellExecute = true,
                    Verb = config?.RunAsAdmin == true ? "runas" : null,
                    WorkingDirectory = workDirectory
                };
            }

            using var process = new Process();
            process.StartInfo = processStartInfo;
            process.Start();

            return ActionResult.CreateSuccess("Command executed");
        }
        catch (Exception ex)
        {
            return ActionResult.CreateFailure($"Failed to execute command: {ex.Message}");
        }
    }
}
