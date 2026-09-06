using MyTools.Desktop.Utils;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Utils;

[TestFixture]
public class ProcessIntegrityLevelTests
{
    [Test]
    public void Read_CurrentProcessReturnsKnownIntegrityLevel()
    {
        var level = ProcessIntegrityLevel.Read((uint)Environment.ProcessId);

        Assert.That(level, Is.AnyOf("untrusted", "low", "medium", "medium-plus", "high", "system", "protected"));
    }

    [TestCase(0x0000u, "untrusted")]
    [TestCase(0x1000u, "low")]
    [TestCase(0x2000u, "medium")]
    [TestCase(0x2100u, "medium-plus")]
    [TestCase(0x3000u, "high")]
    [TestCase(0x4000u, "system")]
    [TestCase(0x5000u, "protected")]
    public void DescribeRid_ReturnsExpectedIntegrityBand(uint rid, string expected)
    {
        Assert.That(ProcessIntegrityLevel.DescribeRid(rid), Is.EqualTo(expected));
    }
}
