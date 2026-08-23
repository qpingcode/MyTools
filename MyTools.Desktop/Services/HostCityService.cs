using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using MyTools.AI;

namespace MyTools.Desktop.Services;

public sealed class HostCityService : IHostCityProvider, IDisposable
{
    private static readonly Uri Endpoint = new("https://ipwho.is/");
    private static readonly TimeSpan SuccessCacheDuration = TimeSpan.FromHours(6);
    private static readonly TimeSpan FailureCacheDuration = TimeSpan.FromMinutes(5);
    private readonly HttpClient httpClient;
    private readonly bool ownsHttpClient;
    private readonly SemaphoreSlim gate = new(1, 1);
    private HostCityResult? cached;
    private DateTimeOffset cacheExpiresAt;

    public HostCityService() : this(CreateHttpClient(), true)
    {
    }

    public HostCityService(HttpClient httpClient) : this(httpClient, false)
    {
    }

    private HostCityService(HttpClient httpClient, bool ownsHttpClient)
    {
        this.httpClient = httpClient;
        this.ownsHttpClient = ownsHttpClient;
    }

    public async Task<HostCityResult> GetCityAsync(CancellationToken cancellationToken = default)
    {
        if (cached is not null && DateTimeOffset.UtcNow < cacheExpiresAt) return cached;
        await gate.WaitAsync(cancellationToken);
        try
        {
            if (cached is not null && DateTimeOffset.UtcNow < cacheExpiresAt) return cached;
            HostCityResult result;
            try
            {
                using var response = await httpClient.GetAsync(Endpoint, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                var root = document.RootElement;
                if (root.TryGetProperty("success", out var success) && !success.GetBoolean())
                {
                    var message = ReadString(root, "message") ?? "The location provider did not return a city.";
                    result = Unavailable(message);
                }
                else
                {
                    var city = ReadString(root, "city");
                    result = string.IsNullOrWhiteSpace(city)
                        ? Unavailable("The location provider did not return a city.")
                        : new HostCityResult(
                            true,
                            city,
                            ReadString(root, "region"),
                            ReadString(root, "country"),
                            ReadString(root, "country_code"),
                            true,
                            "public-ip");
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                result = Unavailable("The city lookup timed out.");
            }
            catch (Exception ex)
            {
                result = Unavailable($"City lookup failed: {ex.Message}");
            }
            cached = result;
            cacheExpiresAt = DateTimeOffset.UtcNow + (result.Available ? SuccessCacheDuration : FailureCacheDuration);
            return result;
        }
        finally
        {
            gate.Release();
        }
    }

    private static HostCityResult Unavailable(string error) =>
        new(false, null, null, null, null, true, "public-ip", error);

    private static string? ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("MyTools", "1.0"));
        return client;
    }

    public void Dispose()
    {
        gate.Dispose();
        if (ownsHttpClient) httpClient.Dispose();
    }
}
