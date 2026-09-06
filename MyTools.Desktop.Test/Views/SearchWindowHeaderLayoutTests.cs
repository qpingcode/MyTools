using System.Xml.Linq;
using NUnit.Framework;

namespace MyTools.Desktop.Test.Views;

[TestFixture]
public class SearchWindowHeaderLayoutTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Test]
    public void Header_AlignsQueryWithResultTitleAndAddsVerticalSpacing()
    {
        var document = LoadXaml("MyTools.Desktop", "Views", "SearchWindow.xaml");
        var searchTextBox = document.Descendants(Presentation + "TextBox")
            .Single(element => (string?)element.Attribute(Xaml + "Name") == "SearchTextBox");
        var headerGrid = searchTextBox.Parent!;
        var headerBorder = headerGrid.Parent!;
        var columns = headerGrid.Element(Presentation + "Grid.ColumnDefinitions")!
            .Elements(Presentation + "ColumnDefinition")
            .ToList();
        var dragHandle = headerGrid.Elements(Presentation + "Border").Single();

        Assert.Multiple(() =>
        {
            Assert.That((string?)headerBorder.Attribute("Padding"), Is.EqualTo("15,13,16,17"));
            Assert.That((string?)columns[0].Attribute("Width"), Is.EqualTo("50"));
            Assert.That((string?)searchTextBox.Attribute("Grid.Column"), Is.EqualTo("1"));
            Assert.That((string?)dragHandle.Attribute("MouseLeftButtonDown"),
                Is.EqualTo("SearchDragHandle_MouseLeftButtonDown"));
        });
    }

    [Test]
    public void DetailedResults_UseTheSameIconColumnWidth()
    {
        var document = LoadXaml("MyTools.Desktop", "Views", "Components", "Search", "DetailedListView.xaml");
        var basicList = document.Descendants()
            .Single(element => element.Name.LocalName == "BasicListView");

        Assert.That((string?)basicList.Attribute("IconColumnWidth"), Is.EqualTo("50"));
    }

    private static XDocument LoadXaml(params string[] relativePath)
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory != null && !File.Exists(Path.Combine(directory.FullName, "MyTools.sln")))
        {
            directory = directory.Parent;
        }

        Assert.That(directory, Is.Not.Null, "Could not locate the MyTools repository root.");
        return XDocument.Load(Path.Combine(new[] { directory!.FullName }.Concat(relativePath).ToArray()));
    }
}
