namespace MyTools.Host.Core.Security;

/// <summary>
/// Identity of the Node process the host expects to connect. Combines the PID, the process
/// creation time (defends against PID reuse) and the plugin the connection must claim.
/// </summary>
public sealed record ProcessIdentity(
    int Pid,
    DateTime CreationTime,
    string PluginId);
