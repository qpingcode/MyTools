using System.Collections.Generic;
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

    [Test]
    public void EvaluateVisibility_EmptyOrMissingMacro_ShouldBeVisible()
    {
        var values = new Dictionary<string, object?>();
        Assert.That(SettingMacroResolver.EvaluateVisibility(null, values), Is.True);
        Assert.That(SettingMacroResolver.EvaluateVisibility("", values), Is.True);
        Assert.That(SettingMacroResolver.EvaluateVisibility("ChromeEnabled == true", values), Is.True);
    }

    [Test]
    public void EvaluateVisibility_ShouldCompareSiblingBools()
    {
        var values = new Dictionary<string, object?> { ["ChromeEnabled"] = true };

        Assert.That(SettingMacroResolver.EvaluateVisibility("${ChromeEnabled == true}", values), Is.True);
        Assert.That(SettingMacroResolver.EvaluateVisibility("${ChromeEnabled == false}", values), Is.False);
        Assert.That(SettingMacroResolver.EvaluateVisibility("${ChromeEnabled}", values), Is.True);

        values["ChromeEnabled"] = "False";
        Assert.That(SettingMacroResolver.EvaluateVisibility("${ChromeEnabled == true}", values), Is.False);
        Assert.That(SettingMacroResolver.EvaluateVisibility("${ChromeEnabled == false}", values), Is.True);
    }

    [Test]
    public void EvaluateVisibility_ShouldCombineAndOr()
    {
        var values = new Dictionary<string, object?>
        {
            ["ChromeEnabled"] = true,
            ["EdgeEnabled"] = false
        };

        Assert.That(
            SettingMacroResolver.EvaluateVisibility("${ChromeEnabled == true && EdgeEnabled == true}", values),
            Is.False);
        Assert.That(
            SettingMacroResolver.EvaluateVisibility("${ChromeEnabled == true || EdgeEnabled == true}", values),
            Is.True);
        Assert.That(
            SettingMacroResolver.EvaluateVisibility("${(ChromeEnabled == true || EdgeEnabled == true) && ChromeEnabled}", values),
            Is.True);
    }

    [Test]
    public void EvaluateVisibility_ShouldCompareStringsAndMissingKeys()
    {
        var values = new Dictionary<string, object?> { ["ChromeProfile"] = "Default" };

        Assert.That(SettingMacroResolver.EvaluateVisibility("${ChromeProfile == \"Default\"}", values), Is.True);
        Assert.That(SettingMacroResolver.EvaluateVisibility("${ChromeProfile != \"Guest\"}", values), Is.True);
        Assert.That(SettingMacroResolver.EvaluateVisibility("${UnknownKey == true}", values), Is.False);
    }
}
