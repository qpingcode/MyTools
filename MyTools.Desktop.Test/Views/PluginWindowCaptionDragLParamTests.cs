using MyTools.Desktop.Views;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class PluginWindowCaptionDragLParamTests
{
    [Test]
    public void PackScreenCoordinates_WhenCoordinatesArePositive_PreservesExactWin32BitLayout()
    {
        var packed = PluginWindowCaptionDragLParam.PackScreenCoordinates(x: 400, y: 300);

        Assert.Multiple(() =>
        {
            Assert.That(unchecked((uint)packed), Is.EqualTo(0x012C0190u));
            Assert.That(unchecked((short)packed), Is.EqualTo(400));
            Assert.That(unchecked((short)(packed >> 16)), Is.EqualTo(300));
        });
    }

    [Test]
    public void PackScreenCoordinates_WhenCoordinatesAreNegative_PreservesSignedSecondaryMonitorCoordinates()
    {
        var packed = PluginWindowCaptionDragLParam.PackScreenCoordinates(x: -1600, y: -200);

        Assert.Multiple(() =>
        {
            Assert.That(unchecked((uint)packed), Is.EqualTo(0xFF38F9C0u));
            Assert.That(unchecked((short)packed), Is.EqualTo(-1600));
            Assert.That(unchecked((short)(packed >> 16)), Is.EqualTo(-200));
        });
    }
}
