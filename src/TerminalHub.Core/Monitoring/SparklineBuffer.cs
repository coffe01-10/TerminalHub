namespace TerminalHub.Core.Monitoring;

/// <summary>Fixed-size ring buffer of recent samples for sparkline widgets.</summary>
public sealed class SparklineBuffer
{
    private readonly double[] _samples;
    private int _head;
    private int _count;

    public SparklineBuffer(int capacity = 60)
    {
        if (capacity < 2) throw new ArgumentOutOfRangeException(nameof(capacity));
        _samples = new double[capacity];
    }

    public int Capacity => _samples.Length;
    public int Count => _count;
    public double Latest => _count == 0 ? 0 : _samples[(_head - 1 + _samples.Length) % _samples.Length];

    public void Add(double value)
    {
        _samples[_head] = value;
        _head = (_head + 1) % _samples.Length;
        if (_count < _samples.Length) _count++;
    }

    /// <summary>Oldest-to-newest view of the buffer.</summary>
    public IEnumerable<double> Ordered()
    {
        var start = _count < _samples.Length ? 0 : _head;
        for (var i = 0; i < _count; i++)
            yield return _samples[(start + i) % _samples.Length];
    }

    public double[] ToArray()
    {
        var result = new double[_count];
        var i = 0;
        foreach (var v in Ordered()) result[i++] = v;
        return result;
    }
}
