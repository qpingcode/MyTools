using System.Reflection;
using System.Runtime.InteropServices;

namespace MyTools.Plugins;

internal static class ExplorerShellLauncher
{
    public static bool TryLaunch(string fileName, string arguments, string workingDirectory, bool runAsAdmin, out Exception? error)
    {
        try
        {
            Exception? explorerError = null;
            if (!runAsAdmin && TryLaunchFromExplorer(fileName, arguments, workingDirectory, runAsAdmin, out explorerError))
            {
                Console.WriteLine(
                    "[shell-launch] explorer success file={0} args={1} workDir={2}",
                    fileName,
                    arguments,
                    workingDirectory);
                error = null;
                return true;
            }
            else if (!runAsAdmin && explorerError is not null)
            {
                Console.WriteLine(
                    "[shell-launch] explorer failed file={0} args={1} workDir={2} error={3}",
                    fileName,
                    arguments,
                    workingDirectory,
                    explorerError.Message);
            }

            Launch(fileName, arguments, workingDirectory, runAsAdmin);
            Console.WriteLine(
                "[shell-launch] fallback success file={0} args={1} workDir={2} runAsAdmin={3}",
                fileName,
                arguments,
                workingDirectory,
                runAsAdmin);
            error = null;
            return true;
        }
        catch (Exception ex)
        {
            Console.WriteLine(
                "[shell-launch] failed file={0} args={1} workDir={2} runAsAdmin={3} error={4}",
                fileName,
                arguments,
                workingDirectory,
                runAsAdmin,
                ex.Message);
            error = ex;
            return false;
        }
    }

    private static bool TryLaunchFromExplorer(string fileName, string arguments, string workingDirectory, bool runAsAdmin, out Exception? error)
    {
        object? shell = null;
        object? windows = null;
        Exception? lastError = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application")
                            ?? throw new InvalidOperationException("Shell.Application is unavailable.");
            shell = Activator.CreateInstance(shellType)
                    ?? throw new InvalidOperationException("Failed to create Shell.Application instance.");

            windows = shellType.InvokeMember(
                "Windows",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args: null);
            if (windows is null)
            {
                throw new InvalidOperationException("Shell.Application.Windows returned null.");
            }

            var windowsType = windows.GetType();
            var count = Convert.ToInt32(
                windowsType.InvokeMember(
                    "Count",
                    BindingFlags.GetProperty,
                    binder: null,
                    target: windows,
                    args: null));

            for (var i = 0; i < count; i++)
            {
                object? window = null;
                object? document = null;
                object? app = null;
                try
                {
                    window = windowsType.InvokeMember(
                        "Item",
                        BindingFlags.InvokeMethod,
                        binder: null,
                        target: windows,
                        args: [i]);
                    if (window is null)
                    {
                        continue;
                    }

                    document = window.GetType().InvokeMember(
                        "Document",
                        BindingFlags.GetProperty,
                        binder: null,
                        target: window,
                        args: null);
                    if (document is null)
                    {
                        continue;
                    }

                    app = document.GetType().InvokeMember(
                        "Application",
                        BindingFlags.GetProperty,
                        binder: null,
                        target: document,
                        args: null);
                    if (app is null)
                    {
                        continue;
                    }

                    app.GetType().InvokeMember(
                        "ShellExecute",
                        BindingFlags.InvokeMethod,
                        binder: null,
                        target: app,
                        args:
                        [
                            fileName,
                            string.IsNullOrWhiteSpace(arguments) ? null : arguments,
                            string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                            runAsAdmin ? "runas" : "open",
                            1
                        ]);

                    error = null;
                    return true;
                }
                catch (Exception ex)
                {
                    lastError = ex;
                }
                finally
                {
                    ReleaseComObject(app);
                    ReleaseComObject(document);
                    ReleaseComObject(window);
                }
            }

            error = lastError ?? new InvalidOperationException("No Explorer automation window was available.");
            return false;
        }
        catch (Exception ex)
        {
            error = ex;
            return false;
        }
        finally
        {
            ReleaseComObject(windows);
            ReleaseComObject(shell);
        }
    }

    public static void Launch(string fileName, string arguments, string workingDirectory, bool runAsAdmin)
    {
        var shellType = Type.GetTypeFromProgID("Shell.Application")
                        ?? throw new InvalidOperationException("Shell.Application is unavailable.");

        object? shell = Activator.CreateInstance(shellType)
                        ?? throw new InvalidOperationException("Failed to create Shell.Application instance.");
        try
        {
            shellType.InvokeMember(
                "ShellExecute",
                BindingFlags.InvokeMethod,
                binder: null,
                target: shell,
                args:
                [
                    fileName,
                    string.IsNullOrWhiteSpace(arguments) ? null : arguments,
                    string.IsNullOrWhiteSpace(workingDirectory) ? null : workingDirectory,
                    runAsAdmin ? "runas" : "open",
                    1
                ]);
        }
        finally
        {
            ReleaseComObject(shell);
        }
    }

    private static void ReleaseComObject(object? obj)
    {
        if (obj is not null && Marshal.IsComObject(obj))
        {
            Marshal.FinalReleaseComObject(obj);
        }
    }
}
