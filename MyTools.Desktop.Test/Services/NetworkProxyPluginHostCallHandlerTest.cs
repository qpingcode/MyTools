using System.Text.Json;
using MyTools.AI;
using MyTools.Desktop.Services;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class NetworkProxyPluginHostCallHandlerTest
{
    [TestCase("http://127.0.0.1:7890/", "http://127.0.0.1:7890/")]
    [TestCase(null, "")]
    public async Task HandleAsync_ReturnsCurrentProxyUrl(string? proxyUrl, string expected)
    {
        var provider = new StubProxyProvider(proxyUrl is null ? null : new Uri(proxyUrl));
        var handler = new NetworkProxyPluginHostCallHandler(provider);
        var request = new HostCallRequest("network.proxy.read", JsonSerializer.SerializeToElement(new { }));

        var result = await handler.HandleAsync(request, CancellationToken.None);

        Assert.That(result.GetProperty("proxyUrl").GetString(), Is.EqualTo(expected));
    }

    private sealed class StubProxyProvider(Uri? proxyUri) : IPluginCreationProxyProvider
    {
        public PluginCreationProxySettings GetProxySettings() => new(proxyUri, "test");
    }
}
