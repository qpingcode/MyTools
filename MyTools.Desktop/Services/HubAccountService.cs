using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Serialization;

namespace MyTools.Desktop.Services;

public sealed class HubAccountService(HubApiClient client)
{
    public async Task<HubAccountStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        try
        {
            var providers = await client.GetAsync<HubProvidersDto>("/api/auth/providers", authenticate: false, cancellationToken);
            return new HubAccountStatus
            {
                SignedIn = client.IsSignedIn,
                Username = client.Session?.Username,
                HubUrl = client.HubUrl,
                Google = providers.Google,
                Microsoft = providers.Microsoft
            };
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException
                                              {
                                                  SocketErrorCode: SocketError.ConnectionRefused
                                              })
        {
            // Hub service offline: keep settings available and show signed-out state.
            return new HubAccountStatus
            {
                SignedIn = false,
                Username = null,
                HubUrl = client.HubUrl,
                Google = false,
                Microsoft = false
            };
        }
    }

    public async Task<HubAccountStatus> LoginAsync(string username, string password, CancellationToken cancellationToken)
    {
        var response = await client.SendJsonAsync<HubAuthDto>(
            HttpMethod.Post, "/api/auth/login", new { username, password }, authenticate: false, cancellationToken);
        Store(response);
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<HubAccountStatus> RegisterAsync(string username, string password, CancellationToken cancellationToken)
    {
        var response = await client.SendJsonAsync<HubAuthDto>(
            HttpMethod.Post, "/api/auth/register", new { username, password }, authenticate: false, cancellationToken);
        Store(response);
        return await GetStatusAsync(cancellationToken);
    }

    public async Task<HubAccountStatus> LoginWithExternalAsync(string provider, CancellationToken cancellationToken)
    {
        var port = GetFreePort();
        var prefix = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener();
        listener.Prefixes.Add(prefix);
        listener.Start();
        var redirect = prefix + "callback";
        var url = $"{client.HubUrl}/api/auth/external/{Uri.EscapeDataString(provider)}?desktopRedirect={Uri.EscapeDataString(redirect)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        var context = await listener.GetContextAsync().WaitAsync(cancellationToken);
        var token = context.Request.QueryString["token"];
        var html = "<!doctype html><html><body style=\"font-family:sans-serif;padding:24px\">You can close this window and return to MyTools.</body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.ContentType = "text/html; charset=utf-8";
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes, cancellationToken);
        context.Response.Close();
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Sign-in did not return an access token.");
        }

        client.SetSession(new HubSession { HubUrl = client.HubUrl, AccessToken = token, Username = "", UserId = "" });
        var me = await client.GetAsync<HubAuthDto>("/api/auth/me", authenticate: true, cancellationToken);
        Store(me);
        return await GetStatusAsync(cancellationToken);
    }

    public HubAccountStatus Logout()
    {
        client.ClearSession();
        return new HubAccountStatus
        {
            SignedIn = false,
            HubUrl = client.HubUrl
        };
    }

    private void Store(HubAuthDto response)
    {
        client.SetSession(new HubSession
        {
            HubUrl = client.HubUrl,
            AccessToken = response.Token,
            Username = response.User.Username,
            UserId = response.User.Id
        });
    }

    private static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    private sealed class HubProvidersDto
    {
        public bool Google { get; set; }
        public bool Microsoft { get; set; }
    }

    private sealed class HubAuthDto
    {
        public string Token { get; set; } = "";
        public HubUserDto User { get; set; } = new();
    }

    private sealed class HubUserDto
    {
        public string Id { get; set; } = "";
        public string Username { get; set; } = "";
        [JsonPropertyName("email")]
        public string? Email { get; set; }
    }
}
