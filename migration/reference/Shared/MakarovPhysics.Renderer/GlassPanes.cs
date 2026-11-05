namespace MakarovPhysics.Renderer;

/// <summary>
/// A translucent glass box: six panes with a Blinn-Phong specular off two
/// fixed lights and a fresnel edge, so the highlight SLIDES across the
/// panes as the camera orbits - the signal that reads as glass. Promoted
/// from the Brownian vessel (12.08) the moment a third module wanted it:
/// the photoelectric envelope and the discharge tube are clients too.
///
/// The pane normal is recovered per fragment as the dominant axis of the
/// local position, so the mesh carries no normal data; because that trick
/// assumes a box CENTRED AT THE ORIGIN in its own frame, the box centre
/// goes into the shader as a uniform and the mesh keeps world positions.
///
/// Draw it LAST in the frame: blending on, depth-write off, cull off -
/// the interior stays visible through both faces of every pane.
/// </summary>
public sealed class GlassPanes : IDisposable
{
    private const string Vs = @"#version 330 core
layout(location = 0) in vec3 aPos;
layout(location = 1) in vec3 aColor;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vWorld;
out vec3 vTint;
void main()
{
    vWorld = aPos;
    vTint = aColor;
    gl_Position = uProj * uView * vec4(aPos, 1.0);
}";

    private const string Fs = @"#version 330 core
in vec3 vWorld;
in vec3 vTint;
uniform vec3 uEye;
uniform vec3 uCentre;
uniform vec3 uHalf;
out vec4 FragColor;
void main()
{
    vec3 local = (vWorld - uCentre) / uHalf; // unit box frame
    vec3 a = abs(local);
    vec3 n = a.x > a.y && a.x > a.z ? vec3(sign(local.x), 0.0, 0.0)
           : a.y > a.z ? vec3(0.0, sign(local.y), 0.0)
                       : vec3(0.0, 0.0, sign(local.z));
    vec3 toEye = normalize(uEye - vWorld);
    if (dot(n, toEye) < 0.0) n = -n; // shade the face the camera sees

    vec3 l1 = normalize(vec3(0.45, 0.62, 0.55));
    vec3 l2 = normalize(vec3(-0.55, 0.35, -0.62));
    float spec = 0.85 * pow(max(dot(n, normalize(l1 + toEye)), 0.0), 90.0)
               + 0.35 * pow(max(dot(n, normalize(l2 + toEye)), 0.0), 60.0);

    float fresnel = pow(1.0 - max(dot(n, toEye), 0.0), 3.0);
    float alpha = 0.05 + 0.30 * fresnel;
    vec3 col = vTint * (0.10 + 0.45 * fresnel) + vec3(1.0) * spec;
    FragColor = vec4(col, clamp(alpha + spec, 0.0, 1.0));
}";

    private readonly ColorLineMesh _mesh;
    private readonly Shader _shader;
    private readonly Vec3 _centre, _half;

    public GlassPanes(Vec3 centre, Vec3 half, Vec3 tint)
    {
        _centre = centre;
        _half = half;
        _shader = new Shader(Vs, Fs);
        List<TrailPoint> v = [with(36)];

        void Quad(Vec3 a, Vec3 b, Vec3 c, Vec3 d)
        {
            v.Add(new TrailPoint(a, tint)); v.Add(new TrailPoint(b, tint)); v.Add(new TrailPoint(c, tint));
            v.Add(new TrailPoint(a, tint)); v.Add(new TrailPoint(c, tint)); v.Add(new TrailPoint(d, tint));
        }

        float x = half.X, y = half.Y, z = half.Z;
        Quad(P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1));     // +Z
        Quad(P(1, -1, -1), P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1)); // -Z
        Quad(P(1, -1, 1), P(1, -1, -1), P(1, 1, -1), P(1, 1, 1));     // +X
        Quad(P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)); // -X
        Quad(P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), P(-1, 1, -1));     // +Y
        Quad(P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)); // -Y

        _mesh = new ColorLineMesh(v.Count) { Mode = GL.TRIANGLES };
        _mesh.Update(v);
        return;

        Vec3 P(float sx, float sy, float sz) => centre + new Vec3(sx * x, sy * y, sz * z);
    }

    /// <summary>Blended pass; call after every opaque and additive layer.</summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 eye)
    {
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.SRC_ALPHA, GL.ONE_MINUS_SRC_ALPHA);
        GL.DepthMask(false);
        GL.Disable(GL.CULL_FACE);

        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetVec3("uEye", eye);
        _shader.SetVec3("uCentre", _centre);
        _shader.SetVec3("uHalf", _half);
        _mesh.DrawRaw();

        GL.Enable(GL.CULL_FACE);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
    }

    public void Dispose()
    {
        _mesh.Dispose();
        _shader.Dispose();
    }
}
