using System.Diagnostics;
using System.IO;
using MyTools.Common;
using MyTools.Common.Config;
using MyTools.Common.Plugins;
using MyTools.Common.Utils;
using MyTools.Plugins.Param;
using Newtonsoft.Json;

namespace MyTools.Plugins;

public class CommandRunner : PluginBase
{
    public override string PluginId => "CommandRunner";
    private List<CommandConfig> _commands = new();

    public override string Name => GetCaption("Plugin.CommandRunner.Name", "Command Runner");
    public override string Description => GetCaption("Plugin.CommandRunner.Description", "Run predefined commands");
    public override List<IActionWithCommand> Actions => [new RunCommandAction().WithDefaultCommand()];

    private Icon defaultIcon = new StringIcon("🚀");
    public override bool IsGlobalSearchPlugin => true;
    
    public override async Task InitializeAsync()
    {
        var configPath = Path.Combine(ConfigPath.Base, "CommandRunner.json");
        if (File.Exists(configPath))
        {
            var json = await File.ReadAllTextAsync(configPath);
            _commands = JsonConvert.DeserializeObject<List<CommandConfig>>(json) ?? new List<CommandConfig>();
        }
        
        var cmdPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "cmd.exe");
        var cmdPath64 = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "SysWOW64", "cmd.exe");
        SetDefaultIconIfPossible(cmdPath);
        SetDefaultIconIfPossible(cmdPath64);
    }

    private void SetDefaultIconIfPossible(string path)
    {
        if (File.Exists(path))
        {
            var imageData = FileIconHelper.GetFileIconData(path);
            if (imageData != null)
            {
                defaultIcon = new ImageIcon(imageData);
            }
        }
    }
   
    public override Task<Result> SearchAsync(string query, CancellationToken cancellationToken, SearchOptions? searchOptions = null)
    {
        var items = _commands
            .Where(c => MatchName(c, query))
            .Select(c => new ResultItem(defaultIcon, c.Name, CreateSubTitle(c), CreateArgs(c), ResultItemPriorities.Medium));
        var result = Result.CreateSuccessResult(items);
        return Task.FromResult(result);
    }

    private bool MatchName(CommandConfig config, string query)
    {
        if (config.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        if (StringUtils.IsSubsequence(query.ToLower(), config.Name.ToLower()))
        {
            return true;
        }
        return false;
    }

    private string CreateSubTitle(CommandConfig config)
    {
        if (config.IsBashScript)
        {
            if (config.Scripts != null)
            {
                return string.Join(" && ", config.Scripts);
            }
            return string.Empty;
        }
        return $"{config.Command} {config.Args}";
    }

    private IActionStringParam CreateArgs(CommandConfig config)
    {
        return ActionStringParam.From(JsonConvert.SerializeObject(config));
    }
}

public class RunCommandAction : IAction
{
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
            var config = JsonConvert.DeserializeObject<CommandConfig>(stringParam.GetValue());
            
            ProcessStartInfo processStartInfo;
            var workDirectory = config?.WorkingDirectory ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (config?.IsBashScript == true)
            {
                var tempFilePath = Path.GetTempFileName();
                var tempFilePathWithExtension = Path.ChangeExtension(tempFilePath, ".bat");
                await File.WriteAllLinesAsync(tempFilePathWithExtension, config.Scripts!);
                
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

            return ActionResult.CreateSuccess($"Command executed");
        }
        catch (Exception ex)
        {
            return ActionResult.CreateFailure($"Failed to execute command: {ex.Message}");
        }
    }
} 