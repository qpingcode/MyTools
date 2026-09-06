using NUnit.Framework;

namespace MyTools.Plugins.Test.Actions;

public class OpenFileTest
{
    [Test]
    public void CreateStartInfo_UsesWindowsShellDefaultApplication()
    {
        const string path = @"C:\Test\Documents\notes.txt";

        var startInfo = OpenFile.CreateStartInfo(path);

        Assert.Multiple(() =>
        {
            Assert.That(startInfo.FileName, Is.EqualTo(path));
            Assert.That(startInfo.UseShellExecute, Is.True);
            Assert.That(startInfo.Verb, Is.EqualTo("open"));
            Assert.That(startInfo.WorkingDirectory, Is.EqualTo(@"C:\Test\Documents"));
        });
    }
}
