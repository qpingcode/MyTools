using Process = System.Diagnostics.Process;
using System.Diagnostics;
using System.Threading.Tasks;
using MyTools.Host.Transports.Process;
using NUnit.Framework;

namespace MyTools.Host.Transports.Test.Process;

[TestFixture]
public class ProcessTreeJobTest
{
    // A long-lived child that stays alive until killed. ping -t 127.0.0.1 runs forever on Windows.
    private static System.Diagnostics.Process StartLongLivedChild()
    {
        var psi = new ProcessStartInfo("ping", "-t 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        var p = System.Diagnostics.Process.Start(psi);
        Assert.That(p, Is.Not.Null, "failed to start test child process");
        return p!;
    }

    [Test]
    public async Task Assign_ThenDispose_ShouldKillTheProcess()
    {
        var child = StartLongLivedChild();
        using (var job = new ProcessTreeJob())
        {
            job.Assign(child);
            Assert.That(child.HasExited, Is.False, "child should be alive while job is open");
        }
        // Job disposed (kill-on-close) -> child must be terminated.
        await Task.Delay(300); // give the OS a moment to reap

        Assert.That(child.HasExited, Is.True, "child should be killed after job disposed");
        try { child.Dispose(); } catch { /* already exited */ }
    }

    [Test]
    public void Assign_MultipleProcesses_AllReclaimedOnDispose()
    {
        var c1 = StartLongLivedChild();
        var c2 = StartLongLivedChild();
        using (var job = new ProcessTreeJob())
        {
            job.Assign(c1);
            job.Assign(c2);
        }

        Assert.That(c1.HasExited, Is.True);
        Assert.That(c2.HasExited, Is.True);
        try { c1.Dispose(); } catch { }
        try { c2.Dispose(); } catch { }
    }
}
