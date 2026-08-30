using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace MyTools.Desktop.Services;

public sealed class HubMarketplaceService(
    HubApiClient client,
    DevelopmentPluginService developmentPlugins)
{
    public Task<HubPluginList> SearchAsync(string? query, CancellationToken cancellationToken, string? locale = null)
    {
        var path = $"/api/plugins?q={Uri.EscapeDataString(query ?? "")}&take=50";
        if (!string.IsNullOrWhiteSpace(locale))
        {
            path += "&locale=" + Uri.EscapeDataString(locale.Trim());
        }

        return client.GetAsync<HubPluginList>(path, authenticate: false, cancellationToken);
    }

    public Task<HubPluginDetail> GetAsync(string pluginId, string? locale, CancellationToken cancellationToken)
    {
        var path = $"/api/plugins/{Uri.EscapeDataString(pluginId)}";
        if (!string.IsNullOrWhiteSpace(locale))
        {
            path += "?locale=" + Uri.EscapeDataString(locale.Trim());
        }

        return client.GetAsync<HubPluginDetail>(path, authenticate: false, cancellationToken);
    }

    public async Task<object> InstallAsync(string pluginId, string? version, CancellationToken cancellationToken)
    {
        var query = string.IsNullOrWhiteSpace(version) ? "" : "?version=" + Uri.EscapeDataString(version);
        var bytes = await client.DownloadAsync($"/api/plugins/{Uri.EscapeDataString(pluginId)}/download{query}", cancellationToken);
        var extractPath = Path.Combine(Path.GetTempPath(), "MyTools.Hub", pluginId + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(extractPath);
        var zipPath = extractPath + ".zip";
        try
        {
            await File.WriteAllBytesAsync(zipPath, bytes, cancellationToken);
            ZipFile.ExtractToDirectory(zipPath, extractPath);
            var manifestPath = Path.Combine(extractPath, "plugin.json");
            if (!File.Exists(manifestPath))
            {
                throw new InvalidOperationException("The downloaded package is missing plugin.json.");
            }

            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
            var id = document.RootElement.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
            if (string.IsNullOrWhiteSpace(id))
            {
                throw new InvalidOperationException("The downloaded plugin.json is missing id.");
            }

            await developmentPlugins.InstallFromDirectoryAsync(id, extractPath, cancellationToken);
            return new { success = true, pluginId = id };
        }
        finally
        {
            TryDelete(extractPath);
            TryDelete(zipPath);
        }
    }

    public Task UninstallAsync(string pluginId, CancellationToken cancellationToken) =>
        developmentPlugins.UninstallInstalledPluginAsync(pluginId, cancellationToken);

    public async Task<HubPluginDetail> PublishDevelopmentAsync(
        string pluginId,
        string? version,
        CancellationToken cancellationToken)
    {
        var validation = await ValidateDevelopmentPublishAsync(pluginId, version, cancellationToken);
        if (!validation.IsValid)
        {
            throw new InvalidOperationException(validation.Message);
        }

        var registration = developmentPlugins.GetAiEditableRegistration(pluginId);
        await developmentPlugins.BuildDevelopmentPluginAsync(registration.PluginId, cancellationToken);
        var distPath = Path.GetFullPath(registration.DistPath);
        var zipPath = Path.Combine(Path.GetTempPath(), $"mytools-hub-{validation.PluginId}-{Guid.NewGuid():N}.zip");
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(distPath, zipPath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
            var readmePaths = Directory.EnumerateFiles(registration.SourcePath, "README*.md", SearchOption.TopDirectoryOnly)
                .ToArray();
            if (readmePaths.Length != 0)
            {
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
                foreach (var readmePath in readmePaths)
                {
                    zip.CreateEntryFromFile(readmePath, Path.GetFileName(readmePath));
                }
            }

            await using var stream = File.OpenRead(zipPath);
            return await client.PostMultipartAsync<HubPluginDetail>(
                "/api/plugins", stream, $"{validation.PluginId}.zip", cancellationToken);
        }
        finally
        {
            TryDelete(zipPath);
        }
    }

    public async Task<HubPublishValidation> ValidateDevelopmentPublishAsync(
        string pluginId,
        string? version,
        CancellationToken cancellationToken)
    {
        var registration = developmentPlugins.GetAiEditableRegistration(pluginId);
        var manifestPath = Path.Combine(registration.SourcePath, "plugin.json");
        if (!File.Exists(manifestPath))
        {
            return HubPublishValidation.Invalid(pluginId, "", "manifest", "The plugin source is missing plugin.json.");
        }

        var applied = await TryApplyVersionOverrideAsync(
            registration.SourcePath, pluginId, version, cancellationToken);
        if (applied is not null)
        {
            return applied;
        }

        string resolvedId;
        string resolvedVersion;
        try
        {
            using var document = JsonDocument.Parse(await File.ReadAllTextAsync(manifestPath, cancellationToken));
            var root = document.RootElement;
            resolvedId = root.TryGetProperty("id", out var id) ? id.GetString()?.Trim() ?? "" : "";
            resolvedVersion = root.TryGetProperty("version", out var value) ? value.GetString()?.Trim() ?? "" : "";
        }
        catch (JsonException)
        {
            return HubPublishValidation.Invalid(pluginId, "", "manifest", "plugin.json is not valid JSON.");
        }

        if (!ValidPluginId.IsMatch(resolvedId))
        {
            return HubPublishValidation.Invalid(resolvedId, resolvedVersion, "pluginId", "Plugin ID must use lowercase letters, numbers, dots or hyphens (maximum 64 characters).");
        }
        if (!string.Equals(resolvedId, pluginId, StringComparison.OrdinalIgnoreCase))
        {
            return HubPublishValidation.Invalid(pluginId, resolvedVersion, "pluginId", "plugin.json id must match the plugin ID.");
        }
        if (!SemanticVersion.TryParse(resolvedVersion, out var candidate))
        {
            return HubPublishValidation.Invalid(resolvedId, resolvedVersion, "version", "Enter a valid semantic version, for example 1.0.0.");
        }
        if (!client.IsSignedIn)
        {
            return HubPublishValidation.Invalid(resolvedId, resolvedVersion, "account", "Sign in to MyTools Hub before publishing.");
        }

        var matches = await SearchAsync(resolvedId, cancellationToken);
        var existing = matches.Items.FirstOrDefault(item =>
            string.Equals(item.Id, resolvedId, StringComparison.OrdinalIgnoreCase));
        if (existing is null)
        {
            return new HubPublishValidation(true, resolvedId, resolvedVersion, null, null, null);
        }

        var username = client.Session?.Username;
        if (string.IsNullOrWhiteSpace(username)
            || !string.Equals(existing.OwnerUsername, username, StringComparison.OrdinalIgnoreCase))
        {
            return HubPublishValidation.Invalid(resolvedId, resolvedVersion, "pluginId", "This plugin ID is already used by another publisher.", existing.CurrentVersion);
        }
        if (!SemanticVersion.TryParse(existing.CurrentVersion, out var published)
            || candidate.CompareTo(published) <= 0)
        {
            return HubPublishValidation.Invalid(
                resolvedId,
                resolvedVersion,
                "version",
                $"Version must be higher than the published version {existing.CurrentVersion}.",
                existing.CurrentVersion);
        }

        return new HubPublishValidation(true, resolvedId, resolvedVersion, existing.CurrentVersion, null, null);
    }

    private static readonly Regex ValidPluginId = new(
        "^[a-z0-9](?:[a-z0-9.-]{0,62}[a-z0-9])?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly JsonSerializerOptions ManifestJsonOptions = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static async Task<HubPublishValidation?> TryApplyVersionOverrideAsync(
        string sourcePath,
        string pluginId,
        string? version,
        CancellationToken cancellationToken)
    {
        var nextVersion = string.IsNullOrWhiteSpace(version) ? null : version.Trim();
        if (nextVersion is null)
        {
            return null;
        }

        if (!SemanticVersion.TryParse(nextVersion, out _))
        {
            return HubPublishValidation.Invalid(
                pluginId,
                nextVersion,
                "version",
                "Enter a valid semantic version, for example 1.0.0.");
        }

        try
        {
            await WriteJsonStringPropertyAsync(
                Path.Combine(sourcePath, "plugin.json"),
                ("version", nextVersion),
                cancellationToken: cancellationToken);
            var packagePath = Path.Combine(sourcePath, "package.json");
            if (File.Exists(packagePath))
            {
                await WriteJsonStringPropertyAsync(packagePath, ("version", nextVersion), cancellationToken: cancellationToken);
            }
        }
        catch (JsonException)
        {
            return HubPublishValidation.Invalid(pluginId, nextVersion, "manifest", "plugin.json is not valid JSON.");
        }
        catch (IOException ex)
        {
            return HubPublishValidation.Invalid(pluginId, nextVersion, "manifest", ex.Message);
        }

        return null;
    }

    private static async Task WriteJsonStringPropertyAsync(
        string path,
        (string Name, string Value)? first = null,
        (string Name, string Value)? second = null,
        CancellationToken cancellationToken = default)
    {
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken)) as JsonObject
            ?? throw new JsonException("Expected a JSON object.");
        if (first is { } firstProperty) node[firstProperty.Name] = firstProperty.Value;
        if (second is { } secondProperty) node[secondProperty.Name] = secondProperty.Value;
        await File.WriteAllTextAsync(path, node.ToJsonString(ManifestJsonOptions) + Environment.NewLine, cancellationToken);
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            if (Directory.Exists(path)) Directory.Delete(path, true);
        }
        catch
        {
            /* temp cleanup */
        }
    }
}

public sealed record HubPublishValidation(
    bool IsValid,
    string PluginId,
    string Version,
    string? PublishedVersion,
    string? Conflict,
    string? Message)
{
    public static HubPublishValidation Invalid(
        string pluginId,
        string version,
        string conflict,
        string message,
        string? publishedVersion = null) =>
        new(false, pluginId, version, publishedVersion, conflict, message);
}

internal readonly record struct SemanticVersion(int Major, int Minor, int Patch, string? PreRelease)
    : IComparable<SemanticVersion>
{
    public static bool TryParse(string value, out SemanticVersion version)
    {
        version = default;
        var match = System.Text.RegularExpressions.Regex.Match(
            value,
            @"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$");
        if (!match.Success
            || !int.TryParse(match.Groups[1].Value, out var major)
            || !int.TryParse(match.Groups[2].Value, out var minor)
            || !int.TryParse(match.Groups[3].Value, out var patch))
        {
            return false;
        }
        version = new SemanticVersion(major, minor, patch, match.Groups[4].Success ? match.Groups[4].Value : null);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var comparison = Major.CompareTo(other.Major);
        if (comparison != 0) return comparison;
        comparison = Minor.CompareTo(other.Minor);
        if (comparison != 0) return comparison;
        comparison = Patch.CompareTo(other.Patch);
        if (comparison != 0) return comparison;
        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;

        var left = PreRelease.Split('.');
        var right = other.PreRelease.Split('.');
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            if (index >= left.Length) return -1;
            if (index >= right.Length) return 1;
            var leftNumeric = int.TryParse(left[index], out var leftNumber);
            var rightNumeric = int.TryParse(right[index], out var rightNumber);
            if (leftNumeric && rightNumeric) comparison = leftNumber.CompareTo(rightNumber);
            else if (leftNumeric != rightNumeric) comparison = leftNumeric ? -1 : 1;
            else comparison = string.Compare(left[index], right[index], StringComparison.Ordinal);
            if (comparison != 0) return comparison;
        }
        return 0;
    }
}

public sealed class HubPluginList
{
    public List<HubPluginSummary> Items { get; set; } = [];
    public int Total { get; set; }
}

public class HubPluginSummary
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Icon { get; set; }
    public string CurrentVersion { get; set; } = "";
    public string ProtocolVersion { get; set; } = "";
    public long DownloadCount { get; set; }
    public string OwnerUsername { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public bool Installed { get; set; }
    public string? InstalledVersion { get; set; }
    public bool UpdateAvailable { get; set; }
    public bool CanUninstall { get; set; }
}

public sealed class HubPluginDetail : HubPluginSummary
{
    public string? Readme { get; set; }
    public string? Changelog { get; set; }
    public List<HubPluginVersion> Versions { get; set; } = [];
}

public sealed class HubPluginVersion
{
    public string Version { get; set; } = "";
    public string ProtocolVersion { get; set; } = "";
    public long FileSize { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
