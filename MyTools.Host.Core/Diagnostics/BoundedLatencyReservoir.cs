namespace MyTools.Host.Core.Diagnostics;

public sealed class BoundedLatencyReservoir
{
    private readonly object _gate = new();
    private readonly double[] _samples;
    private int _count;
    private int _nextIndex;
    private long _totalCount;
    private double _totalMs;
    private double _lastMs;
    private double _maxMs;

    public BoundedLatencyReservoir(int capacity = 256)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(capacity);
        _samples = new double[capacity];
    }

    public int Capacity => _samples.Length;

    public void Add(double elapsedMs)
    {
        if (double.IsNaN(elapsedMs) || double.IsInfinity(elapsedMs) || elapsedMs < 0)
        {
            return;
        }

        lock (_gate)
        {
            _samples[_nextIndex] = elapsedMs;
            _nextIndex = (_nextIndex + 1) % _samples.Length;
            if (_count < _samples.Length)
            {
                _count++;
            }

            _totalCount++;
            _totalMs += elapsedMs;
            _lastMs = elapsedMs;
            if (elapsedMs > _maxMs)
            {
                _maxMs = elapsedMs;
            }
        }
    }

    public LatencySnapshot Snapshot()
    {
        lock (_gate)
        {
            if (_count == 0)
            {
                return new LatencySnapshot(0, 0, 0, 0, 0, 0, 0, 0);
            }

            var samples = new double[_count];
            Array.Copy(_samples, samples, _count);
            Array.Sort(samples);
            return new LatencySnapshot(
                _totalCount,
                _count,
                _lastMs,
                _totalCount == 0 ? 0 : _totalMs / _totalCount,
                _maxMs,
                Percentile(samples, 0.50),
                Percentile(samples, 0.95),
                Percentile(samples, 0.99));
        }
    }

    private static double Percentile(IReadOnlyList<double> samples, double percentile)
    {
        if (samples.Count == 0)
        {
            return 0;
        }

        var index = (int)Math.Ceiling(percentile * samples.Count) - 1;
        return samples[Math.Clamp(index, 0, samples.Count - 1)];
    }
}
