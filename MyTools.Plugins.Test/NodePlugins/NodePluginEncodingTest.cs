using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using MyTools.Plugins.NodePlugins;
using NUnit.Framework;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class NodePluginEncodingTest
{
    private string rootPath = null!;

    [SetUp]
    public void SetUp()
    {
        rootPath = Path.Combine(Path.GetTempPath(), $"mytools-node-plugin-encoding-{Guid.NewGuid():N}");
        Directory.CreateDirectory(rootPath);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, true);
        }
    }

    [Test]
    public async Task SearchAsync_ShouldPreserveUtf8TextFromNodeStdout()
    {
        var entryPath = Path.Combine(rootPath, "index.mjs");
        await File.WriteAllTextAsync(entryPath, """
        import readline from "node:readline";

        const rl = readline.createInterface({
          input: process.stdin,
          crlfDelay: Infinity,
        });

        rl.on("line", (line) => {
          const message = JSON.parse(line);
          process.stdout.write(JSON.stringify({
            jsonrpc: "2.0",
            id: message.id,
            result: {
              items: [{
                id: "utf8",
                title: "翻译：" + message.params.query,
                subtitle: "中文结果",
                priority: 100,
                actions: []
              }]
            }
          }) + "\n");
        });
        """, Encoding.UTF8);

        var manifest = new NodePluginManifest
        {
            Id = "utf8-test",
            Name = "UTF-8 Test",
            Version = "0.1.0",
            Runtime = "node",
            Entry = "index.mjs",
            ProtocolVersion = "2.0",
            PluginDirectory = rootPath,
            EntryFullPath = entryPath,
            Keywords = ["utf8"]
        };
        var factory = new NodePluginFactory(NullLoggerFactory.Instance);
        using (var plugin = factory.CreatePlugins([manifest]).Single())
        {
            var result = await plugin.SearchAsync("你好", CancellationToken.None);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
            var item = result.Items.Single();
            Assert.That(item.Title, Is.EqualTo("翻译：你好"));
            Assert.That(item.SubTitle, Is.EqualTo("中文结果"));
        }

        Assert.That(() => Directory.Delete(rootPath, true), Throws.Nothing);
    }
}
