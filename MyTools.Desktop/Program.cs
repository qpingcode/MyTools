using System.Diagnostics;
using Velopack;

namespace MyTools.Desktop;

internal static class Program
{
    [STAThread]
    public static void Main()
    {
        WaitForRestartedProcess();

        VelopackApp.Build()
            .SetAutoApplyOnStartup(false)
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }

    private static void WaitForRestartedProcess()
    {
        var args = Environment.GetCommandLineArgs();
        if (args.Length != 3 || !string.Equals(args[1], "--restart-wait", StringComparison.Ordinal)
                             || !int.TryParse(args[2], out var processId))
        {
            return;
        }

        try
        {
            using var previousProcess = Process.GetProcessById(processId);
            previousProcess.WaitForExit(10_000);
        }
        catch (ArgumentException)
        {
            // The previous process has already exited.
        }
    }
}

