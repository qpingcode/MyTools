using System.Runtime.InteropServices;

namespace MyTools.Plugins;

internal static class ClipboardAccess
{
    private const int ClipboardCannotOpen = unchecked((int)0x800401D0);
    private static readonly int[] DefaultRetryDelaysMs = [10, 25, 50, 100];

    public static void Execute(Action action, IReadOnlyList<int>? retryDelaysMs = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        Execute(() =>
        {
            action();
            return true;
        }, retryDelaysMs);
    }

    public static T Execute<T>(Func<T> action, IReadOnlyList<int>? retryDelaysMs = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        var delays = retryDelaysMs ?? DefaultRetryDelaysMs;

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return action();
            }
            catch (COMException ex) when (ex.HResult == ClipboardCannotOpen && attempt < delays.Count)
            {
                Thread.Sleep(delays[attempt]);
            }
        }
    }
}
