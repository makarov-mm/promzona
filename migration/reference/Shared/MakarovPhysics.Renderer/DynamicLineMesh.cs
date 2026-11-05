namespace MakarovPhysics.Renderer;

/// <summary>
/// A polyline whose points change every frame (waves, strings, live plots in
/// the scene). The VBO is allocated once at the maximum size and streamed via
/// BufferSubData - no per-frame mesh rebuilds, no garbage.
/// Vertex layout matches the shared shaders: position (loc 0) + normal (loc 1,
/// unused by the unlit shader but keeps the format uniform).
/// </summary>
public sealed class DynamicLineMesh : IDisposable
{
    private const int FloatsPerVertex = 6;

    private uint _vao, _vbo;
    private readonly float[] _data;
    private int _count;

    public uint Mode = GL.LINE_STRIP;

    public DynamicLineMesh(int maxPoints)
    {
        if (maxPoints < 2) throw new ArgumentOutOfRangeException(nameof(maxPoints));
        _data = new float[maxPoints * FloatsPerVertex];

        // Transactional construction (see Mesh)
        try
        {
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            _vbo = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, _data.Length * sizeof(float), GL.DYNAMIC_DRAW);
            const int stride = FloatsPerVertex * sizeof(float);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, GL.FLOAT, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Upload points WITH real normals (lit surfaces: cloth, membranes).</summary>
    public void Update(IReadOnlyList<Vec3> points, IReadOnlyList<Vec3> normals)
    {
        _count = Math.Min(points.Count, _data.Length / FloatsPerVertex);
        for (int i = 0; i < _count; i++)
        {
            int b = i * FloatsPerVertex;
            _data[b + 0] = points[i].X;
            _data[b + 1] = points[i].Y;
            _data[b + 2] = points[i].Z;
            var n = i < normals.Count ? normals[i] : new Vec3(0, 1, 0);
            _data[b + 3] = n.X;
            _data[b + 4] = n.Y;
            _data[b + 5] = n.Z;
        }
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, _count * FloatsPerVertex);
    }

    /// <summary>Upload the current points (count ≤ maxPoints; extra points are ignored).</summary>
    public void Update(IReadOnlyList<Vec3> points)
    {
        _count = Math.Min(points.Count, _data.Length / FloatsPerVertex);
        for (int i = 0; i < _count; i++)
        {
            int b = i * FloatsPerVertex;
            _data[b + 0] = points[i].X;
            _data[b + 1] = points[i].Y;
            _data[b + 2] = points[i].Z;
            // write the FULL dummy normal: a previous Update(points, normals)
            // may have left arbitrary X/Z here, and the buffer is reused
            _data[b + 3] = 0f;
            _data[b + 4] = 1f;
            _data[b + 5] = 0f;
        }
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, _count * FloatsPerVertex);
    }

    public void Draw()
    {
        if (_count < 1) return;
        GL.BindVertexArray(_vao);
        GL.DrawArrays(Mode, 0, _count);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_vbo != 0) { GL.DeleteBuffer(_vbo); _vbo = 0; }
        if (_vao != 0) { GL.DeleteVertexArray(_vao); _vao = 0; }
    }
}
