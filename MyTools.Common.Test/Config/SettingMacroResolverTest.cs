using MyTools.Common.Config;
using NUnit.Framework;

namespace MyTools.Common.Test.Config;

[TestFixture]
public class SettingMacroResolverTest
{
    [Test]
    public void Resolve_ShouldReplaceDateTimeNow()
    {
        var now = new DateTime(2026, 8, 19, 12, 30, 0, DateTimeKind.Local);

        var resolved = SettingMacroResolver.Resolve("created ${DateTime.Now}", now);

        Assert.That(resolved, Is.EqualTo($"created {now:O}"));
    }

    [Test]
    public void Resolve_ShouldLeavePlainValuesUnchanged()
    {
        Assert.That(SettingMacroResolver.Resolve("hello"), Is.EqualTo("hello"));
        Assert.That(SettingMacroResolver.Resolve(null), Is.EqualTo(string.Empty));
        Assert.That(SettingMacroResolver.Resolve(""), Is.EqualTo(""));
    }
}
