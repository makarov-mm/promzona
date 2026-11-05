using System.Drawing;

namespace MakarovPhysics.Renderer;

/// <summary>
/// A batch of static world-space geometry wearing one SCANNED albedo/bump pair (30.10.2026): the brick wall of a
/// cave entrance, the plaster of a parlour, the slate of a tunnel face, the rusted iron of a pipe. It is the batched
/// sibling of <see cref="TexturedSurface"/> - that one draws a Mesh per call, this one takes the triangles the
/// diorama builder already produced (<see cref="SurfaceVertex"/>, colour as a TINT: 0.5 grey leaves the photograph
/// as it is) and draws them in one call through the same triplanar projection and three-tap bump.
///
/// It also does what TexturedSurface never had to: it receives the scene's shadow, casts into its depth pass, and
/// honours <see cref="OutdoorLighting"/> - daylight dimming and the twelve local lamps - so a scanned wall in a
/// lamp-lit cave is lit by the same lamps as the procedural rock beside it. A missing map is not an error:
/// <see cref="Available"/> goes false and the caller draws the geometry with its procedural fallback instead.
/// </summary>
public sealed class ScannedSurface : IDisposable
{
    private const int Stride = 9;
    private Shader? _shader, _depth;
    private Texture? _albedo, _bump;
    private uint _vao, _vbo;
    private int _count;

    /// <summary>Repeats of the map per metre.</summary>
    public float Scale = 2;
    public float BumpStrength = .7f;
    public float Roughness = .84f;
    public float Metal;
    public float Specular = .2f;
    public OutdoorLighting? Lighting;

    public bool Available => _shader is not null && _albedo is not null && _bump is not null;
    public int Count => _count;

    public ScannedSurface(string directory, string stem)
    {
        try
        {
            _albedo = Load(directory, stem + "_albedo.png");
            _bump = Load(directory, stem + "_bump.png");
            if (_albedo is null || _bump is null) { Release(); return; }
            _shader = new Shader(Vertex, Fragment);
            _depth = new Shader(DepthVertex, DefaultShaders.DepthFragment);
        }
        catch
        {
            // a broken PNG or a driver that refuses the program costs the photograph, not the module
            Release();
        }
    }

    private static Texture? Load(string directory, string file)
    {
        if (string.IsNullOrEmpty(directory)) return null;
        string path = Path.Combine(directory, file);
        if (!File.Exists(path)) return null;
        using var bitmap = new Bitmap(path);
        var texture = new Texture(bitmap);
        texture.SetWrapRepeat();
        return texture;
    }

    /// <summary>Uploads the triangles once; the batch is static scenery.</summary>
    public void Update(IReadOnlyList<SurfaceVertex> vertices)
    {
        if (!Available) return;
        var data = new float[Math.Max(1, vertices.Count) * Stride];
        for (int i = 0; i < vertices.Count; i++)
        {
            var v = vertices[i];
            int k = i * Stride;
            data[k] = v.Position.X; data[k + 1] = v.Position.Y; data[k + 2] = v.Position.Z;
            data[k + 3] = v.Normal.X; data[k + 4] = v.Normal.Y; data[k + 5] = v.Normal.Z;
            data[k + 6] = v.Color.X; data[k + 7] = v.Color.Y; data[k + 8] = v.Color.Z;
        }

        if (_vao == 0)
        {
            _vao = GL.GenVertexArray();
            _vbo = GL.GenBuffer();
        }

        GL.BindVertexArray(_vao);
        GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
        GL.BufferData(GL.ARRAY_BUFFER, data, GL.STATIC_DRAW);
        for (uint i = 0; i < 3; i++)
        {
            GL.VertexAttribPointer(i, 3, GL.FLOAT, false, Stride * sizeof(float), (int)i * 3 * sizeof(float));
            GL.EnableVertexAttribArray(i);
        }

        GL.BindVertexArray(0);
        _count = vertices.Count;
    }

    public void DrawDepth(Mat4 lightSpace)
    {
        if (_depth is null || _count == 0) return;
        _depth.Use();
        _depth.SetMatrix4("uLightSpace", lightSpace);
        DrawRaw();
    }

    public void Draw(Mat4 view, Mat4 projection, Vec3 eye, Vec3 light, Vec3 lightColor, Mat4 lightSpace, uint shadow,
        float ambient = .52f, float environmentFill = 0)
    {
        if (!Available || _count == 0) return;
        var s = _shader!;
        Materials.ApplyCamera(s, view, projection, eye);
        Materials.ApplyLight(s, light, lightColor, ambient, .82f, .25f);
        s.Use();
        s.SetFloat("uTexScale", Scale);
        s.SetFloat("uBumpStrength", BumpStrength);
        s.SetFloat("uRough", Roughness);
        s.SetFloat("uMetal", Metal);
        s.SetFloat("uSpec", Specular);
        s.SetFloat("uEnvironmentFill", environmentFill);
        s.SetMatrix4("uLightSpace", lightSpace);
        s.SetFloat("uShadowStrength", shadow == 0 ? 0 : .72f);
        s.SetInt("uLocalLighting", Lighting is null ? 0 : 1);
        Lighting?.Apply(s);
        s.Use();
        _albedo!.Bind();
        _bump!.Bind(1);
        s.SetInt("uAlbedoMap", 0);
        s.SetInt("uBumpMap", 1);
        s.SetInt("uShadowMap", 6);
        GL.ActiveTexture(GL.TEXTURE0 + 6);
        GL.BindTexture(GL.TEXTURE_2D, shadow);
        DrawRaw();
        GL.ActiveTexture(GL.TEXTURE0);
    }

    private void DrawRaw()
    {
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.TRIANGLES, 0, _count);
        GL.BindVertexArray(0);
    }

    private void Release()
    {
        _albedo?.Dispose(); _albedo = null;
        _bump?.Dispose(); _bump = null;
        _shader?.Dispose(); _shader = null;
        _depth?.Dispose(); _depth = null;
    }

    public void Dispose()
    {
        Release();
        if (_vbo != 0) { GL.DeleteBuffer(_vbo); _vbo = 0; }
        if (_vao != 0) { GL.DeleteVertexArray(_vao); _vao = 0; }
        _count = 0;
    }

    private const string DepthVertex = @"#version 330 core
layout(location=0) in vec3 aPos;
uniform mat4 uLightSpace;
void main(){gl_Position=uLightSpace*vec4(aPos,1);}
";

    private const string Vertex = @"#version 330 core
layout(location=0) in vec3 aPos;
layout(location=1) in vec3 aNormal;
layout(location=2) in vec3 aColor;
uniform mat4 uView, uProj;
out vec3 vWorldPos;
out vec3 vNormal;
out vec3 vColor;
void main(){vWorldPos=aPos;vNormal=aNormal;vColor=aColor;gl_Position=uProj*uView*vec4(aPos,1);}
";

    private const string Fragment = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;
in vec3 vColor;
uniform sampler2D uAlbedoMap;
uniform sampler2D uBumpMap;
uniform float uTexScale, uBumpStrength, uRough, uMetal, uSpec, uEnvironmentFill;
" + DefaultShaders.PbrLightUniforms + DefaultShaders.PbrFunctions + OutdoorLighting.LocalLightingGlsl + @"
vec3 triWeights(vec3 n) { vec3 w=abs(n); w*=w; w*=w; return w/max(w.x+w.y+w.z,1e-4); }
vec3 triAlbedo(vec3 q, vec3 w) {
    return texture(uAlbedoMap,q.zy).rgb*w.x+texture(uAlbedoMap,q.xz).rgb*w.y+texture(uAlbedoMap,q.xy).rgb*w.z;
}
float triHeight(vec3 q, vec3 w) {
    return texture(uBumpMap,q.zy).r*w.x+texture(uBumpMap,q.xz).r*w.y+texture(uBumpMap,q.xy).r*w.z;
}
void main() {
    float scale=uTexScale>0.?uTexScale:2.;
    vec3 n0=normalize(vNormal);
    vec3 w=triWeights(n0);
    vec3 q=vWorldPos*scale;
    vec3 albedo=triAlbedo(q,w)*clamp(vColor*2.,vec3(0),vec3(3));
    float e=.35/scale;
    float h=triHeight(q,w);
    vec3 grad=vec3(triHeight((vWorldPos+vec3(e,0,0))*scale,w)-h,
                   triHeight((vWorldPos+vec3(0,e,0))*scale,w)-h,
                   triHeight((vWorldPos+vec3(0,0,e))*scale,w)-h)/max(e,1e-5);
    vec3 tangential=grad-n0*dot(grad,n0);
    vec3 n=normalize(n0-tangential*uBumpStrength*.02);
    // crevices of the height map hold dirt and shade; the ridges catch the light
    albedo*=.82+.36*h;
    float rough=clamp(uRough-.14*(h-.5),.05,.98);
    FragColor=shadePbr(vWorldPos,n,albedo,rough,uMetal,uSpec);
    FragColor.rgb+=uEnvironmentFill*albedo*(.05+.10*max(n.y,0.));
    FragColor.rgb=outdoorLightResult(FragColor.rgb,albedo,n);
}";
}
