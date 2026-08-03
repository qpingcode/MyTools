using System.Collections;
using System.Globalization;
using System.Resources;
using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

[TestFixture]
public class HostResourcesTest
{
    private static readonly ResourceManager Resources = new(
        "MyTools.Desktop.Localization.HostStrings",
        typeof(LanguageService).Assembly);

    [Test]
    public void DefaultResource_ShouldBeKeySourceAndTargetLocalesMustNotAddUnknownKeys()
    {
        var defaultKeys = ReadKeys(CultureInfo.InvariantCulture);
        var zhKeys = ReadKeys(CultureInfo.GetCultureInfo("zh-CN"));
        var frKeys = ReadKeys(CultureInfo.GetCultureInfo("fr-FR"));

        Assert.Multiple(() =>
        {
            Assert.That(defaultKeys, Is.Not.Empty);
            Assert.That(zhKeys.Except(defaultKeys), Is.Empty, "zh-CN contains keys absent from the English source.");
            Assert.That(frKeys.Except(defaultKeys), Is.Empty, "fr-FR contains keys absent from the English source.");
            Assert.That(defaultKeys.Except(zhKeys), Is.Empty, "zh-CN resource should currently cover every English key.");
        });
    }

    [Test]
    public void ResourceManager_ShouldResolveExactTranslationAndEnglishFallback()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Resources.GetString("QuickSearch", CultureInfo.GetCultureInfo("zh-CN")), Is.EqualTo("快速搜索"));
            Assert.That(Resources.GetString("QuickSearch", CultureInfo.GetCultureInfo("fr-FR")), Is.EqualTo("Recherche rapide"));
            Assert.That(Resources.GetString("NoResults", CultureInfo.GetCultureInfo("fr-FR")), Is.EqualTo("No Results"));
        });
    }

    private static HashSet<string> ReadKeys(CultureInfo culture)
    {
        var resourceSet = Resources.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new AssertionException($"Missing resource set for {culture.Name}.");
        return resourceSet.Cast<DictionaryEntry>()
            .Select(entry => (string)entry.Key)
            .ToHashSet(StringComparer.Ordinal);
    }
}

