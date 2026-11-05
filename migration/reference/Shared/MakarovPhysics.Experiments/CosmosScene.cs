using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
public enum CosmicSurface
{
    Rock,
    Ocean,
    Gas,
    Star,
    Ice,
    Cmb,
    Solar,
    Interior,
    Moon,
    Oscillation,
    Neutron,
    /// <summary>Rust plains, dark basins, polar caps; MOLA relief and Viking colour when the maps are present. Thin dusty atmosphere.</summary>
    Mars,
    /// <summary>Bare cratered rock; the MESSENGER mosaic when present. No atmosphere.</summary>
    Mercury,
    /// <summary>An unbroken pale cloud deck; the Magellan map when present. Thick atmosphere.</summary>
    Venus,
    /// <summary>Zonal bands with the Great Red Spot; the Cassini map when present.</summary>
    Jupiter,
    /// <summary>Soft pale bands; the Cassini map when present. Rings are drawn by the module (W5).</summary>
    Saturn,
    /// <summary>A nearly featureless cyan; the Voyager map when present.</summary>
    Uranus,
    /// <summary>Deep blue with a dark spot and bright streaks; the Voyager map when present.</summary>
    Neptune,
    /// <summary>Opaque ochre photochemical haze; illustrative visible-light appearance, not a surface map.</summary>
    Titan
}

public readonly record struct CosmicVolume(Vec3 Center, Vec3 Size, int Kind, Vec3 Color, float Density, float Phase, float Cut = 0);
public readonly record struct CosmicBody(Vec3 Center, Vec3 Size, Vec3 Color, CosmicSurface Surface, float Rotation, Vec3 Sun, float Detail, float Cut, float Tilt = 0, float Irradiance = 1, float Azimuth = 0)
{
    /// <summary>Self-luminous surfaces neither receive shadows nor cast them on others.</summary>
    public bool Emissive => Surface is CosmicSurface.Star or CosmicSurface.Solar or CosmicSurface.Cmb or CosmicSurface.Oscillation or CosmicSurface.Neutron or CosmicSurface.Interior;
    /// <summary>Surfaces drawn with a scattering shell (W3/W4).</summary>
    public bool HasAtmosphere => Surface is CosmicSurface.Ocean or CosmicSurface.Gas or CosmicSurface.Mars or CosmicSurface.Venus or CosmicSurface.Jupiter or CosmicSurface.Saturn or CosmicSurface.Uranus or CosmicSurface.Neptune or CosmicSurface.Titan;
}
/// <summary>A sphere that blocks the scene's light: a body of the scene itself,
/// or one the module draws by other means (a Globe). <paramref name="Atmosphere"/>
/// 0..1 reddens its umbra the way the Earth's does the Moon's.</summary>
public readonly record struct CosmicOccluder(Vec3 Center, float Radius, float Atmosphere = 0);
/// <summary>A ring sheet around <paramref name="Center"/> in the plane normal to
/// <paramref name="Axis"/>, from <paramref name="Inner"/> to <paramref name="Outer"/>
/// (display units). <paramref name="Profile"/>: 0 Saturn's measured edges scaled
/// to the span, 1 a smooth sheet, 2 narrow ringlets (CosmicRings.ProfileGlsl).</summary>
public readonly record struct CosmicRing(Vec3 Center, Vec3 Axis, float Inner, float Outer, Vec3 Tint, int Profile = 0);
/// <summary>Frame-local geometry. All coordinates are display units; module models
/// own the conversion from km, AU or parsecs. Seeded sampling never flickers.</summary>
public sealed class CosmosScene
{
    public readonly List<CosmicBody> Bodies = [];
    public readonly List<CosmicVolume> Volumes = [];
    /// <summary>Occluders the module adds by hand (a Globe drawn outside the
    /// painter, say). Bodies of the scene shadow each other on their own
    /// under the point light - see <see cref="AutoOccluders"/>.</summary>
    public readonly List<CosmicOccluder> Occluders = [];
    public readonly List<CosmicRing> Rings = [];
    /// <summary>Small irregular rocks drawn in one instanced call (W8): belt
    /// asteroids, debris, ejecta. A body that fills the screen stays a Body.</summary>
    public readonly List<RockInstance> Rocks = [];
    /// <summary>The scene's light as a sphere, for soft shadows and a
    /// per-fragment sun direction. Null picks the first Star or Solar body
    /// (<see cref="AutoLight"/>); a module whose Sun is a StarBody outside the
    /// painter sets it by hand. A body takes the point light only when its own
    /// Sun direction agrees with the direction to it (within about 30°) - a
    /// decorative star in the corner does not relight an inset lit from the
    /// side; such a body keeps its directional light and casts no shadow.</summary>
    public Vec3? LightPosition;
    public float LightRadius;
    public bool AutoLight = true;
    /// <summary>Non-emissive bodies lit by the point light shadow each other
    /// (a moon behind its planet, a transit across the star's face is depth,
    /// not shadow, so the star needs nothing). Explicit <see cref="Occluders"/>
    /// act in any case.</summary>
    public bool AutoOccluders = true;
    /// <summary>Angular radius of the light as seen from the bodies, radians;
    /// 0 takes it from the light's size and distance. A module that draws its
    /// Sun nearer and larger than it is passes the true 0.004652 so the umbra
    /// keeps its real length (total where it should be total).</summary>
    public float LightAngularRadius;
    public readonly List<TrailPoint> Lines = [], Stars = [], Gas = [];
    public readonly DioramaGeometry Geometry = new()
    {
        CaptureSurface = true
    };
    public bool Guides = true;
    public bool StellarHalos = true; // A cutaway can hide the centre-plane halo independently of its surface.
    /// <summary>Lens flare of the Star/Solar bodies (W7): 0 off, 0.3 a touch, 1 a camera into the Sun.</summary>
    public float SunGlare = .3f;
    public bool HardwareShadows;
    public bool HardwareWorkshop;
    // Optional immutable, GPU-cached landscape. The offset follows the local
    // display origin while SurfaceVertex.Local stays fixed in terrain space.
    public IReadOnlyList<SurfaceVertex>? Landscape;
    public Vec3 LandscapeOffset;
    public Vec3 HardwareSun = new Vec3(-.4f,.7f,.6f).Normalized();
    public float HardwareAmbient = .42f, HardwareFill = 1.25f;
    public float ShadowRadius = 12;

    public float SurfacePhase;
    public float DustTorusDepth;
    /// <summary>Placement of the dust torus (W9): centre and size factor.</summary>
    public Vec3 DustTorusCenter; public float DustTorusScale = 1;
    public float GranulationScale, GranulationPhase, GranulationContrast = 1;
    /// <summary>Placement of the granulation patch (W9): centre and size factor.</summary>
    public Vec3 GranulationCenter; public float GranulationExtent = 1;
    // Opt-in floating-point emission for body-free diffuse galaxy scenes.
    public bool DiffuseGalaxy;
    public bool FloatingEmission;
    public float GuideWidth; // Screen pixels; zero preserves the original line path.
    public float GasRadius = .13f, StarRadius = .026f;
    public void Clear()
    {
        GuideWidth=0;DiffuseGalaxy=false;FloatingEmission=false;StellarHalos=true;SunGlare=.3f;
        Bodies.Clear();
        Volumes.Clear();
        Occluders.Clear();
        Rings.Clear();
        Rocks.Clear();
        LightPosition=null; LightRadius=0; AutoLight=true; AutoOccluders=true; LightAngularRadius=0;
        Lines.Clear();
        Stars.Clear();
        Gas.Clear();
        Geometry.Clear();
        Geometry.BevelBoxes=false;
        HardwareShadows=false; HardwareWorkshop=false;
        Landscape=null; LandscapeOffset=Vec3.Zero;
        HardwareSun=new Vec3(-.4f,.7f,.6f).Normalized();
        HardwareAmbient=.42f; HardwareFill=1.25f; ShadowRadius=12;
        DustTorusDepth = 0; DustTorusCenter = Vec3.Zero; DustTorusScale = 1;
        GranulationScale = 0; GranulationCenter = Vec3.Zero; GranulationExtent = 1; GranulationContrast = 1;
    }

    public void Occluder(Vec3 center, float radius, float atmosphere = 0) => Occluders.Add(new(center, radius, atmosphere));
    /// <summary>A ring sheet; the body at <paramref name="center"/> shadows it and
    /// is shadowed by it. Saturn's rings span 1.11..2.33 of its equatorial radius.</summary>
    public void Ring(Vec3 center, Vec3 axis, float inner, float outer, Vec3? tint = null, int profile = 0) => Rings.Add(new(center, axis, inner, outer, tint ?? new Vec3(.86f, .79f, .63f), profile));
    public void Rock(Vec3 center, float radius, float seed, Vec3? tint = null) => Rocks.Add(new(center, radius, seed, tint ?? new Vec3(.52f, .47f, .40f)));
    public void Light(Vec3 position, float radius, float angularRadius = 0) { LightPosition = position; LightRadius = radius; LightAngularRadius = angularRadius; }
    public void Volume(Vec3 center, Vec3 size, int kind, Vec3 color, float density = 1, float phase = 0, float cut = 0) => Volumes.Add(new(center, size, kind, color, density, phase, cut));
    public void Body(Vec3 center, float radius, CosmicSurface surface, Vec3 color, double rotation = 0, double detail = 1, Vec3? sun = null, float cut = 0, float flattening = 1, float tilt = 0, float irradiance = 1, float azimuth = 0) => Bodies.Add(new(center, new(radius, radius * flattening, radius), color, surface, (float)rotation, sun ?? new Vec3(-.7f, .35f, .8f).Normalized(), (float)detail, cut, tilt, irradiance, azimuth));
    public void Line(Vec3 a, Vec3 b, Vec3 color, bool guide = true)
    {
        if (guide && !Guides)
            return;
        Lines.Add(new(a, color));
        Lines.Add(new(b, color));
    }

    public void Curve(Func<double, Vec3> f, Vec3 color, int segments = 180, bool guide = true)
    {
        var last = f(0);
        for (int i = 1; i <= segments; i++)
        {
            var next = f((double)i / segments);
            Line(last, next, color, guide);
            last = next;
        }
    }

    public void Orbit(double radius, Vec3 color, double inclination = 0, Vec3? center = null) => Curve(t => (center ?? Vec3.Zero) + P(radius * Math.Cos(t * Math.Tau), radius * Math.Sin(t * Math.Tau) * Math.Sin(inclination), radius * Math.Sin(t * Math.Tau) * Math.Cos(inclination)), color);
    public static Vec3 P(double x, double y, double z) => new((float)x, (float)y, (float)z);
    public static double Noise(int index, int salt = 0)
    {
        uint x = unchecked((uint)(index * 747796405 + salt * 2891336453L + 277803737));
        x = ((x >> (int)((x >> 28) + 4)) ^ x) * 277803737;
        x = (x >> 22) ^ x;
        return x / 4294967296d;
    }

    public static Vec3 Direction(int index, int salt = 0)
    {
        double y = 2 * Noise(index, salt) - 1, phi = Math.Tau * Noise(index, salt + 1), r = Math.Sqrt(1 - y * y);
        return P(r * Math.Cos(phi), y, r * Math.Sin(phi));
    }

    public void Dust(Vec3 p, Vec3 color, float strength = 1) => Gas.Add(new(p, color * strength));
    public void Star(Vec3 p, Vec3 color, float strength = 1) => Stars.Add(new(p, color * strength));
    public void Spacecraft(Vec3 p, float size = .15f)
    {
        var g = Geometry;
        g.Material = SurfaceMaterial.Steel;
        g.Box(p, new(size * .6f, size * .45f, size), new(.72f, .75f, .78f));
        g.Material = SurfaceMaterial.Copper;
        g.Box(p + new Vec3(0, 0, size * .5f), new(size * .61f, size * .46f, size * .18f), new(.73f, .48f, .14f));
        g.Material = SurfaceMaterial.Steel;
        g.Box(p + new Vec3(0, -size * .48f, 0), new(size * .53f, size * .025f, size * .85f), new(.17f, .20f, .24f));
        for (int i = -3; i <= 3; i++)
            g.Tube(p + new Vec3(-size * .58f, size * .47f, i * size * .25f), p + new Vec3(size * .58f, size * .47f, i * size * .25f), size * .014f, new(.9f, .73f, .31f));
        g.Tube(p, p + new Vec3(0, size * 1.35f, 0), size * .035f, new(.76f, .80f, .87f));
        for (int ring = 0; ring < 5; ring++)
        {
            float r = size * (.11f + ring * .075f), y = size * (.87f + ring * ring * .02f);
            Curve(t => p + P(r * Math.Cos(t * Math.Tau), y, r * Math.Sin(t * Math.Tau)), new(.68f, .72f, .8f), 24, false);
        }

        for (int sign = -1; sign <= 1; sign += 2)
        {
            g.Material = SurfaceMaterial.Steel;
            g.Tube(p, p + new Vec3(sign * size * 2.8f, 0, 0), size * .07f, new(.65f, .68f, .73f));
            g.Material = SurfaceMaterial.Enamel;
            g.Box(p + new Vec3(sign * size * 2.1f, 0, 0), new(size * 1.02f, size * .035f, size * 1.17f), new(.48f, .53f, .61f));
            for (int x = 0; x < 6; x++)
                for (int z = 0; z < 8; z++)
                {
                    float blue = (float)Noise(x * 8 + z, 301);
                    g.Box(p + new Vec3(sign * size * 2.1f + (x - 2.5f) * size * .327f, size * .045f, (z - 3.5f) * size * .284f), new(size * .151f, size * .01f, size * .131f), new(.025f + .02f * blue, .08f + .025f * blue, .24f + .1f * blue));
                }

            for (int j = -2; j <= 2; j++)
                Line(p + new Vec3(sign * size * 2.1f + j * size * .4f, size * .04f, -size * 1.15f), p + new Vec3(sign * size * 2.1f + j * size * .4f, size * .04f, size * 1.15f), new(.15f, .37f, .69f), false);
        }
    }
}
