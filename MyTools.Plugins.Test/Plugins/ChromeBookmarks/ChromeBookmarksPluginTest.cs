using MyTools.Common;
using MyTools.Plugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.Plugins.ChromeBookmarks;

public class ChromeBookmarksPluginTest
{
    [Test]
    public void Reader_ShouldFlattenNestedBookmarks()
    {
        var filePath = CreateBookmarksFile();
        var reader = new ChromeBookmarkReader();

        var bookmarks = reader.ReadBookmarks(filePath);

        Assert.That(bookmarks.Select(x => x.Title), Is.EquivalentTo(new[] { "GitLab", "SharpLab", "GitHub" }));
        Assert.That(bookmarks.Single(x => x.Title == "SharpLab").FolderPath, Is.EqualTo("Bar/Dev"));
    }

    [Test]
    public async Task SearchAsync_ShouldReturnMatchingBookmarks()
    {
        var filePath = CreateBookmarksFile();
      var plugin = new ChromeBookmarksPlugin(filePath, new ChromeBookmarkReader());
        await plugin.InitializeAsync();

        var result = await plugin.SearchAsync("git", CancellationToken.None);

        Assert.That(result.Success, Is.True);
        Assert.That(result.Items.Select(x => x.Title), Is.EquivalentTo(new[] { "GitLab", "GitHub" }));
    }

    [Test]
    public async Task SearchAsync_ShouldReloadBookmarksWhenFileChanges()
    {
      var filePath = CreateBookmarksFile();
      var plugin = new ChromeBookmarksPlugin(filePath, new ChromeBookmarkReader());
      await plugin.InitializeAsync();

      await plugin.SearchAsync("git", CancellationToken.None);
      UpdateBookmarksFile(filePath);

      var result = await plugin.SearchAsync("new", CancellationToken.None);

      Assert.That(result.Success, Is.True);
      Assert.That(result.Items.Select(x => x.Title), Is.EquivalentTo(new[] { "New Bookmark" }));
    }

    private static string CreateBookmarksFile()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"chrome-bookmarks-{Guid.NewGuid():N}.json");
        File.WriteAllText(filePath, """
        {
          "roots": {
            "bookmark_bar": {
              "children": [
                {
                  "type": "url",
                  "name": "GitLab",
                  "url": "http://git.qping.me/"
                },
                {
                  "type": "folder",
                  "name": "Dev",
                  "children": [
                    {
                      "type": "url",
                      "name": "SharpLab",
                      "url": "https://sharplab.io/"
                    }
                  ]
                }
              ]
            },
            "other": {
              "children": [
                {
                  "type": "url",
                  "name": "GitHub",
                  "url": "https://github.com/"
                }
              ]
            }
          }
        }
        """);
        return filePath;
    }

    private static void UpdateBookmarksFile(string filePath)
    {
        Thread.Sleep(1100);
        File.WriteAllText(filePath, """
        {
          "roots": {
            "bookmark_bar": {
              "children": [
                {
                  "type": "url",
                  "name": "New Bookmark",
                  "url": "https://example.com/"
                }
              ]
            }
          }
        }
        """);
    }
}