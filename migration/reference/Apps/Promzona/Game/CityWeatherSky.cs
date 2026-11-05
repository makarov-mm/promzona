using MakarovPhysics.Renderer;
namespace Promzona.Game;

/// <summary>One sky draw with a slowly advecting cloud deck. Shares its horizon with fog.</summary>
internal sealed class CityWeatherSky : IDisposable
{
    private readonly Mesh _cube;
    private readonly Shader _shader;

    public CityWeatherSky()
    {
        _cube = Primitives.Cube();

        try
        {
            _shader = new Shader(CityShaderSource.Read("Sky.Weather.vert"), CityShaderSource.Read("Sky.Weather.frag"));
        }
        catch
        {
            _cube.Dispose();
            throw;
        }
    }

    public void Draw(Mat4 view, Mat4 projection, Vec3 eye, Vec3 sun, float time, float far, CityAtmosphere weather)
    {
        GL.DepthMask(false);
        GL.Disable(GL.DEPTH_TEST);
        GL.Disable(GL.CULL_FACE);
        GL.Disable(GL.BLEND);

        try
        {
            _shader.Use();
            weather.Apply(_shader);
            _shader.SetMatrix4("uView", view);
            _shader.SetMatrix4("uProj", projection);
            float size = far * 1.1f;
            _shader.SetMatrix4("uModel", Mat4.Translate(eye) * Mat4.Scale(size, size, size));
            _shader.SetVec3("uEye", eye);
            _shader.SetVec3("uSun", sun);
            _shader.SetFloat("uGoldenLight", weather.GoldenLight);
            _shader.SetFloat("uTime", time);
            _cube.Draw();
        }
        finally
        {
            GL.Enable(GL.DEPTH_TEST);
            GL.DepthMask(true);
            GL.Disable(GL.CULL_FACE);
        }
    }

    public void Dispose()
    {
        _cube.Dispose();
        _shader.Dispose();
    }
}
