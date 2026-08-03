using System.Globalization;
using MyTools.Common.Localization;
using NUnit.Framework;

namespace MyTools.Common.Test.Localization;

[TestFixture]
public class LocalizedMessageTest
{
    [Test]
    public void Format_ShouldReplaceNamedPlaceholdersAndPreserveMissingOnes()
    {
        var values = LocalizedMessage.ToDictionary(new { name = "MyTools", count = 2 });

        var result = LocalizedMessage.Format(
            "{{name}} found {{count}} items at {{path}}",
            values,
            CultureInfo.InvariantCulture);

        Assert.That(result, Is.EqualTo("MyTools found 2 items at {{path}}"));
    }

    [Test]
    public void ResultAndActionResult_ShouldKeepDescriptorAndStringCompatibility()
    {
        var message = new LocalizedMessage("Host.Test", "Hello {{name}}", new { name = "World" });

        var result = Result.CreateFailure(message);
        var actionResult = ActionResult.CreateSuccess(message);

        Assert.Multiple(() =>
        {
            Assert.That(result.ErrorMessage, Is.EqualTo("Hello World"));
            Assert.That(result.LocalizedErrorMessage, Is.SameAs(message));
            Assert.That(actionResult.Message, Is.EqualTo("Hello World"));
            Assert.That(actionResult.LocalizedMessage, Is.SameAs(message));
        });
    }
}

