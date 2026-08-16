namespace MyTools.Desktop.Models;

/// <summary>
/// 用户配置的自定义命令，持久化到 CommandRunner.json。
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
