using MyTools.Common;
using MyTools.Plugins.NodePlugins;
using MyTools.Plugins.Param;

namespace MyTools.Plugins;

// Persist only declarative data. Runtime action objects are reconnected by stable id.
public sealed record SearchResultSnapshot(
    string Title,
    string SubTitle,
    string IconKind,
    string IconValue,
    IReadOnlyList<SearchActionSnapshot>? Actions = null,
    string? ArgumentKind = null,
    string? ArgumentValue = null,
    string? ArgumentExtra = null,
    int SchemaVersion = 0) : IActionParams
{
    public const int CurrentSchemaVersion = 1;
    private const string StringArgument = "string";
    private const string ExecuteArgument = "execute";
    private const string NodeArgument = "node";

    public static SearchResultSnapshot Create(ResultItem item)
    {
        var previous = item.Args as SearchResultSnapshot;
        var (kind, value, extra) = SerializeArguments(item.Args, previous);
        return new(item.Title, item.SubTitle ?? string.Empty,
            item.Icon is ImageIcon ? "image" : item.Icon is MdiIcon ? "mdi" : "emoji",
            item.Icon switch
            {
                ImageIcon image => Convert.ToBase64String(image.ImageData),
                MdiIcon mdi => mdi.Name,
                StringIcon emoji => emoji.Emoji,
                _ => string.Empty
            },
            item.AllowedActions.Select(SearchActionSnapshot.Create).ToArray(),
            kind,
            value,
            extra,
            CurrentSchemaVersion);
    }

    public bool IsCurrent => SchemaVersion == CurrentSchemaVersion;

    public IActionParams? RestoreArguments() => ArgumentKind switch
    {
        ExecuteArgument when ArgumentValue != null => new ExecuteActionParams(ArgumentValue, ArgumentExtra ?? string.Empty),
        StringArgument when ArgumentValue != null => ActionStringParam.From(ArgumentValue),
        NodeArgument when ArgumentValue != null => new NodePluginActionArgs(ArgumentValue, ArgumentExtra ?? string.Empty),
        _ => null
    };

    private static (string? Kind, string? Value, string? Extra) SerializeArguments(
        IActionParams args,
        SearchResultSnapshot? previous)
    {
        return args switch
        {
            ExecuteActionParams execute => (ExecuteArgument, execute.GetValue(), execute.Arguments),
            NodePluginActionArgs node => (NodeArgument, node.ItemId, node.Query),
            IActionStringParam value => (StringArgument, value.GetValue(), null),
            SearchResultSnapshot => (previous?.ArgumentKind, previous?.ArgumentValue, previous?.ArgumentExtra),
            _ => (null, null, null)
        };
    }

    public Icon RestoreIcon() => IconKind switch
    {
        "image" => new ImageIcon(Convert.FromBase64String(IconValue)),
        "mdi" => new MdiIcon(IconValue),
        _ => new StringIcon(IconValue)
    };
}

public sealed record SearchActionSnapshot(
    string Id,
    string Name,
    string Description,
    HotkeyKey Key,
    HotkeyModifiers Modifiers,
    bool Pinned)
{
    public Hotkey Hotkey => new(Key, Modifiers);

    public static SearchActionSnapshot Create(IActionWithHotkey action) => new(
        action.Id,
        action.Name,
        action.Description,
        action.Hotkey.Key,
        action.Hotkey.Modifiers,
        action.Pinned);
}
