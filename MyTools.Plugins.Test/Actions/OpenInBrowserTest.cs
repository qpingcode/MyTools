using NUnit.Framework;

namespace MyTools.Plugins.Test.Actions;

public class OpenInBrowserTest
{
    [Test]
    public void CreateProcessStartInfo_ChromeExtensionUrl_StartsChromeDirectly()
    {
        const string chromePath = @"C:\Users\Test\AppData\Local\Google\Chrome\Application\chrome.exe";
        const string url = "chrome-extension://hkedbapjpblbodpgbajblpnlpenaebaa/index.html#/cluster/0";

        var startInfo = OpenInBrowser.CreateProcessStartInfo(
            url,
            folder => folder == Environment.SpecialFolder.LocalApplicationData
                ? @"C:\Users\Test\AppData\Local"
                : string.Empty,
            path => path == chromePath);

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.FileName, Is.EqualTo(chromePath));
            Assert.That(startInfo.ArgumentList, Is.EqualTo(new[] { url }));
            Assert.That(startInfo.UseShellExecute, Is.False);
        });
    }

    [Test]
    public void CreateProcessStartInfo_HttpsUrl_UsesWindowsShell()
    {
        const string url = "https://example.com/";

        var startInfo = OpenInBrowser.CreateProcessStartInfo(
            url,
            _ => string.Empty,
            _ => false);

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.FileName, Is.EqualTo(url));
            Assert.That(startInfo.ArgumentList, Is.Empty);
            Assert.That(startInfo.UseShellExecute, Is.True);
        });
    }

    [Test]
    public void CreateProcessStartInfo_ChromeExtensionUrlWithoutChrome_Throws()
    {
        const string url = "chrome-extension://hkedbapjpblbodpgbajblpnlpenaebaa/index.html";

        var exception = Assert.Throws<FileNotFoundException>(() =>
            OpenInBrowser.CreateProcessStartInfo(url, _ => string.Empty, _ => false));

        Assert.That(exception!.Message, Does.Contain("Google Chrome"));
    }
}
