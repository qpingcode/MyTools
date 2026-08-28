using System.Net;
using System.Text.RegularExpressions;

namespace MyTools.AI;

internal sealed class AgentWebTools(HttpClient httpClient, Action<string, string?>? report = null)
{
    public async Task<string> SearchWebAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query)) return "Search query is empty.";
        var uri = new Uri("https://html.duckduckgo.com/html/?q=" + Uri.EscapeDataString(query.Trim()));
        report?.Invoke("searchingWeb", uri.ToString());
        var html = await httpClient.GetStringAsync(uri, cancellationToken);
        return ToPlainText(html, 12000);
    }

    public async Task<string> FetchUrlAsync(string url, CancellationToken cancellationToken = default)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            return "Only absolute HTTPS URLs are supported.";
        if (uri.IsLoopback || await ResolvesToPrivateAddress(uri.Host, cancellationToken))
            return "Local and private network URLs are not allowed.";
        report?.Invoke("fetchingUrl", uri.ToString());
        var content = await httpClient.GetStringAsync(uri, cancellationToken);
        return ToPlainText(content, 20000);
    }

    private static async Task<bool> ResolvesToPrivateAddress(string host, CancellationToken cancellationToken)
    {
        try
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            return addresses.Length == 0 || addresses.Any(IsPrivateAddress);
        }
        catch { return true; }
    }

    private static bool IsPrivateAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return true;
        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            return bytes[0] == 10 || bytes[0] == 127 || (bytes[0] == 169 && bytes[1] == 254)
                || (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) || (bytes[0] == 192 && bytes[1] == 168);
        return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.Equals(IPAddress.IPv6Loopback);
    }

    private static string ToPlainText(string content, int limit)
    {
        var text = Regex.Replace(content, "<script[\\s\\S]*?</script>|<style[\\s\\S]*?</style>", " ", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, "<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        text = Regex.Replace(text, "\\s+", " ").Trim();
        return text.Length <= limit ? text : text[..limit];
    }
}
