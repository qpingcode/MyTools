using NUnit.Framework;
using MyTools.Plugins.NodePlugins;

namespace MyTools.Plugins.Test.NodePlugins;

[TestFixture]
public class NodeRuntimeLocatorTest
{
    [Test]
    public void Resolve_ShouldPreferBundledNodeExe()
    {
        var root = Path.Combine(Path.GetTempPath(), "mytools-node-runtime-" + Guid.NewGuid().ToString("N"));
        var bundled = Path.Combine(root, "runtime", "node");
        Directory.CreateDirectory(bundled);
        var exe = Path.Combine(bundled, "node.exe");
        File.WriteAllText(exe, "stub");

        try
        {
            Assert.That(NodeRuntimeLocator.FindBundled(root), Is.EqualTo(Path.GetFullPath(exe)));
            Assert.That(NodeRuntimeLocator.Resolve(root), Is.EqualTo(Path.GetFullPath(exe)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Test]
    public void Resolve_ShouldFallBackToPathWhenBundledRuntimeIsMissing()
    {
        var root = Path.Combine(Path.GetTempPath(), "mytools-node-runtime-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            Assert.That(NodeRuntimeLocator.FindBundled(root), Is.Null);
            Assert.That(NodeRuntimeLocator.Resolve(root), Is.EqualTo(NodeRuntimeLocator.PathFallback));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
