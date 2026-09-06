using MyTools.Common;

namespace MyTools.Plugins;

// Only presentation data is persisted; executable objects are resolved when clicked.
public sealed record SearchResultSnapshot(string Title, string SubTitle, string IconKind, string IconValue) : IActionParams
{
    public static SearchResultSnapshot Create(ResultItem item) => item.Args as SearchResultSnapshot
        ?? new(item.Title, item.SubTitle ?? string.Empty,
            item.Icon is ImageIcon ? "image" : item.Icon is MdiIcon ? "mdi" : "emoji",
            item.Icon switch
            {
                ImageIcon image => Convert.ToBase64String(image.ImageData),
                MdiIcon mdi => mdi.Name,
                StringIcon emoji => emoji.Emoji,
                _ => string.Empty
            });

    public Icon RestoreIcon() => IconKind switch
    {
        "image" => new ImageIcon(Convert.FromBase64String(IconValue)),
        "mdi" => new MdiIcon(IconValue),
        _ => new StringIcon(IconValue)
    };
}
