using System.IO;
using System.Linq;
using NUnit.Framework;

namespace MyTools.Host.Core.Test.Decoupling;

/// <summary>
/// Architectural decoupling gate: the v3 message-bus stack must not reference any of the legacy
/// stdio JSON-RPC types. This guarantees that when the legacy code is removed (design 非目标第8条:
/// 旧 stdio 协议整体替换), the v3 layer is unaffected. The gate scans the v3 source files for any
/// reference to the legacy type/namespace names.
/// </summary>
[TestFixture]
public class V3StackDoesNotReferenceStdioTest
{
    // The v3 modules that form the new stack.
    private static readonly string[] V3Projects =
    [
        "MyTools.Protocol",
        "MyTools.Host.Core",
        "MyTools.Host.Transports",
    ];

    // Legacy identifiers that must never appear in v3 source.
    private static readonly string[] LegacyIdentifiers =
    [
        "NodePluginProcessHost",
        "NodePluginProtocol",   // legacy DTO namespace
        "HostCallProtocol",      // legacy host-call DTO
        "JsonRpc",               // legacy JSON-RPC framing
        "tool-call",             // legacy WebView page protocol
        "MyTools.Plugins.NodePlugins", // legacy namespace
    ];

    [Test]
    public void V3Source_ShouldNotReferenceAnyLegacyStdioIdentifier()
    {
        var repoRoot = FindRepoRoot();
        var violations = new System.Collections.Generic.List<string>();

        foreach (var project in V3Projects)
        {
            var dir = Path.Combine(repoRoot, project);
            if (!Directory.Exists(dir)) continue;

            foreach (var cs in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(cs);
                foreach (var id in LegacyIdentifiers)
                {
                    if (text.Contains(id))
                    {
                        violations.Add($"{Path.GetRelativePath(repoRoot, cs)} references '{id}'");
                    }
                }
            }
        }

        Assert.That(violations, Is.Empty,
            "v3 stack must not reference legacy stdio types. Violations:\n" +
            string.Join("\n", violations));
    }

    private static string FindRepoRoot()
    {
        var dir = TestContext.CurrentContext.TestDirectory;
        for (var i = 0; i < 6 && dir is not null; i++)
        {
            if (File.Exists(Path.Combine(dir, "MyTools.sln"))) return dir;
            dir = Path.GetDirectoryName(dir);
        }
        return TestContext.CurrentContext.TestDirectory;
    }
}
