using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using MyTools.Common.Config;
using MyTools.Common.Config.Interfaces;

namespace MyTools.Desktop.Services;

public sealed class HubSession
{
    public string HubUrl { get; set; } = "";
    public string AccessToken { get; set; } = "";
    public string Username { get; set; } = "";
    public string UserId { get; set; } = "";
}

public sealed class HubAccountStatus
{
    public bool SignedIn { get; init; }
    public string? Username { get; init; }
    public string HubUrl { get; init; } = "";
    public bool Google { get; init; }
    public bool Microsoft { get; init; }
}

public sealed class HubApiClient
{
    public const string DefaultHubUrl = GeneralSettings.DefaultHubUrl;
    private static readonly string SessionPath = Path.Combine(ConfigPath.Base, "HubSession.json");
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly IConfigurationRegistry registry;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromMinutes(2) };
    private readonly object gate = new();
    private HubSession? session;

    public HubApiClient(IConfigurationRegistry registry)
    {
        this.registry = registry;
        session = LoadSession();
    }

    public string HubUrl
    {
        get
        {
            var configured = registry.FindSetting(GeneralSettings.HubUrl)?.GetValue<string>()?.Trim();
            if (!string.IsNullOrWhiteSpace(configured))
            {
                return configured.TrimEnd('/');
            }

            return (session?.HubUrl ?? DefaultHubUrl).TrimEnd('/');
        }
    }

    public HubSession? Session
    {
        get
        {
            lock (gate) return session;
        }
    }

    public bool IsSignedIn => !string.IsNullOrWhiteSpace(Session?.AccessToken);

    public void SetSession(HubSession value)
    {
        lock (gate)
        {
            session = value;
            Directory.CreateDirectory(ConfigPath.Base);
            File.WriteAllText(SessionPath, JsonSerializer.Serialize(value, JsonOptions));
        }
    }

    public void ClearSession()
    {
        lock (gate)
        {
            session = null;
            if (File.Exists(SessionPath))
            {
                File.Delete(SessionPath);
            }
        }
    }

    public async Task<T> GetAsync<T>(string path, bool authenticate, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path, authenticate);
        return await SendAsync<T>(request, cancellationToken);
    }

    public async Task<T> SendJsonAsync<T>(HttpMethod method, string path, object body, bool authenticate, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(method, path, authenticate);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return await SendAsync<T>(request, cancellationToken);
    }

    public async Task<T> PostMultipartAsync<T>(string path, Stream zip, string fileName, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Post, path, authenticate: true);
        var content = new MultipartFormDataContent();
        var streamContent = new StreamContent(zip);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue("application/zip");
        content.Add(streamContent, "package", fileName);
        request.Content = content;
        return await SendAsync<T>(request, cancellationToken);
    }

    public async Task<byte[]> DownloadAsync(string path, CancellationToken cancellationToken)
    {
        using var request = CreateRequest(HttpMethod.Get, path, authenticate: false);
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccess(response);
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, bool authenticate)
    {
        var request = new HttpRequestMessage(method, HubUrl + path);
        if (authenticate)
        {
            var token = Session?.AccessToken;
            if (string.IsNullOrWhiteSpace(token))
            {
                throw new InvalidOperationException("Sign in to MyTools Hub first.");
            }

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return request;
    }

    private async Task<T> SendAsync<T>(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using var response = await http.SendAsync(request, cancellationToken);
        await EnsureSuccess(response);
        if (response.StatusCode == System.Net.HttpStatusCode.NoContent)
        {
            return default!;
        }

        return await response.Content.ReadFromJsonAsync<T>(JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("The Hub response was empty.");
    }

    private static async Task EnsureSuccess(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var body = await response.Content.ReadAsStringAsync();
        var message = body;
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                message = error.GetString() ?? body;
            }
        }
        catch
        {
            /* keep raw body */
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
            ? $"Hub request failed ({(int)response.StatusCode})."
            : message);
    }

    private static HubSession? LoadSession()
    {
        try
        {
            if (!File.Exists(SessionPath))
            {
                return null;
            }

            return JsonSerializer.Deserialize<HubSession>(File.ReadAllText(SessionPath), JsonOptions);
        }
        catch
        {
            return null;
        }
    }
}
