namespace MyTools.Desktop.Views;

/// <summary>
/// Tracks explicitly declared operations that temporarily move focus away from a
/// launcher window without dismissing it. Supports nested and repeated disposal.
/// </summary>
internal sealed class AutoHideSuppressionState
{
    private int count;

    public bool IsSuppressed => Volatile.Read(ref count) > 0;

    public IDisposable Enter()
    {
        Interlocked.Increment(ref count);
        return new Releaser(this);
    }

    private void Exit()
    {
        Interlocked.Decrement(ref count);
    }

    private sealed class Releaser(AutoHideSuppressionState owner) : IDisposable
    {
        private AutoHideSuppressionState? current = owner;

        public void Dispose()
        {
            Interlocked.Exchange(ref current, null)?.Exit();
        }
    }
}
