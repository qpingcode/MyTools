namespace MyTools.Common.Config;

/// <summary>
/// Resolves supported macros in setting default values. Currently <c>${DateTime.Now}</c>.
/// </summary>
public static class SettingMacroResolver
{
    public const string DateTimeNow = "${DateTime.Now}";

    public static string Resolve(string? value, DateTime? now = null)
    {
        if (string.IsNullOrEmpty(value) || !value.Contains(DateTimeNow, StringComparison.Ordinal))
        {
            return value ?? string.Empty;
        }

        var timestamp = (now ?? DateTime.Now).ToString("O");
        return value.Replace(DateTimeNow, timestamp, StringComparison.Ordinal);
    }
}
