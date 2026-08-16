using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class WindowSystemMenuFilterTests
{
    [Test]
    public void ShouldSuppress_AltSpaceSysCommand_WhenCapturing()
    {
        var suppress = WindowSystemMenuFilter.ShouldSuppress(
            WindowSystemMenuFilter.WmSysCommand,
            new IntPtr(WindowSystemMenuFilter.ScKeyMenu),
            capturing: true);

        Assert.That(suppress, Is.True);
    }

    [Test]
    public void ShouldSuppress_SysKeyDownSpace_WhenCapturing()
    {
        var suppress = WindowSystemMenuFilter.ShouldSuppress(
            WindowSystemMenuFilter.WmSysKeyDown,
            new IntPtr(WindowSystemMenuFilter.VkSpace),
            capturing: true);

        Assert.That(suppress, Is.True);
    }

    [Test]
    public void ShouldNotSuppress_WhenNotCapturing()
    {
        var suppress = WindowSystemMenuFilter.ShouldSuppress(
            WindowSystemMenuFilter.WmSysCommand,
            new IntPtr(WindowSystemMenuFilter.ScKeyMenu),
            capturing: false);

        Assert.That(suppress, Is.False);
    }

    [Test]
    public void ShouldNotSuppress_UnrelatedSysCommand_WhenCapturing()
    {
        const int scClose = 0xF060;
        var suppress = WindowSystemMenuFilter.ShouldSuppress(
            WindowSystemMenuFilter.WmSysCommand,
            new IntPtr(scClose),
            capturing: true);

        Assert.That(suppress, Is.False);
    }
}
