using System.Globalization;
using System.Windows.Data;

namespace MyTools.Desktop.Converters;

public sealed class HotkeyPart
{
    public required string Text { get; init; }
    public bool IsReturn { get; init; }
    public string? MdiName { get; init; }
}

public sealed class HotkeyPartsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return Parse(value as string);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    public static IReadOnlyList<HotkeyPart> Parse(string? command)
    {
        if (string.IsNullOrWhiteSpace(command) || command.Contains(':', StringComparison.Ordinal))
        {
            return [];
        }

        var parts = new List<HotkeyPart>();
        foreach (var token in command.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            parts.Add(ToPart(token));
        }

        return parts;
    }

    public static string ToGestureText(string? command)
    {
        var parts = Parse(command);
        return parts.Count == 0
            ? string.Empty
            : string.Join("+", parts.Select(part => part.Text));
    }

    private static HotkeyPart ToPart(string token)
    {
        if (token.Equals("Enter", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Return", StringComparison.OrdinalIgnoreCase))
        {
            return new HotkeyPart { Text = "Enter", IsReturn = true, MdiName = "mdi-keyboard-return" };
        }

        return new HotkeyPart { Text = FormatKey(token), IsReturn = false };
    }

    private static string FormatKey(string token)
    {
        if (token.Equals("Control", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Ctrl", StringComparison.OrdinalIgnoreCase)
            || token.Equals("LeftCtrl", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RightCtrl", StringComparison.OrdinalIgnoreCase))
        {
            return "Ctrl";
        }

        if (token.Equals("Alt", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Menu", StringComparison.OrdinalIgnoreCase)
            || token.Equals("LeftAlt", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RightAlt", StringComparison.OrdinalIgnoreCase)
            || token.Equals("System", StringComparison.OrdinalIgnoreCase))
        {
            return "Alt";
        }

        if (token.Equals("Shift", StringComparison.OrdinalIgnoreCase)
            || token.Equals("LeftShift", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RightShift", StringComparison.OrdinalIgnoreCase))
        {
            return "Shift";
        }

        if (token.Equals("Windows", StringComparison.OrdinalIgnoreCase)
            || token.Equals("Win", StringComparison.OrdinalIgnoreCase)
            || token.Equals("LWin", StringComparison.OrdinalIgnoreCase)
            || token.Equals("RWin", StringComparison.OrdinalIgnoreCase))
        {
            return "Win";
        }

        if (token.Length == 2
            && (token[0] is 'D' or 'd')
            && char.IsDigit(token[1]))
        {
            return token[1].ToString();
        }

        return token;
    }
}
