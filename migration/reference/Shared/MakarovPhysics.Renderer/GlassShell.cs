namespace MakarovPhysics.Renderer;

/// <summary>
/// The additive Fresnel "glass shell" pass - a body filled faintly and rimmed
/// brightly where the surface grazes the view, drawn with ONE,ONE blending
/// after everything opaque so whatever is inside stays visible.
///
/// Promoted to the Renderer on 11.08: by then FOUR modules (Viscosity,
/// FranckHertz, ThomsonEm, the Kundt tube) carried byte-identical private
/// copies of the same fragment shader and the same state juggling, and two
/// more were queued to want it. Six copies of a shader is how one of them
/// drifts.
///
/// Usage:
///     _glass.Begin(view, proj, eye);          // shader + additive state
///     _glass.Draw(model, tint, 0.05f, 0.55f); // the built-in cylinder
///     _glass.Draw(myMesh, model, tint, b, f); // or any mesh
///     _glass.End();                           // state restored
/// </summary>
public sealed class GlassShell : IDisposable
{
    private const string ShellFragment = @"#version 330 core
in vec3 vWorldPos;
in vec3 vNormal;
uniform vec3 uViewPos;
uniform vec3 uTint;
uniform float uBase; // view-independent fill
uniform float uFres; // silhouette weight
out vec4 FragColor;
void main()
{
    vec3 N = normalize(vNormal);
    vec3 V = normalize(uViewPos - vWorldPos);
    float mu = abs(dot(N, V));
    float fres = pow(1.0 - mu, 3.0);
    FragColor = vec4(uTint * (uBase + uFres * fres), 1.0);
}";

    private readonly Shader _shader = new(DefaultShaders.LitVertex, ShellFragment);

    /// <summary>A 36-segment uncapped cylinder - the shell every current
    /// consumer wraps around a tube or column. Owned here so four modules
    /// stop allocating identical geometry.</summary>
    public Mesh Cylinder { get; } = Primitives.Cylinder(36, capped: false);

    /// <summary>Binds the shader, uploads the camera, and switches to the
    /// additive no-depth-write no-cull state the shell needs. Call after the
    /// opaque scene; pair with <see cref="End"/>.</summary>
    public void Begin(in Mat4 view, in Mat4 proj, Vec3 eye)
    {
        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        _shader.SetVec3("uViewPos", eye);

        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE);
        GL.DepthMask(false);
        GL.Disable(GL.CULL_FACE);
    }

    /// <summary>One shall pass with the built-in cylinder.</summary>
    public void Draw(in Mat4 model, Vec3 tint, float baseFill, float fresnel) => Draw(Cylinder, model, tint, baseFill, fresnel);

    /// <summary>One shall pass with any mesh.</summary>
    public void Draw(Mesh mesh, in Mat4 model, Vec3 tint, float baseFill, float fresnel)
    {
        // defensive: a caller may have bound another program between Begin
        // and this Draw (a label pass, a debug overlay); re-binding is cheap
        _shader.Use();
        _shader.SetMatrix4("uModel", model);
        _shader.SetVec3("uTint", tint);
        _shader.SetFloat("uBase", baseFill);
        _shader.SetFloat("uFres", fresnel);
        mesh.Draw();
    }

    /// <summary>Restores the state <see cref="Begin"/> changed.</summary>
    public void End()
    {
        GL.Enable(GL.CULL_FACE);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
    }

    public void Dispose()
    {
        Cylinder.Dispose();
        _shader.Dispose();
    }
}
