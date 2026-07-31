using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;
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
}


