using Serilog.Core;
using Serilog.Events;
using MyTools.Common.Config.Interfaces;

namespace MyTools.Desktop.Services;

/// <summary>
/// Owns the Serilog <see cref="LoggingLevelSwitch"/> and applies the user-configured
/// minimum log level (<c>General.LogLevel</c>) at runtime, without restarting.
/// </summary>
public sealed class LogLevelService
{
    public const string LogLevelSettingPath = "General.LogLevel";
    private const LogEventLevel DefaultLevel = LogEventLevel.Debug;

    public LoggingLevelSwitch LevelSwitch { get; } = new(DefaultLevel);

    /// <summary>
    /// Reads the configured log level from the registry and applies it to the switch.
    /// Falls back to <see cref="DefaultLevel"/> when the setting is missing or invalid.
    /// </summary>
    public void ApplyFromSettings(IConfigurationRegistry registry)
    {
        var stored = registry.FindSetting(LogLevelSettingPath)?.GetValue<string>();
        LevelSwitch.MinimumLevel = TryParseLevel(stored, out var parsed) ? parsed : DefaultLevel;
    }

    private static bool TryParseLevel(string? value, out LogEventLevel level)
    {
        if (!string.IsNullOrWhiteSpace(value)
            && Enum.TryParse(value, ignoreCase: true, out level))
        {
            return true;
        }

        level = DefaultLevel;
        return false;
    }
}
