namespace MyTools.Plugins;

public enum ClipboardContentKind
{
    Text,
    Image,
    File,
    Mixed,
    Other
}

public static class ClipboardContentKindParser
{
    public static string ToStorage(ClipboardContentKind kind) => kind switch
    {
        ClipboardContentKind.Image => "image",
        ClipboardContentKind.File => "file",
        ClipboardContentKind.Mixed => "mixed",
        ClipboardContentKind.Other => "other",
        _ => "text"
    };

    public static ClipboardContentKind Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "image" => ClipboardContentKind.Image,
        "file" => ClipboardContentKind.File,
        "mixed" => ClipboardContentKind.Mixed,
        "other" => ClipboardContentKind.Other,
        _ => ClipboardContentKind.Text
    };
}
