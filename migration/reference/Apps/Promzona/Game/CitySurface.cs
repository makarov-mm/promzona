using MakarovPhysics.Renderer;
namespace Promzona.Game;

public sealed class CitySurface : IDisposable
{
    private const int Stride = 13;
    private float[] _data;
    public int VertexCount => _count;
    public int StagingBytes => _data.Length * sizeof(float);
    private Shader? _shader, _depth;
    private readonly bool _owner;
    private bool _disposed;
    private readonly Texture? _albedo;
    private readonly Texture? _bump;
    private readonly CityAtlas? _atlas;
    public static float Wetness { get; set; }
    private uint _vao, _vbo, _ebo;
    private int _indexCount;
    public int UniqueVertexCount {get;private set;}
    public long GpuBytes => (long)UniqueVertexCount * Stride * sizeof(float) + (long)_indexCount * sizeof(uint);
    private int _count;
    public bool DetailedFinish { get; set; }

    /// <summary>Opt-in filtered production materials and analytic workshop reflections.</summary>
    public bool WorkshopFinish { get; set; }

    public bool Outdoor { get; set; }
    public float AtmosphereDensity { get; set; } = 1;
    public OutdoorLighting? Lighting { get; set; }
    internal CityReflections? Reflections {get;set;}

    public CitySurface(int capacity = 600000, CitySurface? shared = null)
    {
        _data = new float[capacity * Stride];

        try
        {
            _owner = shared is null;
            _shader = shared?._shader ?? new Shader(CityShaderSource.ReadGeometry("Surface.vert"), CityShaderSource.ReadGeometry("Surface.frag"));
            _depth = shared?._depth ?? new Shader(CityShaderSource.ReadGeometry("SurfaceDepth.vert"), DefaultShaders.DepthFragment);

            if (shared is not null)
            {
                _albedo = shared._albedo;
                _bump = shared._bump;
                _atlas = shared._atlas;
            }
            else
            {
                _atlas = CityAtlas.Acquire();
                string path = Path.Combine(CityAtlas.AssetRoot, "City");

                if (File.Exists(Path.Combine(path, "wall_albedo.png")))
                {
                    using var a = new Bitmap(Path.Combine(path, "wall_albedo.png"));
                    using var b = new Bitmap(Path.Combine(path, "wall_bump.png"));
                    _albedo = new Texture(a);
                    _bump = new Texture(b);
                    _albedo.SetWrapRepeat();
                    _bump.SetWrapRepeat();
                }
            }

            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
            GL.BindVertexArray(_vao);
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, _data.Length * sizeof(float), GL.DYNAMIC_DRAW);

            for (uint i = 0; i < 5; i++)
            {
                GL.VertexAttribPointer(i, i == 4 ? 1 : 3, GL.FLOAT, false, Stride * sizeof(float), (int)i * 3 * sizeof(float));
                GL.EnableVertexAttribArray(i);
            }

            GL.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public void Update(IReadOnlyList<SurfaceVertex> vertices, SurfaceMaterial automatic = SurfaceMaterial.Enamel)
    {
        if (vertices.Count > _data.Length / Stride)
        {
            throw new InvalidOperationException("Studio surface geometry budget exceeded");
        }

        _count = vertices.Count;

        for (int i = 0; i < _count; i++)
        {
            SurfaceVertex v = vertices[i];
            int k = i * Stride;

            _data[k] = v.Position.X;
            _data[k + 1] = v.Position.Y;
            _data[k + 2] = v.Position.Z;
            _data[k + 3] = v.Normal.X;
            _data[k + 4] = v.Normal.Y;
            _data[k + 5] = v.Normal.Z;
            _data[k + 6] = v.Color.X;
            _data[k + 7] = v.Color.Y;
            _data[k + 8] = v.Color.Z;
            _data[k + 9] = v.Local.X;
            _data[k + 10] = v.Local.Y;
            _data[k + 11] = v.Local.Z;
            _data[k + 12] = (float)(v.Material == SurfaceMaterial.Automatic ? automatic : v.Material);
        }

        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferSubData(GL.ARRAY_BUFFER, 0, _data, _count * Stride);
    }

    /// <summary>Uploads a static tile and releases its CPU staging buffer.</summary>
    public void UploadStatic(IReadOnlyList<SurfaceVertex> vertices)
    {
        var staging = new CityMeshStaging();
        staging.Begin(vertices);
        while (!staging.Pack()) { }
        UploadStatic(staging);
    }

    internal void UploadStatic(CityMeshStaging staging)
    {
        if(_ebo!=0)throw new InvalidOperationException("Static tile already uploaded");
        if(!staging.Complete)throw new InvalidOperationException("Static tile packing is incomplete");
        // Upload only the used prefixes. Capacity is reused by the next tile,
        // never retained by a resident surface or submitted as spare triangles.
        int floats=staging.UniqueCount*Stride;
        GL.BindBuffer(GL.ARRAY_BUFFER,_vbo);
        GL.BufferData(GL.ARRAY_BUFFER,floats*sizeof(float),GL.STATIC_DRAW);
        if(floats>0)GL.BufferSubData(GL.ARRAY_BUFFER,0,staging.Vertices,floats);
        UniqueVertexCount=staging.UniqueCount;
        _count=staging.VertexCount;_indexCount=staging.VertexCount;
        _ebo=GL.GenBuffer();
        GL.BindVertexArray(_vao);
        GL.BindBuffer(GL.ELEMENT_ARRAY_BUFFER,_ebo);
        GL.BufferData(GL.ELEMENT_ARRAY_BUFFER,_indexCount*sizeof(uint),GL.STATIC_DRAW);
        if(_indexCount>0)GL.BufferSubData(GL.ELEMENT_ARRAY_BUFFER,0,staging.Indices,_indexCount);
        GL.BindVertexArray(0);
        _data = [];
    }

    // The caller binds the matching shared program once with BeginDepthBatch.
    internal void DrawBound() => DrawRaw();

    internal void DrawIndices(int first, int count)
    {
        GL.BindVertexArray(_vao);
        GL.DrawElements(GL.TRIANGLES, count, GL.UNSIGNED_INT, first * sizeof(uint));
        GL.BindVertexArray(0);
    }
    internal void BeginDepthBatch(Mat4 lightSpace, float time)
    {
        _depth!.Use();
        _depth.SetMatrix4("uModel", Mat4.Identity);
        _depth.SetFloat("uTime", time);
        _depth.SetMatrix4("uLightSpace", lightSpace);
    }

    public void DrawDepth(Mat4 lightSpace) => DrawDepth(lightSpace, 0);
    public void DrawDepth(Mat4 lightSpace, float time) => DrawDepthTranslated(lightSpace, Vec3.Zero, time);

    public void DrawDepthTranslated(Mat4 lightSpace, Vec3 offset, float time)
    {
        if (_depth is null || _count == 0)
        {
            return;
        }

        _depth.Use();
        _depth.SetMatrix4("uModel", Mat4.Translate(offset));
        _depth.SetFloat("uTime", time);
        _depth.SetMatrix4("uLightSpace", lightSpace);
        DrawRaw();
    }

    public void Draw(Mat4 view, Mat4 projection, Vec3 eye, Vec3 light, Vec3 lightColor, Mat4 lightSpace, uint shadow, float time, float ambient = .52f, bool emissionOnly = false, float environmentFill = 0) => DrawTranslated(view, projection, eye, light, lightColor, lightSpace, shadow, time, Vec3.Zero, ambient, emissionOnly, environmentFill);

    public void DrawTranslated(Mat4 view, Mat4 projection, Vec3 eye, Vec3 light, Vec3 lightColor, Mat4 lightSpace,
        uint shadow, float time, Vec3 offset, float ambient = .52f, bool emissionOnly = false,
        float environmentFill = 0)
    {
        if (_shader is null || _count == 0)
            return;
        BeginBatch(view, projection, eye, light, lightColor, lightSpace, shadow, time, offset, ambient, emissionOnly, environmentFill);
        DrawRaw();
        GL.ActiveTexture(GL.TEXTURE0);
    }

    internal void BeginBatch(Mat4 view, Mat4 projection, Vec3 eye, Vec3 light, Vec3 lightColor, Mat4 lightSpace,
        uint shadow, float time, Vec3 offset, float ambient, bool emissionOnly, float environmentFill)
    {
        var s = _shader!;
        s.Use();
        s.SetVec3("uOffset", offset);
        s.SetInt("uHasFacade", _albedo is null ? 0 : 1);
        _albedo?.Bind(4);
        _bump?.Bind(5);
        s.SetInt("uFacade", 4);
        s.SetInt("uFacadeBump", 5);
        _atlas?.Color.Bind(7);
        _atlas?.Height.Bind(8);
        _atlas?.Finish.Bind(10);
        s.SetInt("uAtlasFinish",10);
        s.SetInt("uAtlas", 7);
        s.SetInt("uAtlasHeight", 8);
        s.SetFloat("uWetness", Wetness);
        // The wet sand band sits 8 m inside the coast of the current layout. It
        // was a literal 622 (= the 33-node island's edge), which darkened the
        // whole 54 m coastal strip of every 49-node city and left no band at all.
        s.SetFloat("uLandEdge", (float)CityTraffic.LandEdge);
        s.SetMatrix4("uView", view);
        s.SetMatrix4("uProj", projection);
        Materials.ApplyLight(s, light, lightColor, ambient, .82f, .25f);
        s.SetVec3("uViewPos", eye);
        s.SetFloat("uTime", time);
        s.SetInt("uDetailedFinish", DetailedFinish ? 1 : 0);
        s.SetInt("uWorkshopFinish", WorkshopFinish ? 1 : 0);
        s.SetInt("uOutdoor", Outdoor ? 1 : 0);
        s.SetInt("uLocalLighting", Lighting is null ? 0 : 1);
        if (Lighting is not null) CityLighting.Apply(Lighting, s);
        if(Reflections is not null)Reflections.Apply(s);
        else {s.SetInt("uProbe",12);s.SetInt("uProbeReady",0);}
        s.SetFloat("uAtmosphereDensity", Math.Clamp(AtmosphereDensity, 0, 1));
        s.SetFloat("uEnvironmentFill", environmentFill);
        s.SetMatrix4("uLightSpace", lightSpace);
        s.SetFloat("uShadowStrength", shadow == 0 ? 0 : .72f*(1-.72f*CityLighting.Atmosphere.Rain));
        s.SetInt("uShadowMap", 6);
        s.SetInt("uEmissionOnly", emissionOnly ? 2 : 0);
        GL.ActiveTexture(GL.TEXTURE0 + 6);
        GL.BindTexture(GL.TEXTURE_2D, shadow);
        GL.ActiveTexture(GL.TEXTURE0);
    }

    private void DrawRaw()
    {
        GL.BindVertexArray(_vao);
        if(_ebo!=0)GL.DrawElements(GL.TRIANGLES,_indexCount,GL.UNSIGNED_INT,0);
        else GL.DrawArrays(GL.TRIANGLES, 0, _count);
        GL.BindVertexArray(0);
    }

    public void Dispose()
    {
        if(_disposed)return;
        _disposed=true;
        if (_owner)
        {
            _shader?.Dispose();
            _albedo?.Dispose();
            _bump?.Dispose();
            _atlas?.Dispose();
        }

        _shader = null;
        if (_owner) _depth?.Dispose();
        _depth = null;
        if(_ebo!=0){GL.DeleteBuffer(_ebo);_ebo=0;}
        if (_vbo != 0)
        {
            GL.DeleteBuffer(_vbo);
            _vbo = 0;
        }

        if (_vao != 0)
        {
            GL.DeleteVertexArray(_vao);
            _vao = 0;
        }
    }
}
