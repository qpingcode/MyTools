using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
using System.Net;
using MyTools.Common.Config.Interfaces;
using MyTools.Common.Config.Models;
using MyTools.Desktop.Serializers;
using MyTools.Desktop.Services;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class UpdateServiceTests
{
    [Test]
    public void DefaultUpdateUrl_UsesMyToolsGithubReleases()
    {
        Assert.That(UpdateService.DefaultUpdateUrl, Is.EqualTo("https://github.com/qpingcode/MyTools/releases"));
    }

    [TestCase("https://github.com/qpingcode/MyTools", "https://github.com/qpingcode/MyTools")]
    [TestCase("https://github.com/qpingcode/MyTools/", "https://github.com/qpingcode/MyTools")]
    [TestCase("https://github.com/qpingcode/MyTools/releases", "https://github.com/qpingcode/MyTools")]
    [TestCase("https://GITHUB.COM/qpingcode/MyTools/RELEASES/", "https://github.com/qpingcode/MyTools")]
    [TestCase("https://downloads.qping.me/mytools/stable/win-x64/", null)]
    [TestCase("C:\\Updates\\MyTools", null)]
    public void GetGithubRepositoryUrl_NormalizesSupportedUrls(string updateUrl, string? expected)
    {
        Assert.That(UpdateService.GetGithubRepositoryUrl(updateUrl), Is.EqualTo(expected));
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("   ")]
    public void ParseProxyUri_WhenProxyIsEmpty_ReturnsNull(string? proxyUrl)
    {
        Assert.That(UpdateService.ParseProxyUri(proxyUrl), Is.Null);
    }

    [TestCase("http://127.0.0.1:7890", "http://127.0.0.1:7890/")]
    [TestCase("  https://proxy.example.com:8443  ", "https://proxy.example.com:8443/")]
    [TestCase("socks4://localhost:1080", "socks4://localhost:1080/")]
    [TestCase("socks4a://localhost:1080", "socks4a://localhost:1080/")]
    [TestCase("socks5://localhost:1080", "socks5://localhost:1080/")]
    public void ParseProxyUri_WhenProxyIsValid_ReturnsNormalizedUri(string proxyUrl, string expected)
    {
        Assert.That(UpdateService.ParseProxyUri(proxyUrl)?.AbsoluteUri, Is.EqualTo(expected));
    }

    [TestCase("localhost:7890")]
    [TestCase("ftp://proxy.example.com:21")]
    [TestCase("http://")]
    public void ParseProxyUri_WhenProxyIsInvalid_Throws(string proxyUrl)
    {
        Assert.That(
            () => UpdateService.ParseProxyUri(proxyUrl),
            Throws.TypeOf<InvalidOperationException>());
    }

    [TestCase("http://user@proxy.example.com:7890")]
    [TestCase("http://user:password@proxy.example.com:7890")]
    public void ParseProxyUri_WhenProxyContainsCredentials_Throws(string proxyUrl)
    {
        Assert.That(
            () => UpdateService.ParseProxyUri(proxyUrl),
            Throws.TypeOf<InvalidOperationException>()
                .With.Message.EqualTo("The update proxy URL must not contain a username or password."));
    }

    [Test]
    public void UpdateProxyFileDownloader_ConfiguresProxyWithoutCredentials()
    {
        var proxyUri = new Uri("http://127.0.0.1:7890");
        var downloader = new TestableUpdateProxyFileDownloader(proxyUri);

        using var handler = downloader.CreateHandler();

        Assert.Multiple(() =>
        {
            Assert.That(handler.UseProxy, Is.True);
            Assert.That(handler.Proxy, Is.TypeOf<WebProxy>());
            Assert.That(((WebProxy)handler.Proxy!).Address, Is.EqualTo(proxyUri));
            Assert.That(handler.Proxy!.Credentials, Is.Null);
        });
    }

    [Test]
    public void UpdateProxyFileDownloader_WithoutProxy_DisablesProxy()
    {
        var downloader = new TestableUpdateProxyFileDownloader(null);

        using var handler = downloader.CreateHandler();

        Assert.That(handler.UseProxy, Is.False);
    }

    [Test]
    public async Task CheckForUpdatesAsync_WhenUpdateUrlIsMissing_ReturnsNotConfigured()
    {
        var registry = new Mock<IConfigurationRegistry>();
        var service = CreateService(registry);

        var result = await service.CheckForUpdatesAsync();

        Assert.That(result.Status, Is.EqualTo(UpdateCheckStatus.NotConfigured));
    }

    [Test]
    public async Task CheckForUpdatesAsync_WhenRunningOutsideVelopack_ReturnsNotInstalled()
    {
        var registry = new Mock<IConfigurationRegistry>();
        registry.Setup(x => x.FindSetting("General.UpdateUrl"))
            .Returns(CreateStringSetting(TestContext.CurrentContext.WorkDirectory));
        registry.Setup(x => x.FindSetting("General.UpdateChannel"))
            .Returns(CreateStringSetting("win"));
        var service = CreateService(registry);

        var result = await service.CheckForUpdatesAsync();

        Assert.That(result.Status, Is.EqualTo(UpdateCheckStatus.NotInstalled));
    }

    [Test]
    public void DownloadAndPrepareUpdateAsync_WithoutAvailableUpdate_Throws()
    {
        var registry = new Mock<IConfigurationRegistry>();
        var service = CreateService(registry);

        Assert.That(
            async () => await service.DownloadAndPrepareUpdateAsync(),
            Throws.TypeOf<InvalidOperationException>());
    }

    private static UpdateService CreateService(Mock<IConfigurationRegistry> registry)
    {
        return new UpdateService(registry.Object, Mock.Of<ILogger<UpdateService>>());
    }

    private static ConfigurationSetting CreateStringSetting(string value)
    {
        var setting = new ConfigurationSetting
        {
            Name = "Test",
            Serializer = new StringSerializer()
        };
        setting.InitValueWithoutNotify(value);
        return setting;
    }

    private sealed class TestableUpdateProxyFileDownloader(Uri? proxyUri) : UpdateProxyFileDownloader(proxyUri)
    {
        public HttpClientHandler CreateHandler()
        {
            return CreateHttpClientHandler();
        }
    }
}


