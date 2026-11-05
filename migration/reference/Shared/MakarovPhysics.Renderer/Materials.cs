namespace MakarovPhysics.Renderer;

/// <summary>
/// The material library (03.08, Michael's request off the DC-motor
/// screenshots): procedural surfaces every module can adopt in a couple of
/// lines. Wood for benches and stands, brushed metal for magnets, frames and
/// fittings. Both are FUNCTIONS, not images - no assets to load, nothing for
/// a delta archive to clobber, infinite resolution - and both shade through
/// the exact shadePbr the plain lit path uses, so a wooden bench and a lit
/// copper coil live under one light without a seam.
///
/// Adoption pattern (see ElectricMotorModule for the live example):
///   _wood = Materials.Wood();                      // Initialize
///   Materials.ApplyCamera(_wood, view, proj, eye); // per frame
///   Materials.ApplyLight(_wood, dir, colour, ambient, env, skyTint);
///   _wood.SetMatrix4("uModel", ...); mesh.Draw();
/// Knobs (optional): uWoodScale / uBrushScale, uBumpStrength, uMetalTint.
/// </summary>
public static class Materials
{
    public static Shader Wood() => new(DefaultShaders.SurfaceVertex, DefaultShaders.WoodFragment);
    public static Shader BrushedMetal() => new(DefaultShaders.SurfaceVertex, DefaultShaders.BrushedMetalFragment);

    /// <summary>Camera block, shared by every material shader.</summary>
    public static void ApplyCamera(Shader shader, Mat4 view, Mat4 proj, Vec3 eye)
    {
        shader.Use();
        shader.SetMatrix4("uView", view);
        shader.SetMatrix4("uProj", proj);
        shader.SetVec3("uViewPos", eye);
    }

    /// <summary>Light block - the same uniforms the lit shader takes, so a
    /// module states its light ONCE and hands it to every material.</summary>
    public static void ApplyLight(Shader shader, Vec3 lightDir, Vec3 lightColor, float ambient, float envIntensity = 0f, float skyTint = 0f)
    {
        shader.Use();
        shader.SetVec3("uLightDir", lightDir);
        shader.SetVec3("uLightColor", lightColor);
        shader.SetFloat("uAmbient", ambient);
        shader.SetFloat("uEnvIntensity", envIntensity);
        shader.SetFloat("uSkyTint", skyTint);
    }
}
