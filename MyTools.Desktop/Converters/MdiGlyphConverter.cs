using System.Collections.Frozen;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using MyTools.Plugins;

namespace MyTools.Desktop.Converters;

public sealed class MdiGlyphConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var name = value switch
        {
            MdiIcon icon => icon.Name,
            string text => text,
            _ => null
        };
        return MdiGlyphLookup.Get(name);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}

internal static class MdiGlyphLookup
{
    private static readonly Lazy<FrozenDictionary<string, string>> glyphs = new(Load);

    public static string Get(string? name)
    {
        var key = MdiIcon.ToLigatureName(name);
        if (key.Length == 0)
        {
            return string.Empty;
        }

        return glyphs.Value.TryGetValue(key, out var glyph) ? glyph : key;
    }

    private static FrozenDictionary<string, string> Load()
    {
        var assembly = typeof(MdiGlyphLookup).Assembly;
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(name => name.EndsWith("mdi-glyphs.txt", StringComparison.OrdinalIgnoreCase));
        if (resourceName == null)
        {
            return FrozenDictionary<string, string>.Empty;
        }

        using var stream = assembly.GetManifestResourceStream(resourceName);
        if (stream == null)
        {
            return FrozenDictionary<string, string>.Empty;
        }

        using var reader = new StreamReader(stream);
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#')
            {
                continue;
            }

            var separator = line.IndexOf('=');
            if (separator <= 0 || separator == line.Length - 1)
            {
                continue;
            }

            var key = line[..separator];
            var hex = line[(separator + 1)..];
            if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var utf32))
            {
                continue;
            }

            map[key] = char.ConvertFromUtf32(utf32);
        }

        return map.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
