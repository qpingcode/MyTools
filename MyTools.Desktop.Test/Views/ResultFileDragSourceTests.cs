using System.Collections.Specialized;
using System.Windows;
using MyTools.Desktop.Components;
using MyTools.Plugins.Param;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
[Apartment(System.Threading.ApartmentState.STA)]
public class ResultFileDragSourceTests
{
    [Test]
    public void FileAndDirectoryParams_ProduceFileDropData()
    {
        var file = Path.GetTempFileName();
        try
        {
            foreach (var path in new[] { file, Path.GetDirectoryName(file)! })
            {
                var data = ResultFileDragSource.CreateDataObject(ActionStringParam.From(path));
                Assert.That(data!.GetFileDropList().Cast<string>(), Is.EqualTo(new[] { path }));
            }
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public void ClipboardFiles_ExcludeMissingPathsAndDuplicates()
    {
        var file = Path.GetTempFileName();
        var missingFile = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var files = new StringCollection();
            files.AddRange(new[] { file, missingFile, file });
            var clipboard = new DataObject();
            clipboard.SetFileDropList(files);
            var data = ResultFileDragSource.CreateDataObject(new ClipboardParam(clipboard));
            Assert.That(data!.GetFileDropList().Cast<string>(), Is.EqualTo(new[] { file }));
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Test]
    public void PlainTextAndCommands_AreNotFileDrags()
    {
        var clipboard = new DataObject();
        clipboard.SetText(Path.GetTempPath());
        Assert.Multiple(() =>
        {
            Assert.That(ResultFileDragSource.CreateDataObject(new ClipboardParam(clipboard)), Is.Null);
            Assert.That(ResultFileDragSource.CreateDataObject(ActionStringParam.From("echo hello")), Is.Null);
            Assert.That(ResultFileDragSource.CreateDataObject(new ExecuteActionParams(Path.GetTempPath(), "--help")), Is.Null);
        });
    }
}
