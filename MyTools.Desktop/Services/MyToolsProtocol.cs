namespace MyTools.Desktop.Services;

public sealed record MyToolsProtocolRequest(string Action, string? PluginId, string? Version);

public static class MyToolsProtocol
{
    public const string Scheme = "mytools";
    public const string InstallAction = "install";

    public static bool IsActivation(string? value) => TryParse(value, out _);

    public static bool TryParse(string? raw, out MyToolsProtocolRequest request)
    {
        request = new MyToolsProtocolRequest("", null, null);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return false;
        }

        var text = raw.Trim().Trim('"');
        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri)
            || !uri.Scheme.Equals(Scheme, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var action = uri.Host;
        if (string.IsNullOrWhiteSpace(action))
        {
            var first = uri.AbsolutePath.Trim('/').Split('/', 2)[0];
            action = first;
        }

        if (string.IsNullOrWhiteSpace(action))
        {
            return false;
        }

        var query = ParseQuery(uri.Query);
        query.TryGetValue("pluginId", out var pluginId);
        pluginId ??= query.GetValueOrDefault("id");
        if (string.IsNullOrWhiteSpace(pluginId))
        {
            pluginId = PluginIdFromPath(uri.AbsolutePath, action);
        }

        query.TryGetValue("version", out var version);
        request = new MyToolsProtocolRequest(action.Trim(), EmptyToNull(pluginId), EmptyToNull(version));
        return true;
    }

    public static string InstallUri(string pluginId, string? version = null)
    {
        var uri = $"{Scheme}://{InstallAction}?pluginId={Uri.EscapeDataString(pluginId)}";
        if (!string.IsNullOrWhiteSpace(version))
        {
            uri += "&version=" + Uri.EscapeDataString(version);
        }

        return uri;
    }

    private static string? PluginIdFromPath(string path, string action)
    {
        var segments = path.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
        {
            return null;
        }

        if (segments.Length == 1)
        {
            return segments[0].Equals(action, StringComparison.OrdinalIgnoreCase) ? null : segments[0];
        }

        return segments[^1];
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var text = query.StartsWith('?') ? query[1..] : query;
        foreach (var part in text.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = part.IndexOf('=');
            if (separator <= 0)
            {
                continue;
            }

            var name = Uri.UnescapeDataString(part[..separator]);
            var value = Uri.UnescapeDataString(part[(separator + 1)..].Replace('+', ' '));
            result[name] = value;
        }

        return result;
    }

    private static string? EmptyToNull(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
