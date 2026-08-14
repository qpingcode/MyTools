using System.Collections.Generic;

namespace MyTools.Host.Core.Backpressure;

/// <summary>
/// Per-endpoint bounded outbound event queue. When full, the oldest event is dropped (the design's
/// "满时丢弃最旧并递增 droppedEvents" rule) and <see cref="DroppedEvents"/> is incremented. Events
/// are not reliably delivered; a page reload or state re-read recovers. Used so a slow WebView
/// cannot block the bus or consume unbounded memory.
/// </summary>
public sealed class BoundedEventQueue<T>
{
    private readonly int _capacity;
    private readonly Queue<T> _queue = new();
    private long _dropped;

    public BoundedEventQueue(int capacity) => _capacity = capacity;

    public long DroppedEvents => _dropped;

    public void Enqueue(T item)
    {
        if (_queue.Count >= _capacity)
        {
            _queue.Dequeue();
            _dropped++;
        }
        _queue.Enqueue(item);
    }

    /// <summary>Drains and returns all currently-queued events in FIFO order.</summary>
    public IReadOnlyList<T> Drain()
    {
        var snapshot = new List<T>(_queue.Count);
        while (_queue.Count > 0)
        {
            snapshot.Add(_queue.Dequeue());
        }
        return snapshot;
    }
}
