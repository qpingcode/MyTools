using System.Net;
using System.Net.Http;
using System.Text;
using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public sealed class HostCityServiceTest
{
    [Test]
    public async Task GetCityAsyncReturnsOnlyCityLevelDataAndCachesResult()
    {
        var handler = new StubHandler("""
            {
              "success": true,
              "ip": "203.0.113.10",
              "city": "Shanghai",
              "region": "Shanghai",
              "country": "China",
              "country_code": "CN",
              "latitude": 31.2,
              "longitude": 121.5
            }
            """);
        using var client = new HttpClient(handler);
        using var service = new HostCityService(client);

        var first = await service.GetCityAsync();
        var second = await service.GetCityAsync();

        Assert.Multiple(() =>
        {
            Assert.That(first.Available, Is.True);
            Assert.That(first.City, Is.EqualTo("Shanghai"));
            Assert.That(first.Region, Is.EqualTo("Shanghai"));
            Assert.That(first.CountryCode, Is.EqualTo("CN"));
            Assert.That(first.Approximate, Is.True);
            Assert.That(first.Source, Is.EqualTo("public-ip"));
            Assert.That(first, Is.SameAs(second));
            Assert.That(handler.RequestCount, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task GetCityAsyncReturnsUnavailableWhenProviderHasNoCity()
    {
        using var client = new HttpClient(new StubHandler("{\"success\":false,\"message\":\"lookup unavailable\"}"));
        using var service = new HostCityService(client);

        var result = await service.GetCityAsync();

        Assert.Multiple(() =>
        {
            Assert.That(result.Available, Is.False);
            Assert.That(result.City, Is.Null);
            Assert.That(result.Error, Is.EqualTo("lookup unavailable"));
        });
    }

    private sealed class StubHandler(string json) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
