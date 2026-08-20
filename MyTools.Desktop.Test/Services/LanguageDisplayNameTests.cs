using NUnit.Framework;
using MyTools.Desktop.Services;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class LanguageDisplayNameTests
{
    [TestCase("zh-CN", "中文")]
    [TestCase("en-US", "English")]
    [TestCase("fr-FR", "Français")]
    public void GetNativeDisplayName_UsesTheLanguagesOwnName(string locale, string expected)
    {
        Assert.That(LanguageService.GetNativeDisplayName(locale), Is.EqualTo(expected));
    }
}
