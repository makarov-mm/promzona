using MakarovPhysics.UI;

namespace MakarovPhysics.Charts;

/// <summary>Point series backed by a ring buffer - the data source for charts.</summary>
public sealed class Series(int capacity, string name = "", UiColor color = default)
{
    public string Name = name;
    public UiColor Color = color.A > 0 ? color : UiColor.FromBytes(220, 90, 70);

    // Capacity 0 used to reach `% _x.Length` as a division by
    // zero; a negative one died in the array allocation with an obscure error
    private readonly double[] _x = capacity > 0
        ? new double[capacity]
        : throw new ArgumentOutOfRangeException(nameof(capacity), "Series capacity must be positive.");
    private readonly double[] _y = new double[capacity];
    private int _head, _count;
    public int Count => _count;

    public void Add(double x, double y)
    {
        // A single NaN/Infinity sample poisons the autoscale of
        // the whole chart - drop it instead of storing it
        if (!double.IsFinite(x) || !double.IsFinite(y)) return;

        _x[_head] = x; _y[_head] = y;
        _head = (_head + 1) % _x.Length;
        if (_count < _x.Length) _count++;
    }

    public void Clear()
    {
        _head = 0; 
        _count = 0;
    }

    public (double x, double y) At(int i)
    {
        // an out-of-range index silently returned a stale or
        // uninitialized ring-buffer slot instead of failing loudly
        if ((uint)i >= (uint)_count)
        {
            throw new ArgumentOutOfRangeException(nameof(i), $"Index {i} is outside 0..{_count - 1}.");
        }

        int idx = (_head - _count + i) % _x.Length;

        if (idx < 0)
        {
            idx += _x.Length;
        }

        return (_x[idx], _y[idx]);
    }

    public (double minX, double maxX, double minY, double maxY) Bounds()
    {
        double minX = double.MaxValue, maxX = double.MinValue;
        double minY = double.MaxValue, maxY = double.MinValue;

        for (int i = 0; i < _count; i++)
        {
            (double x, double y) = At(i);

            if (x < minX) minX = x;
            if (x > maxX) maxX = x;
            if (y < minY) minY = y;
            if (y > maxY) maxY = y;
        }

        return (minX, maxX, minY, maxY);
    }
}
