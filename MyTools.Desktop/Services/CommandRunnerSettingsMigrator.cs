using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;
using MyTools.Desktop.Models;

namespace MyTools.Desktop.Services;

/// <summary>
/// Copies legacy CommandRunner.json into the command-runner plugin setting in Settings.json.
/// </summary>
public static class CommandRunnerSettingsMigrator
{
    public const string SettingFullPath = "command-runner.Commands";
    public const string LegacyFileName = "CommandRunner.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    public static void Migrate(IConfigurationRegistry registry, ILogger logger, string? legacyFilePath = null)
    {
        var setting = registry.FindSetting(SettingFullPath);
        if (setting == null)
        {
            return;
        }

        var path = legacyFilePath ?? Path.Combine(ConfigPath.Base, LegacyFileName);
        if (!File.Exists(path))
        {
            return;
        }

        if (HasStoredCommands(setting))
        {
            logger.LogInformation(
                "Skipping CommandRunner.json migration because {Setting} already has values.",
                SettingFullPath);
            return;
        }

        List<CommandConfig> commands;
        try
        {
            commands = ParseLegacy(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to parse legacy {File}; leaving it in place.", path);
            return;
        }

        setting.CurrentValue = ToStoredElement(commands);
        registry.SaveChanges();
        ArchiveLegacyFile(path, logger);
        logger.LogInformation(
            "Migrated {Count} commands from {File} to {Setting}.",
            commands.Count,
            path,
            SettingFullPath);
    }

    public static List<CommandConfig> ParseLegacy(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<CommandConfig>>(json, JsonOptions) ?? [];
        }
        catch (JsonException)
        {
            var stripped = Regex.Replace(json, @",(\s*[}\]])", "$1");
            return JsonSerializer.Deserialize<List<CommandConfig>>(stripped, JsonOptions) ?? [];
        }
    }

    public static JsonElement ToStoredElement(IReadOnlyList<CommandConfig> commands)
    {
        var rows = commands.Select(command => new StoredCommand
        {
            Name = command.Name ?? "",
            Command = command.Command ?? "",
            Args = command.Args ?? "",
            RunAsAdmin = command.RunAsAdmin,
            IsBashScript = command.IsBashScript,
            Scripts = JoinScripts(command.Scripts),
            WorkingDirectory = command.WorkingDirectory ?? ""
        }).ToList();
        return JsonSerializer.SerializeToElement(rows, JsonOptions);
    }

    public static string JoinScripts(IEnumerable<string>? scripts)
    {
        if (scripts == null)
        {
            return "";
        }

        return string.Join("\n", scripts.Select(line => line?.TrimEnd() ?? "").Where(line => line.Length > 0));
    }

    private static bool HasStoredCommands(MyTools.Common.Config.Models.ConfigurationSetting setting)
    {
        if (setting.CurrentValue is JsonElement json && json.ValueKind == JsonValueKind.Array)
        {
            return json.GetArrayLength() > 0;
        }

        return false;
    }

    private static void ArchiveLegacyFile(string path, ILogger logger)
    {
        try
        {
            var bak = path + ".bak";
            File.Move(path, bak, overwrite: true);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Migrated commands but failed to rename {File}.", path);
        }
    }

    private sealed class StoredCommand
    {
        public string Name { get; set; } = "";
        public string Command { get; set; } = "";
        public string Args { get; set; } = "";
        public bool RunAsAdmin { get; set; }
        public bool IsBashScript { get; set; }
        public string Scripts { get; set; } = "";
        public string WorkingDirectory { get; set; } = "";
    }
}
