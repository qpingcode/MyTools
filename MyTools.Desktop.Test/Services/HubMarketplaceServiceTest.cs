using MyTools.Desktop.Services;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Services;

public sealed class HubMarketplaceServiceTest
{
    [TestCase("1.0.0", "0.9.9")]
    [TestCase("1.0.0", "1.0.0-beta.2")]
    [TestCase("1.0.0-beta.10", "1.0.0-beta.2")]
    public void SemanticVersion_ShouldOrderHigherVersions(string candidateText, string publishedText)
    {
        Assert.That(SemanticVersion.TryParse(candidateText, out var candidate), Is.True);
        Assert.That(SemanticVersion.TryParse(publishedText, out var published), Is.True);
        Assert.That(candidate.CompareTo(published), Is.GreaterThan(0));
    }

    [TestCase("1")]
    [TestCase("1.0")]
    [TestCase("01.0.0")]
    [TestCase("latest")]
    public void SemanticVersion_ShouldRejectInvalidValues(string value)
    {
        Assert.That(SemanticVersion.TryParse(value, out _), Is.False);
    }
}
