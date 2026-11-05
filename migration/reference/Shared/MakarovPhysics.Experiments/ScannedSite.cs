using MakarovPhysics.Base;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>The photographed map pairs shipped in Textures/ (ambientCG, CC0), by what they look like.</summary>
public enum ScanMap
{
    /// <summary>Planed board (Textures/Wood, "bench").</summary>
    Wood,
    /// <summary>Plaster (Textures/Wall, "wall").</summary>
    Plaster,
    /// <summary>Quarried slate (Textures/Stone, "stone").</summary>
    Stone,
    /// <summary>Fired clay bricks (Textures/Brick, "brick").</summary>
    Brick,
    /// <summary>Rusted iron (Textures/Sandbox, "rusty_metal").</summary>
    RustyIron,
    /// <summary>Rough crate boards (Textures/Sandbox, "cart_wood").</summary>
    CrateWood,
}

/// <summary>
/// Scenery of an <see cref="EngineeringSiteExhibit"/> that wears a photographed map instead of a procedural finish
/// (30.10.2026). A module adds geometry per map in <see cref="EngineeringSiteExhibit.BuildScannedSite"/>; the site
/// uploads each map's triangles to one <see cref="ScannedSurface"/>, draws them in its own depth and colour passes,
/// and - when a map is not shipped or does not load - hands the same triangles to the procedural site with the
/// fallback material, so a missing photograph changes the finish and never the scene.
/// </summary>
public sealed class ScannedSite : IDisposable
{
    private sealed class Layer
    {
        public required ScanMap Map;
        public readonly DioramaGeometry Geometry = new() { CaptureSurface = true };
        public SurfaceMaterial Fallback;
        public float Scale, Bump, Roughness, Metal, Specular;
        public ScannedSurface? Surface;
    }

    private readonly AppPaths _paths;
    private readonly Dictionary<ScanMap, Layer> _layers = [];
    private readonly Dictionary<ScanMap, ScannedSurface> _loaded = [];

    public ScannedSite(AppPaths paths) => _paths = paths;

    /// <summary>Geometry for one map. Several calls for the same map add to one batch; the first sets its finish.
    /// <paramref name="repeatsPerMetre"/> is the scale of the photograph, <paramref name="fallback"/> the procedural
    /// finish used when the map is missing. Colours are tints: 0.5 grey is the photograph itself.</summary>
    public void Add(ScanMap map, float repeatsPerMetre, SurfaceMaterial fallback, Action<DioramaGeometry> build,
        float bump = .7f, float roughness = .84f, float metal = 0, float specular = .2f)
    {
        if (!_layers.TryGetValue(map, out var layer))
        {
            layer = new Layer { Map = map, Fallback = fallback, Scale = repeatsPerMetre, Bump = bump, Roughness = roughness, Metal = metal, Specular = specular };
            _layers[map] = layer;
        }

        layer.Geometry.Material = fallback;
        build(layer.Geometry);
    }

    internal void Begin()
    {
        foreach (var layer in _layers.Values) layer.Surface = null;
        _layers.Clear();
    }

    /// <summary>Uploads every layer whose map loaded; the others go into the procedural site geometry.</summary>
    internal void Commit(DioramaGeometry site)
    {
        foreach (var layer in _layers.Values)
        {
            var surface = Surface(layer.Map);
            if (surface is { Available: true } && layer.Geometry.SurfacePoints.Count > 0)
            {
                surface.Scale = layer.Scale;
                surface.BumpStrength = layer.Bump;
                surface.Roughness = layer.Roughness;
                surface.Metal = layer.Metal;
                surface.Specular = layer.Specular;
                surface.Update(layer.Geometry.SurfacePoints);
                layer.Surface = surface;
            }
            else
            {
                // the procedural finish of the fallback material, with the tint brought back to an albedo
                foreach (var v in layer.Geometry.SurfacePoints)
                    site.SurfacePoints.Add(v with { Color = v.Color * 1.6f, Material = layer.Fallback });
            }
        }

        foreach (var (map, surface) in _loaded)
            if (!_layers.TryGetValue(map, out var used) || used.Surface is null)
                surface.Update([]);
    }

    private ScannedSurface? Surface(ScanMap map)
    {
        if (_loaded.TryGetValue(map, out var s)) return s;
        var (directory, stem) = map switch
        {
            ScanMap.Wood => (_paths.WoodTexturesDirectory, "bench"),
            ScanMap.Plaster => (_paths.WallTexturesDirectory, "wall"),
            ScanMap.Stone => (_paths.StoneTexturesDirectory, "stone"),
            ScanMap.Brick => (_paths.BrickTexturesDirectory, "brick"),
            ScanMap.RustyIron => (_paths.SandboxTexturesDirectory, "rusty_metal"),
            _ => (_paths.SandboxTexturesDirectory, "cart_wood"),
        };
        s = new ScannedSurface(directory, stem);
        _loaded[map] = s;
        return s;
    }

    internal void DrawDepth(Mat4 lightSpace)
    {
        foreach (var layer in _layers.Values) layer.Surface?.DrawDepth(lightSpace);
    }

    internal void Draw(Mat4 view, Mat4 projection, Vec3 eye, Vec3 light, Vec3 lightColor, Mat4 lightSpace, uint shadow,
        OutdoorLighting? lighting, float environmentFill)
    {
        foreach (var layer in _layers.Values)
        {
            if (layer.Surface is not { } s) continue;
            s.Lighting = lighting;
            s.Draw(view, projection, eye, light, lightColor, lightSpace, shadow, .52f, environmentFill);
        }
    }

    public void Dispose()
    {
        foreach (var s in _loaded.Values) s.Dispose();
        _loaded.Clear();
        _layers.Clear();
    }
}
