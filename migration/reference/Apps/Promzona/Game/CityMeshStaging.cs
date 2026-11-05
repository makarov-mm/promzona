using System.Runtime.InteropServices;
using MakarovPhysics.Renderer;

namespace Promzona.Game;

/// <summary>One reusable CPU workspace for streamed tiles. Packing may resume next
/// frame; first-occurrence vertex order and every material/normal/UV are retained.</summary>
internal sealed class CityMeshStaging
{
    public const int Stride = 13;
    private readonly Dictionary<SurfaceVertex, uint> _lookup = [];
    private IReadOnlyList<SurfaceVertex>? _source;
    private float[] _vertices = [];
    private uint[] _indices = [];
    private int _cursor;
    private Vec3 _low, _high;
    public float[] Vertices => _vertices;
    public uint[] Indices => _indices;
    public int VertexCount { get; private set; }
    public int UniqueCount { get; private set; }
    public bool Complete => _cursor == VertexCount;
    public Vec3 Centre => VertexCount == 0 ? Vec3.Zero : (_low + _high) * .5f;
    public float Radius => VertexCount == 0 ? 0 : (_high - _low).Length() * .5f + 2;
    public long BufferBytes => (long)_vertices.Length * sizeof(float) + (long)_indices.Length * sizeof(uint);

    public void Begin(IReadOnlyList<SurfaceVertex> source)
    {
        _lookup.Clear();
        _source = source;
        VertexCount = source.Count;
        UniqueCount = _cursor = 0;
        _low = new(float.MaxValue, float.MaxValue, float.MaxValue);
        _high = new(float.MinValue, float.MinValue, float.MinValue);
        Grow(ref _indices, VertexCount);
    }

    public bool Pack(int count = 4096)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));
        int end = _cursor + Math.Min(count, VertexCount - _cursor);
        for (; _cursor < end; _cursor++)
        {
            var v = _source![_cursor];
            ref uint index = ref CollectionsMarshal.GetValueRefOrAddDefault(_lookup, v, out bool exists);
            if (!exists)
            {
                index = (uint)UniqueCount++;
                int k = (int)index * Stride;
                Grow(ref _vertices, k + Stride);
                _vertices[k] = v.Position.X; _vertices[k + 1] = v.Position.Y; _vertices[k + 2] = v.Position.Z;
                _vertices[k + 3] = v.Normal.X; _vertices[k + 4] = v.Normal.Y; _vertices[k + 5] = v.Normal.Z;
                _vertices[k + 6] = v.Color.X; _vertices[k + 7] = v.Color.Y; _vertices[k + 8] = v.Color.Z;
                _vertices[k + 9] = v.Local.X; _vertices[k + 10] = v.Local.Y; _vertices[k + 11] = v.Local.Z;
                _vertices[k + 12] = (float)(v.Material == SurfaceMaterial.Automatic ? SurfaceMaterial.Enamel : v.Material);
                _low = new(Math.Min(_low.X,v.Position.X),Math.Min(_low.Y,v.Position.Y),Math.Min(_low.Z,v.Position.Z));
                _high = new(Math.Max(_high.X,v.Position.X),Math.Max(_high.Y,v.Position.Y),Math.Max(_high.Z,v.Position.Z));
            }
            _indices[_cursor] = index;
        }
        return Complete;
    }

    private static void Grow<T>(ref T[] buffer, int size)
    {
        if (size > buffer.Length) Array.Resize(ref buffer, Math.Max(size, Math.Max(4096, buffer.Length * 2)));
    }

    public void Cancel()
    {
        _source = null;
        _lookup.Clear();
        VertexCount = UniqueCount = _cursor = 0;
    }

    public void Release()
    {
        Cancel();
        _lookup.TrimExcess();
        _vertices = []; _indices = [];
    }
}
