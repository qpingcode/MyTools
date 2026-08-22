using System.Text.Json;
using MyTools.Common;
using MyTools.Common.Localization;
using MyTools.Common.DependencyInjection;
using MyTools.Plugins.Param;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// 执行插件返回的 <c>host</c> outcome。每个 kind 只读它自己声明的字段——
/// 参数由 Node 侧的 action 现场构造，宿主不再从 item 上做跨字段回退猜测。
/// 在宿主进程里启动程序，使其不受插件 job object 牵连。
/// </summary>
public static class NodePluginWellKnownActions
{
    internal static Task<ActionResult> ExecuteAsync(NodePluginHostActionDto host)
    {
        var kind = Normalize(host.Kind);
        return kind switch
        {
            "copy" => WellKnownActions.Copy.ExecuteAsync(ActionStringParam.From(host.Text ?? string.Empty)),
            "copyandpaste" => WellKnownActions.CopyAndPaste.ExecuteAsync(
                ActionStringParam.From(host.Text ?? string.Empty)),
            "addclipboardhistory" when host.Texts.Count == 0 => InvalidPayload(host.Kind, "texts"),
            "addclipboardhistory" => ServiceLocator.GetRequiredService<ClipBoardPlugin>()
                .AddTextHistoryAsync(host.Texts),
            "execute" when string.IsNullOrWhiteSpace(host.Path) => InvalidPayload(host.Kind, "path"),
            "execute" => (host.RunAsAdmin ? WellKnownActions.AdminExecute : WellKnownActions.Execute)
                .ExecuteAsync(new ExecuteActionParams(host.Path!, host.Args ?? string.Empty)),
            "openinexplorer" when string.IsNullOrWhiteSpace(host.Path) => InvalidPayload(host.Kind, "path"),
            "openinexplorer" => WellKnownActions.OpenInExplorer.ExecuteAsync(
                ActionStringParam.From(host.Path!)),
            "openinbrowser" when string.IsNullOrWhiteSpace(ReadUrls(host.Url)) => InvalidPayload(host.Kind, "url"),
            "openinbrowser" => WellKnownActions.OpenInBrowser.ExecuteAsync(
                ActionStringParam.From(ReadUrls(host.Url))),
            "openplugin" when string.IsNullOrWhiteSpace(host.PluginId) => InvalidPayload(host.Kind, "pluginId"),
            "openplugin" => WellKnownActions.OpenPlugin.ExecuteAsync(
                ActionStringParam.From(host.PluginId!)),
            "run" when host.Command.ValueKind != JsonValueKind.Object => InvalidPayload(host.Kind, "command"),
            "run" => new RunCommandAction().ExecuteAsync(ActionStringParam.From(ReadCommand(host.Command))),
            "kill" when host.Pid <= 0 => InvalidPayload(host.Kind, "pid"),
            "kill" => new KillProcessAction().ExecuteAsync(
                ActionStringParam.From(host.Pid.ToString(System.Globalization.CultureInfo.InvariantCulture))),
            _ => Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
                "NodePlugin.UnknownHostAction",
                "Unknown host action: {{kind}}",
                new { kind = host.Kind })))
        };
    }

    private static Task<ActionResult> InvalidPayload(string kind, string field) =>
        Task.FromResult(ActionResult.CreateFailure(new LocalizedMessage(
            "NodePlugin.InvalidHostAction",
            "Host action {{kind}} requires {{field}}.",
            new { kind, field })));

    /// <summary>openInBrowser 接受单个 url 或 url 数组，OpenInBrowser 按逗号切分。</summary>
    private static string ReadUrls(JsonElement url)
    {
        return url.ValueKind switch
        {
            JsonValueKind.String => url.GetString() ?? string.Empty,
            JsonValueKind.Array => string.Join(
                OpenInBrowser.SplitStr,
                url.EnumerateArray()
                    .Where(item => item.ValueKind == JsonValueKind.String)
                    .Select(item => item.GetString())
                    .Where(value => !string.IsNullOrWhiteSpace(value))),
            _ => string.Empty
        };
    }

    /// <summary>RunCommandAction 接受 CommandSpec 的 JSON 文本。</summary>
    private static string ReadCommand(JsonElement command) =>
        command.ValueKind == JsonValueKind.Object ? command.GetRawText() : "{}";

    private static string Normalize(string? kind) =>
        (kind ?? string.Empty).Trim().Replace("-", "", StringComparison.Ordinal).ToLowerInvariant();
}
