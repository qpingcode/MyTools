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
    private int _highWaterMark;

    public BoundedEventQueue(int capacity) => _capacity = capacity;

    public int Capacity => _capacity;
    public int Count => _queue.Count;
    public int HighWaterMark => _highWaterMark;
    public long DroppedEvents => _dropped;

    public void Enqueue(T item)
        => TryEnqueue(item, out _);

    public bool TryEnqueue(T item, out T droppedItem)
    {
        droppedItem = default!;
        if (_queue.Count >= _capacity)
        {
            droppedItem = _queue.Dequeue();
            _dropped++;
            _queue.Enqueue(item);
            if (_queue.Count > _highWaterMark)
            {
                _highWaterMark = _queue.Count;
            }
            return true;
        }

        _queue.Enqueue(item);
        if (_queue.Count > _highWaterMark)
        {
            _highWaterMark = _queue.Count;
        }

        return false;
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

    public bool TryPeek(out T item)
    {
        if (_queue.Count == 0)
        {
            item = default!;
            return false;
        }

        item = _queue.Peek();
        return true;
    }
}
