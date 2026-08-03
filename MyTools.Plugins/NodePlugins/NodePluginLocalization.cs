using System.Globalization;
using System.IO;
using System.Text.Json;

namespace MyTools.Plugins.NodePlugins;

public static class NodePluginLocalization
{
    private const long MaxResourceFileBytes = 2 * 1024 * 1024;
    private const int MaxEntries = 10_000;

    public static IReadOnlyDictionary<string, string> LoadMessages(NodePluginManifest manifest, string locale)
    {
        var messages = new Dictionary<string, string>(StringComparer.Ordinal);
        LoadCatalogFallbacks(manifest.CatalogFullPath, messages);

        if (string.IsNullOrWhiteSpace(manifest.LocalesDirectoryFullPath))
        {
            return messages;
        }

        LoadLocaleFile(manifest.LocalesDirectoryFullPath, manifest.DefaultLocale, messages);
        var requestedCulture = TryGetCulture(locale);
        if (requestedCulture != null && !string.IsNullOrEmpty(requestedCulture.Parent.Name))
        {
            LoadLocaleFile(manifest.LocalesDirectoryFullPath, requestedCulture.Parent.Name, messages);
        }
        LoadLocaleFile(manifest.LocalesDirectoryFullPath, requestedCulture?.Name ?? locale, messages);
        return messages;
    }

    private static void LoadCatalogFallbacks(string? path, IDictionary<string, string> messages)
    {
        if (!CanRead(path))
        {
            return;
        }

        using var stream = File.OpenRead(path!);
        using var document = JsonDocument.Parse(stream, JsonOptions);
        if (!document.RootElement.TryGetProperty("entries", out var entries)
            || entries.ValueKind != JsonValueKind.Array
            || entries.GetArrayLength() > MaxEntries)
        {
            return;
        }

        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.TryGetProperty("key", out var keyElement)
                && entry.TryGetProperty("defaultValue", out var valueElement))
            {
                AddMessage(messages, keyElement.GetString(), valueElement.GetString());
            }
        }
    }

    private static void LoadLocaleFile(string directory, string? locale, IDictionary<string, string> messages)
    {
        if (string.IsNullOrWhiteSpace(locale))
        {
            return;
        }

        var path = Path.GetFullPath(Path.Combine(directory, $"{locale}.json"));
        var fullDirectory = Path.GetFullPath(directory) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(fullDirectory, StringComparison.OrdinalIgnoreCase) || !CanRead(path))
        {
            return;
        }

        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream, JsonOptions);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || document.RootElement.EnumerateObject().Take(MaxEntries + 1).Count() > MaxEntries)
        {
            return;
        }

        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && messages.ContainsKey(property.Name))
            {
                AddMessage(messages, property.Name, property.Value.GetString());
            }
        }
    }

    private static void AddMessage(IDictionary<string, string> messages, string? key, string? value)
    {
        if (!string.IsNullOrWhiteSpace(key)
            && !string.IsNullOrEmpty(value)
            && key.Length <= 512
            && value.Length <= 16_384)
        {
            messages[key] = value;
        }
    }

    private static bool CanRead(string? path) =>
        !string.IsNullOrWhiteSpace(path)
        && File.Exists(path)
        && new FileInfo(path).Length <= MaxResourceFileBytes;

    private static CultureInfo? TryGetCulture(string locale)
    {
        try
        {
            return CultureInfo.GetCultureInfo(locale);
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static readonly JsonDocumentOptions JsonOptions = new()
    {
        MaxDepth = 32,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };
}



