namespace TerminalHub.Core.Monitoring;

/// <summary>Fixed-size ring buffer of recent samples for sparkline widgets.
/// Thread-safe: samples are added on the monitor thread and read on the UI thread.</summary>
public sealed class SparklineBuffer
{
    private readonly double[] _samples;
    private readonly object _gate = new();
    private int _head;
    private int _count;

    public SparklineBuffer(int capacity = 60)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new double[capacity];
    }

    public int Capacity => _samples.Length;
    public int Count { get { lock (_gate) return _count; } }
    public double Latest
    {
        get
        {
            lock (_gate)
                return _count == 0 ? 0 : _samples[(_head - 1 + _samples.Length) % _samples.Length];
        }
    }

    public void Add(double value)
    {
        lock (_gate)
        {
            _samples[_head] = value;
            _head = (_head + 1) % _samples.Length;
            if (_count < _samples.Length) _count++;
        }
    }

    /// <summary>Oldest-to-newest snapshot of the buffer.</summary>
    public IEnumerable<double> Ordered() => ToArray();

    public double[] ToArray()
    {
        lock (_gate)
        {
            var result = new double[_count];
            var start = _count < _samples.Length ? 0 : _head;
            for (var i = 0; i < _count; i++)
                result[i] = _samples[(start + i) % _samples.Length];
            return result;
        }
    }
}
