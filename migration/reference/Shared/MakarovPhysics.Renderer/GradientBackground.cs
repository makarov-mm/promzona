namespace MakarovPhysics.Renderer;

/// <summary>
/// Fullscreen vertical gradient drawn before the scene: the deep blue-black
/// laboratory backdrop from the reference renderers, replacing the host's
/// engineering grid behind volumetric content. One oversized NDC triangle,
/// depth writes off. Promoted to the shared renderer once a second module
/// needed it; construction is transactional.
/// </summary>
public sealed class GradientBackground : IDisposable
{
    private const string Vertex = @"#version 330 core
layout(location = 0) in vec3 aPos;
out vec2 vUv;
void main()
{
    vUv = aPos.xy * 0.5 + 0.5;
    gl_Position = vec4(aPos.xy, 0.0, 1.0);
}";

    private const string Fragment = @"#version 330 core
in vec2 vUv;
out vec4 FragColor;
uniform vec3 uBottomColor;
uniform vec3 uTopColor;
void main()
{
    FragColor = vec4(mix(uBottomColor, uTopColor, vUv.y), 1.0);
}";

    private readonly Mesh _triangle;
    private readonly Shader _shader;

    public Vec3 BottomColor = new(0.015f, 0.018f, 0.030f);
    public Vec3 TopColor = new(0.045f, 0.060f, 0.095f);

    public GradientBackground()
    {
        Mesh? triangle = null;
        Shader? shader = null;

        try
        {
            // one triangle covering the whole screen in NDC (classic 3-vertex
            // fullscreen trick); the position doubles as the UV source
            float[] vertices =
            [
                -1f, -1f, 0f, 0f, 0f, 1f,
                 3f, -1f, 0f, 0f, 0f, 1f,
                -1f,  3f, 0f, 0f, 0f, 1f,
            ];

            triangle = new Mesh(vertices, [0u, 1u, 2u]);
            shader = new Shader(Vertex, Fragment);
        }
        catch
        {
            triangle?.Dispose();
            shader?.Dispose();
            throw;
        }

        _triangle = triangle;
        _shader = shader;
    }

    public void Draw()
    {
        GL.DepthMask(false);
        GL.Disable(GL.DEPTH_TEST);

        _shader.Use();
        _shader.SetVec3("uBottomColor", BottomColor);
        _shader.SetVec3("uTopColor", TopColor);
        _triangle.Draw();

        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
    }

    public void Dispose()
    {
        _triangle.Dispose();
        _shader.Dispose();
    }
}
