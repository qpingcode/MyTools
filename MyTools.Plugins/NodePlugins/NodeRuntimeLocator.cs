using System.IO;

namespace MyTools.Plugins.NodePlugins;

/// <summary>
/// Resolves the Node/npm runtime available to MyTools. Installed builds ship a Windows x64
/// development runtime under <c>runtime/node</c>; ordinary plugin hosting prefers bundled Node,
/// while AI development setup may prefer a complete system Node/npm pair and fall back to this runtime.
/// </summary>
public static class NodeRuntimeLocator
{
    public const string PathFallback = "node";
    public const string NpmPathFallback = "npm.cmd";
    public const string BundledRelativePath = @"runtime\node\node.exe";
    public const string BundledNpmRelativePath = @"runtime\node\npm.cmd";

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

    public static string? FindBundledNpm(string? applicationBaseDirectory = null)
    {
        var root = string.IsNullOrWhiteSpace(applicationBaseDirectory)
            ? AppContext.BaseDirectory
            : applicationBaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(root, "runtime", "node", "npm.cmd"));
        var cli = Path.Combine(Path.GetDirectoryName(candidate)!, "node_modules", "npm", "bin", "npm-cli.js");
        return File.Exists(candidate) && File.Exists(cli) ? candidate : null;
    }
}
