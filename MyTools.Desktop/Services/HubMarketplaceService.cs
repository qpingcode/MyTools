using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace MyTools.Desktop.Services;

public sealed class HubMarketplaceService(
    HubApiClient client,
    DevelopmentPluginService developmentPlugins)
{
    public Task<HubPluginList> SearchAsync(string? query, CancellationToken cancellationToken) =>
        client.GetAsync<HubPluginList>(
            $"/api/plugins?q={Uri.EscapeDataString(query ?? "")}&take=50",
            authenticate: false,
            cancellationToken);

    public Task<HubPluginDetail> GetAsync(string pluginId, CancellationToken cancellationToken) =>
        client.GetAsync<HubPluginDetail>(
            $"/api/plugins/{Uri.EscapeDataString(pluginId)}",
            authenticate: false,
            cancellationToken);

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

    public async Task<HubPluginDetail> PublishDevelopmentAsync(string pluginId, CancellationToken cancellationToken)
    {
        var registration = developmentPlugins.GetAiEditableRegistration(pluginId);
        await developmentPlugins.BuildDevelopmentPluginAsync(registration.PluginId, cancellationToken);
        var distPath = Path.GetFullPath(registration.DistPath);
        var readmePath = Path.Combine(registration.SourcePath, "README.md");
        var zipPath = Path.Combine(Path.GetTempPath(), $"mytools-hub-{pluginId}-{Guid.NewGuid():N}.zip");
        try
        {
            if (File.Exists(zipPath)) File.Delete(zipPath);
            ZipFile.CreateFromDirectory(distPath, zipPath, CompressionLevel.SmallestSize, includeBaseDirectory: false);
            if (File.Exists(readmePath))
            {
                using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Update);
                zip.CreateEntryFromFile(readmePath, "README.md");
            }

            await using var stream = File.OpenRead(zipPath);
            return await client.PostMultipartAsync<HubPluginDetail>(
                "/api/plugins", stream, $"{pluginId}.zip", cancellationToken);
        }
        finally
        {
            TryDelete(zipPath);
        }
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
}

public sealed class HubPluginDetail : HubPluginSummary
{
    public string? Readme { get; set; }
    public List<HubPluginVersion> Versions { get; set; } = [];
}

public sealed class HubPluginVersion
{
    public string Version { get; set; } = "";
    public string ProtocolVersion { get; set; } = "";
    public long FileSize { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}
