namespace MakarovPhysics.Renderer;

/// <summary>
/// The real naked-eye sky behind the Space dome (wave W6, 20.10): the 8 874
/// Hipparcos stars to V = 6.5 of <see cref="SkyCatalog"/> as point sprites
/// at infinity - size and brightness from the magnitude, colour from B−V
/// through the Ballesteros temperature and <see cref="StarBody.ColorOfTemperature"/>
/// - and, on request, the IAU constellation figures as faint lines. Drawn
/// right after the dome, depth off, additive, so every body of the scene
/// covers them.
///
/// Frame: ICRS J2000 with the celestial pole along +y of the scene,
/// x toward the vernal equinox (RA 0) and z toward RA 270° (so RA grows the
/// way a globe's longitude does seen from outside); <see cref="SkyRotation"/>
/// turns the whole sky for a module that wants the ecliptic or the horizon
/// horizontal.
/// </summary>
public sealed class CatalogSky : IDisposable
{
    private const int FloatsPerVertex = 7; // dir xyz, colour rgb, size
    private readonly Shader _shader = new(Vertex, Fragment);
    private uint _vao, _vbo;
    private readonly int _count;
    private ColorLineMesh? _lines;
    private readonly List<TrailPoint> _linePoints = [];

    /// <summary>Rotation applied to the catalogue frame (identity: pole up).</summary>
    public Mat4 SkyRotation = Mat4.Identity;

    public CatalogSky()
    {
        float[] stars = SkyCatalog.Stars;
        _count = SkyCatalog.StarCount;
        var data = new float[_count * FloatsPerVertex];

        for (int i = 0; i < _count; i++)
        {
            float ra = stars[i * 4] * MathF.PI / 180, dec = stars[i * 4 + 1] * MathF.PI / 180;
            float mag = stars[i * 4 + 2], bv = stars[i * 4 + 3];
            var dir = Direction(ra, dec);
            var colour = StarBody.ColorOfTemperature(TemperatureOfColourIndex(bv));
            // flux from the magnitude, pinned so Sirius does not drown the field and
            // a sixth-magnitude star still shows as a dot
            float flux = Math.Clamp(MathF.Pow(10f, -.4f * (mag - 1.5f)), .035f, 1.6f);
            float size = Math.Clamp(1.6f + (6.5f - mag) * .75f, 1.2f, 7f);
            int k = i * FloatsPerVertex;
            data[k] = dir.X; data[k + 1] = dir.Y; data[k + 2] = dir.Z;
            data[k + 3] = colour.X * flux; data[k + 4] = colour.Y * flux; data[k + 5] = colour.Z * flux;
            data[k + 6] = size;
        }

        try
        {
            _vao = GL.GenVertexArray();
            GL.BindVertexArray(_vao);
            _vbo = GL.GenBuffer();
            GL.BindBuffer(GL.ARRAY_BUFFER, _vbo);
            GL.BufferData(GL.ARRAY_BUFFER, data, GL.STATIC_DRAW);
            int stride = FloatsPerVertex * sizeof(float);
            GL.VertexAttribPointer(0, 3, GL.FLOAT, false, stride, 0);
            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(1, 3, GL.FLOAT, false, stride, 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);
            GL.VertexAttribPointer(2, 1, GL.FLOAT, false, stride, 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);
            GL.BindVertexArray(0);
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Unit vector of RA/Dec (radians) in the catalogue frame.</summary>
    public static Vec3 Direction(float ra, float dec) => new(MathF.Cos(dec) * MathF.Cos(ra), MathF.Sin(dec), -MathF.Cos(dec) * MathF.Sin(ra));

    /// <summary>Ballesteros (2012): T = 4600 K (1/(0.92 B−V + 1.7) + 1/(0.92 B−V + 0.62)).</summary>
    public static double TemperatureOfColourIndex(double bv) => 4600 * (1 / (.92 * bv + 1.7) + 1 / (.92 * bv + .62));

    /// <summary>Draw the stars around the camera at the far plane; state as the dome's.</summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 camPos, float time, float farPlane, int viewportHeight, float strength = 1)
    {
        GL.DepthMask(false);
        GL.Disable(GL.DEPTH_TEST);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE);
        GL.Enable(GL.PROGRAM_POINT_SIZE);
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetMatrix4("uSky", SkyRotation);
        _shader.SetVec3("uCamPos", camPos);
        _shader.SetFloat("uFar", farPlane * 1.05f);
        _shader.SetFloat("uTime", time);
        _shader.SetFloat("uStrength", strength);
        _shader.SetFloat("uPixelScale", Math.Max(.5f, viewportHeight / 1080f));
        GL.BindVertexArray(_vao);
        GL.DrawArrays(GL.POINTS, 0, _count);
        GL.BindVertexArray(0);
        GL.Disable(GL.PROGRAM_POINT_SIZE);
        GL.Disable(GL.BLEND);
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
    }

    /// <summary>The constellation figures as faint lines at the far plane.</summary>
    public void DrawConstellations(Mat4 view, Mat4 proj, Vec3 camPos, float farPlane, float strength = 1)
    {
        float[] lines = SkyCatalog.Lines;
        _linePoints.Clear();
        var colour = new Vec3(.10f, .15f, .22f) * strength;
        float r = farPlane * 1.04f;
        int i = 0;

        while (i < lines.Length)
        {
            int n = (int)lines[i++];
            Vec3? last = null;

            for (int k = 0; k < n; k++)
            {
                Vec3 p = camPos + SkyRotation.TransformDirection(Direction(lines[i] * MathF.PI / 180, lines[i + 1] * MathF.PI / 180)) * r;
                i += 2;

                if (last is { } a)
                {
                    _linePoints.Add(new TrailPoint(a, colour)); 
                    _linePoints.Add(new TrailPoint(p, colour));
                }

                last = p;
            }
        }

        _lines ??= new ColorLineMesh(Math.Max(16, _linePoints.Count)) { Mode = GL.LINES };
        _lines.Update(_linePoints);
        GL.DepthMask(false);
        GL.Disable(GL.DEPTH_TEST);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE);
        _lines.Draw(view, proj);
        GL.Disable(GL.BLEND);
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
    }

    public void Dispose()
    {
        _shader.Dispose();
        _lines?.Dispose();

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

    private const string Vertex = @"#version 330 core
layout(location = 0) in vec3 aDir;
layout(location = 1) in vec3 aColor;
layout(location = 2) in float aSize;
uniform mat4 uView, uProj, uSky;
uniform vec3 uCamPos;
uniform float uFar, uTime, uPixelScale;
out vec3 vColor;
out float vSize;
void main()
{
    vec3 dir = mat3(uSky) * aDir;
    gl_Position = uProj * uView * vec4(uCamPos + dir * uFar, 1.0);
    // a slow, star-specific scintillation on the bright ones only
    float tw = 1.0 - 0.18 * step(3.5, aSize) * (0.5 + 0.5 * sin(uTime * 2.3 + aDir.x * 37.0 + aDir.z * 51.0));
    vColor = aColor * tw;
    vSize = aSize * uPixelScale;
    gl_PointSize = vSize + 2.0;
}";

    private const string Fragment = @"#version 330 core
in vec3 vColor;
in float vSize;
uniform float uStrength;
out vec4 FragColor;
void main()
{
    vec2 d = (gl_PointCoord * 2.0 - 1.0) * (vSize + 2.0) / vSize;
    float r2 = dot(d, d);
    // a sharp core with a soft skirt: a star is a point, the glare is the eye's
    float core = exp(-r2 * 2.2);
    float skirt = exp(-r2 * 0.6) * 0.18;
    if (r2 > 2.6) discard;
    FragColor = vec4(vColor * (core + skirt) * uStrength, 1.0);
}";
}
