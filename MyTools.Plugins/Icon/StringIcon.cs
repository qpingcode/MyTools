using MyTools.Common;

namespace MyTools.Plugins;

public class StringIcon(string emoji) : Icon
{
    public string Emoji { get; } = emoji;
    public static StringIcon Empty => new(string.Empty);
}