using MyTools.Common;
using MyTools.Plugins.Param;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// Maps Node search-item <c>actions[].kind</c> onto host well-known actions,
/// and runs a plugin-returned <c>hostAction</c> on the host process
/// (so launched programs are not tied to the plugin job object).
/// </summary>
public static class NodePluginWellKnownActions
{
    public static IAction? Resolve(string? kind)
    {
        return Normalize(kind) switch
        {
            "copy" => WellKnownActions.Copy,
            "copyandpaste" => WellKnownActions.CopyAndPaste,
            "execute" => WellKnownActions.Execute,
            "openinexplorer" => WellKnownActions.OpenInExplorer,
            "openinbrowser" => WellKnownActions.OpenInBrowser,
            "openplugin" => WellKnownActions.OpenPlugin,
            "run" => new RunCommandAction(),
            "kill" => new KillProcessAction(),
            _ => null
        };
    }

    public static bool IsWellKnown(string? kind) => Resolve(kind) != null;

    public static IActionParams CreateParams(
        string? kind,
        string? path,
        string? args,
        string? copyText,
        string itemId,
        string title,
        string query)
    {
        var normalized = Normalize(kind);
        if (normalized is "execute")
        {
            return new ExecuteActionParams(
                FirstNonEmpty(path, copyText, title),
                args ?? string.Empty);
        }

        if (normalized is "openinexplorer" or "openinbrowser")
        {
            return ActionStringParam.From(FirstNonEmpty(path, copyText, title));
        }

        if (normalized is "copy" or "copyandpaste")
        {
            return ActionStringParam.From(FirstNonEmpty(copyText, title));
        }

        if (normalized is "kill" or "run")
        {
            return ActionStringParam.From(FirstNonEmpty(copyText, itemId));
        }

        if (normalized is "openplugin")
        {
            return ActionStringParam.From(FirstNonEmpty(path, copyText, itemId));
        }

        return new NodePluginActionArgs(itemId, query);
    }

    public static Task<ActionResult> ExecuteHostActionAsync(string? kind, string path, string? args)
    {
        var effectiveKind = string.IsNullOrWhiteSpace(kind) ? "execute" : kind;
        var action = Resolve(effectiveKind) ?? WellKnownActions.Execute;
        var parameters = CreateParams(effectiveKind, path, args, path, path, path, string.Empty);
        return action.ExecuteAsync(parameters);
    }

    private static string Normalize(string? kind) =>
        (kind ?? string.Empty).Trim().Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();

    private static string FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;
}
