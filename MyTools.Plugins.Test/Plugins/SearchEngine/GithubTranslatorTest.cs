using MyTools.Plugins.Translator;
using NUnit.Framework;

namespace MyTools.Plugins.Test.SearchEngine;

public class GithubTranslatorTest
{
    [Test]
    public void TestTranslate()
    {
        var testCases = new[]
        {
            new { Input = "ext:cs", Expected = "path:*.cs" },
            new { Input = "file:*test*me*", Expected = "path:**/*test*me*" },
            new { Input = "file:test", Expected = "path:**/test.*" },
            new { Input = "path:*test*me*", Expected = "path:/.*test.*me.*/" },
            new { Input = "notest", Expected = " NOT path:/.*test.*/" },
            new { Input = "ext:cs notest file:*file1* path:*path1* NOT file:*file2*", Expected = "path:*.cs  path:**/*file1* path:/.*path1.*/ NOT path:**/*file2* NOT path:/.*test.*/" }
        };

        foreach (var testCase in testCases)
        {
            var result = GithubTranslator.Translate(testCase.Input);
            Assert.That(result, Is.EqualTo(testCase.Expected));
        }
    }
}