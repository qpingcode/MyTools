namespace MyTools.Desktop.Models;

/// <summary>
/// 用户配置的自定义命令。历史数据来自 CommandRunner.json，现已迁入 Settings.json 的 command-runner.Commands。
/// </summary>
public sealed class CommandConfig
{
    public string Name { get; set; } = string.Empty;

    public string Command { get; set; } = string.Empty;

    public string Args { get; set; } = string.Empty;

    public bool RunAsAdmin { get; set; }

    public bool IsBashScript { get; set; }

    public List<string>? Scripts { get; set; }

    public string? WorkingDirectory { get; set; }
}
