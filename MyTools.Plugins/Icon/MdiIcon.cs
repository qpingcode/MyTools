using MyTools.Common;

namespace MyTools.Plugins;

public sealed class MdiIcon(string name) : Icon
{
    public string Name { get; } = Normalize(name);

    public string LigatureName { get; } = ToLigatureName(name);

    public static MdiIcon ForClipboardKind(ClipboardContentKind kind) => kind switch
    {
        ClipboardContentKind.Image => new("mdi-image-outline"),
        ClipboardContentKind.File => new("mdi-file-outline"),
        ClipboardContentKind.Mixed => new("mdi-puzzle-outline"),
        ClipboardContentKind.Other => new("mdi-help-circle-outline"),
        _ => new("mdi-format-text")
    };

    public static string ToLigatureName(string? name)
    {
        var normalized = Normalize(name);
        return normalized.StartsWith("mdi-", StringComparison.OrdinalIgnoreCase)
            ? normalized["mdi-".Length..]
            : normalized;
    }

    private static string Normalize(string? name) => (name ?? string.Empty).Trim().ToLowerInvariant();
}
