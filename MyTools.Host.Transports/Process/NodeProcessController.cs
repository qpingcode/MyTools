using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using MyTools.Host.Core.Security;
using MyTools.Host.Core.Sessions;
using MyTools.Host.Core.Transports;
using MyTools.Host.Transports.NamedPipe;

namespace MyTools.Host.Transports.Process;

/// <summary>
/// Real <see cref="INodeProcessController"/>: spawns the Node child process, writes the bootstrap
/// token as the first stdin line, owns the server side of the named pipe via a
/// <see cref="NamedPipeTransport"/>, and assigns the process to a <see cref="ProcessTreeJob"/> so
/// the whole tree is reclaimed on stop/host-exit. stdout/stderr carry logs only.
///
/// The Node SDK entry reads the token from stdin, connects to the pipe at the given path, and
/// speaks the v3 protocol. This controller is agnostic to the plugin's business logic.
/// </summary>
public sealed class NodeProcessController : INodeProcessController
{
    private readonly string _nodeExePath;
    private readonly string _nodeEntryFullPath;
    private System.Diagnostics.Process? _process;
    private ProcessTreeJob? _job;
    private NamedPipeTransport? _transport;

    public NodeProcessController(string nodeExePath, string nodeEntryFullPath)
    {
        _nodeExePath = nodeExePath;
        _nodeEntryFullPath = nodeEntryFullPath;
    }

    public IMessageTransport? Transport => _transport;

    public ProcessIdentity? ObservedIdentity { get; private set; }

    public async Task StartAsync(
        string pipeName,
        string pluginId,
        string entryId,
        Func<ProcessIdentity, string> issueToken,
        CancellationToken cancellationToken)
    {
        var pipePath = @"\\.\pipe\" + pipeName;
        _transport = new NamedPipeTransport(pipeName, isServer: true);
        var connectTask = _transport.ConnectAsync(cancellationToken);

        _job = new ProcessTreeJob();

        var psi = new ProcessStartInfo
        {
            FileName = _nodeExePath,
            Arguments = $"\"{_nodeEntryFullPath}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        // Log stderr for debugging Node startup failures.
        psi.Environment["MYTOOLS_V3"] = "1";

        _process = System.Diagnostics.Process.Start(psi)
            ?? throw new System.Exception($"failed to start node: {_nodeExePath}");

        _job.Assign(_process);

        // Capture stderr/stdout for debugging. CRITICAL: both stdout and stderr must be drained
        // asynchronously — if either buffer fills (OS pipe buffer ~4KB), the Node process blocks on
        // its next write, which in turn blocks it from reading stdin, causing WriteLineAsync to hang.
        _process.ErrorDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                System.Console.Error.WriteLine($"[node-stderr] {e.Data}");
        };
        _process.OutputDataReceived += (sender, e) =>
        {
            if (!string.IsNullOrEmpty(e.Data))
                System.Console.Out.WriteLine($"[node-stdout] {e.Data}");
        };
        _process.BeginErrorReadLine();
        _process.BeginOutputReadLine();

        // Detect early exit: if the Node process exits before we write stdin, WriteLineAsync will hang.
        if (_process.HasExited)
        {
            throw new System.Exception($"node exited immediately (code {_process.ExitCode}), entry: {_nodeEntryFullPath}");
        }

        ObservedIdentity = new ProcessIdentity(
            Pid: _process.Id,
            CreationTime: _process.StartTime.ToUniversalTime(),
            PluginId: pluginId,
            EntryId: entryId);
        var bootstrapToken = issueToken(ObservedIdentity);

        await _process.StandardInput.WriteLineAsync($"{pipePath}\t{bootstrapToken}");
        await _process.StandardInput.FlushAsync();

        // Wait for the Node SDK to connect the pipe.
        await connectTask;
    }

    public async Task StopAsync()
    {
        try { await (_transport?.DisposeAsync() ?? ValueTask.CompletedTask); }
        catch { /* transport teardown during process kill can throw */ }

        if (_job is not null)
        {
            _job.Dispose(); // kill-on-close reclaims the whole process tree
            _job = null;
        }

        _process?.Dispose();
        _process = null;
        _transport = null;
        ObservedIdentity = null;
    }
}
