using System.IO;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// Resolves the Node executable used to host plugins. Installed builds ship a Windows x64
/// runtime at <c>runtime/node/node.exe</c>; development builds fall back to PATH <c>node</c>.
/// </summary>
public static class NodeRuntimeLocator
{
    public const string PathFallback = "node";
    public const string BundledRelativePath = @"runtime\node\node.exe";

    public static string Resolve(string? applicationBaseDirectory = null)
    {
        return FindBundled(applicationBaseDirectory) ?? PathFallback;
    }

    public static string? FindBundled(string? applicationBaseDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : applicationBaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(root, "runtime", "node", "node.exe"));
        return File.Exists(candidate) ? candidate : null;
    }
}
