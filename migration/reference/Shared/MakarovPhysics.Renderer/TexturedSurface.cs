using System.Drawing;

namespace MakarovPhysics.Renderer;

/// <summary>
/// A surface from a SCANNED albedo/bump pair: the benches and stands the
/// modules put their apparatus on, the plaster wall behind them, the steel
/// plate under a heavy rig. One class, one shader, three map folders.
///
/// It began as WoodBench and was renamed when the wall arrived (14.08): a
/// class that draws plaster should not have "wood" in its name, and the only
/// thing wooden about it was the folder it was pointed at.
///
/// Why this exists next to <see cref="Materials.Wood"/>: the procedural board
/// is a function of position, and a function has to invent plank seams and
/// grain at whatever scale it is asked for. It holds up around a metre and
/// falls apart on either side - on a 26 cm tray the seams become blotches, on
/// a two-metre ramp they become stripes of paint (screenshots, 14.08).
///
/// The FIRST attempt at a photograph failed too, and for a reason worth
/// keeping: it reached for Textures/Sandbox/cart_wood, which is the map of a
/// CRATE - boards across, with end grain and cleats. Tiled eight times a metre
/// that is roof tiles, and the bench got worse than the procedural one it
/// replaced. The map now used (Textures/Wood, ambientCG Wood092, CC0) is plain
/// planed board with no seams of its own, so it repeats without a pattern at
/// any scale. A texture is not automatically better than a function; the RIGHT
/// texture is.
///
/// The maps are TILED (Texture is asked for REPEAT), projected triplanarly in
/// world space, and the bump map is sampled rather than ignored - three taps
/// give the surface gradient and the normal is bent by it, so the grain
/// catches the light instead of lying there like printed paper.
///
/// Usage, three lines, the same shape as Materials:
///     _bench = new TexturedSurface(context.Shared.WoodTexturesDirectory);
///     _bench.Begin(view, proj, eye, lightDir, lightColor, ambient);
///     _bench.Draw(cube, model, scale: 4f);
///
/// A missing file is not an error: <see cref="Available"/> goes false and the
/// caller keeps whatever it drew before. A bench that cannot find its texture
/// is a cosmetic loss, not a reason to take a module down.
/// </summary>
public sealed class TexturedSurface : IDisposable
{
    private readonly Shader? _shader;
    private readonly Texture? _albedo;
    private readonly Texture? _bump;

    /// <summary>Both maps loaded and the program built.</summary>
    public bool Available => _shader is not null && _albedo is not null && _bump is not null;

    /// <param name="texturesDirectory">Textures/Wood, /Wall or /Metal, from
    /// AppPaths.</param>
    /// <param name="stem">Map pair to wear: "bench" (wood), "wall" (plaster),
    /// "plate" (steel).</param>
    public TexturedSurface(string texturesDirectory, string stem = "bench")
    {
        try
        {
            _albedo = Load(texturesDirectory, stem + "_albedo.png");
            _bump = Load(texturesDirectory, stem + "_bump.png");

            if (_albedo is not null && _bump is not null)
            {
                _shader = new Shader(DefaultShaders.SurfaceVertex, DefaultShaders.TexturedWoodFragment);
            }
        }
        catch
        {
            // a broken PNG or a driver that refuses the upload must not take
            // the module with it - the bench simply is not Available
            _albedo?.Dispose();
            _bump?.Dispose();
            _albedo = null;
            _bump = null;
            _shader?.Dispose();
            _shader = null;
        }
    }

    private static Texture? Load(string directory, string file)
    {
        if (string.IsNullOrEmpty(directory)) return null;
        string path = Path.Combine(directory, file);
        if (!File.Exists(path)) return null;
        using var bitmap = new Bitmap(path);
        var texture = new Texture(bitmap);
        texture.SetWrapRepeat(); // triplanar tiles; the default clamp would smear
        return texture;
    }

    /// <summary>Binds the program and states the light, exactly like the other
    /// materials, so a module declares its lighting once for the whole bench.</summary>
    public void Begin(Mat4 view, Mat4 proj, Vec3 eye, Vec3 lightDir, Vec3 lightColor,
        float ambient, float envIntensity = 0.6f, float skyTint = 0.3f)
    {
        if (!Available) return;
        Materials.ApplyCamera(_shader!, view, proj, eye);
        Materials.ApplyLight(_shader!, lightDir, lightColor, ambient, envIntensity, skyTint);
        _shader!.Use();
        _albedo!.Bind();
        _bump!.Bind(1);
        _shader.SetInt("uAlbedoMap", 0);
        _shader.SetInt("uBumpMap", 1);
    }

    /// <summary>
    /// Hand the board a shadow map (06.10), the same call RodChainScene has:
    /// the triplanar fragment shades through shadePbr, which already samples
    /// uShadowMap when uShadowStrength is set - the surface only never had a
    /// way to say so, and a bench whose apparatus casts no shadow on it is
    /// the first thing that reads as "modelled". Call after <see cref="Begin"/>.
    /// </summary>
    public void UseShadowMap(ShadowMap map, Mat4 lightSpace, float strength = 0.75f, int unit = 3)
    {
        if (!Available) return;
        _shader!.Use();
        _shader.SetMatrix4("uLightSpace", lightSpace);
        _shader.SetFloat("uShadowStrength", strength);
        _shader.SetInt("uShadowMap", unit);
        GL.ActiveTexture(GL.TEXTURE0 + (uint)unit);
        GL.BindTexture(GL.TEXTURE_2D, map.DepthTexture);
        GL.ActiveTexture(GL.TEXTURE0);
    }

    /// <param name="scale">Repeats per metre. A bench top wants the boards a
    /// palm wide, so 4 to 8; a two-metre ramp wants 2. This is the one number
    /// a caller has to think about, and it means something.</param>
    /// <param name="tint">Zero leaves the photograph alone; a colour warms or
    /// darkens it without turning it into paint.</param>
    /// <param name="texOrigin">World anchor of the pattern. Leave at zero for
    /// anything that stands still - a bench, a wall, a floor slab - and the
    /// grain belongs to the room. Pass a MOVING body's centre and the mapping
    /// travels with it instead of swimming across its faces (22.08, the stone
    /// load of Pulleys rises a metre on every haul).</param>
    public void Draw(Mesh mesh, Mat4 model, float scale = 4f, Vec3 tint = default, float bumpStrength = 0.6f, Vec3 texOrigin = default)
    {
        if (!Available) return;
        _shader!.Use();
        _shader.SetFloat("uTexScale", scale);
        _shader.SetFloat("uBumpStrength", bumpStrength);
        _shader.SetVec3("uTint", tint);
        _shader.SetVec3("uTexOrigin", texOrigin);
        _shader.SetMatrix4("uModel", model);
        mesh.Draw();
    }

    public void Dispose()
    {
        _albedo?.Dispose();
        _bump?.Dispose();
        _shader?.Dispose();
    }
}
