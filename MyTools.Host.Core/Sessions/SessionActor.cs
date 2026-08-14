using System.Threading;
using System.Threading.Tasks;

namespace MyTools.Host.Core.Sessions;

/// <summary>
/// A serial mailbox (actor) per logical entry. State transitions, endpoint register/unregister,
/// restart counts and session snapshots are only ever modified inside <see cref="PostAsync"/>,
/// which runs actions strictly one-at-a-time in submission order. The action itself must not await
/// transport/process/capability I/O — it kicks off async work and posts the result back when done.
/// </summary>
public sealed class SessionActor
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public Task PostAsync(System.Action action)
    {
        return PostAsync(() =>
        {
            action();
            return true;
        });
    }

    public async Task PostAsync(System.Func<bool> action)
    {
        await _gate.WaitAsync();
        try
        {
            action();
        }
        finally
        {
            _gate.Release();
        }
    }
}
