namespace MakarovPhysics.Renderer;

public sealed class Mesh : IDisposable
{
    private uint _vao, _vbo, _ebo;
    private readonly int _indexCount;
    public uint Mode = GL.TRIANGLES;

    public Mesh(float[] vertices, uint[] indices, uint mode = GL.TRIANGLES)
    {
        Mode = mode;
        _indexCount = indices.Length;

        // Transactional construction - a failing GL call must not
        // leak the buffers created before it (fault path: bad driver, lost
        // context; exactly when the app tries to fall back to the carousel)
        try
        {
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);

            _vbo = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, vertices, GL.STATIC_DRAW);

            _ebo = GL.GenBuffer();
            GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER, _ebo);
            GL.BufferData(GL.ELEMENT_ARRAY_BUFFER, indices, GL.STATIC_DRAW);

            const int stride = 6 * sizeof(float);

            // location 0 - position
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, stride, 0);
            GL.EnableVertexAttribArray(0);

            // location 1 - normale
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

    public void Draw()
    {
        GL.BindVertexArray(_vao);
        GL.DrawElements(Mode, _indexCount, GL.UNSIGNED_INT, 0);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if (_vao != 0)
        {
            GL.DeleteVertexArray(_vao); 
            _vao = 0;
        }

        if (_vbo != 0)
        {
            GL.DeleteBuffer(_vbo); 
            _vbo = 0;
        }

        if (_ebo != 0)
        {
            GL.DeleteBuffer(_ebo);
            _ebo = 0;
        }
    }
}