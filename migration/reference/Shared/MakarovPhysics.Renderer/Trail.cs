namespace MakarovPhysics.Renderer;

/// <summary>Ring buffer of recent positions with fading color (newer → brighter).</summary>
public sealed class Trail(int capacity)
{
    private readonly Vec3[] _buf = new Vec3[Math.Max(1, capacity)];
    private int _count, _head;
    public Vec3 BaseColor = new(0.95f, 0.45f, 0.15f);

    public void Clear()
    {
        _count = 0; 
        _head = 0;
    }

    public void Push(Vec3 p)
    {
        _buf[_head] = p;
        _head = (_head + 1) % _buf.Length;
        if (_count < _buf.Length) _count++;
    }

    public void Fill(List<TrailPoint> dst)
    {
        dst.Clear();

        for (int i = 0; i < _count; i++)
        {
            int idx = (_head - _count + i) % _buf.Length;
            if (idx < 0) idx += _buf.Length;
            float k = (i + 1) / (float)_count; // newer → brighter
            dst.Add(new TrailPoint(_buf[idx], BaseColor * k));
        }
    }
}
