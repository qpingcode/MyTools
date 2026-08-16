using System.Windows.Input;
using MyTools.Desktop.Models;

namespace MyTools.Desktop.Services;

public sealed class HotKeyInspectionRequest
{
    public string? SearchHotKey { get; init; }
    public string? SearchHotKeyDisplayName { get; init; }
    public bool ExcludeSearchHotKey { get; init; }
    public string? ExcludePluginId { get; init; }
    public IReadOnlyDictionary<string, string?> PluginHotKeys { get; init; }
        = new Dictionary<string, string?>();
    public IReadOnlyDictionary<string, string> PluginNames { get; init; }
        = new Dictionary<string, string>();
}

public sealed class HotKeyInspection
{
    public string? ConflictWith { get; init; }
    public bool Reserved { get; init; }
}

/// <summary>
/// Common OS/app editing shortcuts that work poorly as global hotkeys.
/// </summary>
public static class ReservedHotKeys
{
    private static readonly HashSet<string> Values = new(StringComparer.OrdinalIgnoreCase)
    {
        "Ctrl+C", "Ctrl+V", "Ctrl+X", "Ctrl+A", "Ctrl+Z", "Ctrl+Y",
        "Ctrl+S", "Ctrl+O", "Ctrl+N", "Ctrl+P", "Ctrl+F", "Ctrl+W",
        "Ctrl+Insert", "Shift+Insert", "Shift+Delete",
        "Alt+F4", "Alt+Tab", "Ctrl+Esc"
    };

    public static bool IsReserved(string? hotKey)
    {
        var normalized = Normalize(hotKey);
        return normalized != null && Values.Contains(normalized);
    }

    public static string? Normalize(string? hotKey)
    {
        if (string.IsNullOrWhiteSpace(hotKey))
        {
            return null;
        }

        var parsed = new HotKeyConfig(hotKey);
        return parsed.Key == Key.None ? hotKey.Trim() : parsed.ToString();
    }
}

public static class HotKeyInspector
{
    public static HotKeyInspection Inspect(string? hotKey, HotKeyInspectionRequest request)
    {
        var normalized = ReservedHotKeys.Normalize(hotKey);
        if (normalized == null)
        {
            return new HotKeyInspection();
        }

        string? conflictWith = null;
        if (!request.ExcludeSearchHotKey
            && HotKeysEqual(normalized, request.SearchHotKey))
        {
            conflictWith = string.IsNullOrWhiteSpace(request.SearchHotKeyDisplayName)
                ? "Search hotkey"
                : request.SearchHotKeyDisplayName;
        }
        else
        {
            foreach (var (pluginId, pluginHotKey) in request.PluginHotKeys)
            {
                if (string.Equals(pluginId, request.ExcludePluginId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (!HotKeysEqual(normalized, pluginHotKey))
                {
                    continue;
                }

                conflictWith = request.PluginNames.GetValueOrDefault(pluginId, pluginId);
                break;
            }
        }

        return new HotKeyInspection
        {
            ConflictWith = conflictWith,
            Reserved = ReservedHotKeys.IsReserved(normalized)
        };
    }

    private static bool HotKeysEqual(string normalized, string? other)
    {
        var otherNormalized = ReservedHotKeys.Normalize(other);
        return otherNormalized != null
               && string.Equals(normalized, otherNormalized, StringComparison.OrdinalIgnoreCase);
    }
}
