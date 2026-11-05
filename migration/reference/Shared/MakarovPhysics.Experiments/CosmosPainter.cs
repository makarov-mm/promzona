using MakarovPhysics.Numerics;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Procedural, locally lit celestial surfaces and depth-tested optically
/// thin emission. Textures illustrate morphology; models supply measured quantities.</summary>
public sealed class CosmosPainter : IDisposable
{
    private PostFxStack? _galaxyLight, _particleLight, _bodyLight;
    private ScientificPlot? _guidePlot;
    private readonly Mesh _sphere;
    private readonly Shader _surface, _depthOnly;
    private GlowPoints _stars, _gas;
    private readonly ColorLineMesh _lines;
    private readonly StudioSurface _hardware;
    private ShadowMap? _hardwareShadow;
    private StudioSurface? _landscape;
    private IReadOnlyList<SurfaceVertex>? _landscapeSource;
    private CosmicDustVolume? _dustVolume;
    private CosmicRings? _rings;
    private InstancedRocks? _rocks;
    private CosmicNebula? _nebula;
    private CosmicPhotosphere? _photosphere;
    private readonly Shader _atmosphere = new(Vertex, AtmosphereFragment);
    private readonly StarBody _halo = new()
    {
        Glow = .9f
    };
    // Surface maps by sampler slot (see MapNames); a missing file leaves its
    // slot empty and the shader falls back to the procedural surface.
    private readonly Texture?[] _maps = new Texture?[MapNames.Length];
    private static readonly (string File, bool Colour)[] MapNames =
    [
        ("earth-blue-marble-july.jpg", true),   // 0  Earth albedo
        ("moon-lroc.jpg", true),                // 1  Moon albedo
        ("moon-lola-height.jpg", false),        // 2  Moon height
        ("earth-black-marble.jpg", true),       // 3  Earth night lights
        ("earth-clouds.jpg", false),            // 4  Earth cloud cover (grey)
        ("earth-topo-bathy.jpg", false),        // 5  Earth height
        ("mars-viking-color.jpg", true),        // 6  Mars albedo
        ("mars-mola-height.jpg", false),        // 7  Mars height
        ("mercury-messenger.jpg", true),        // 8  Mercury
        ("venus-magellan.jpg", true),           // 9  Venus
        ("jupiter-cassini.jpg", true),          // 10 Jupiter
        ("saturn-cassini.jpg", true),           // 11 Saturn
        ("uranus-voyager.jpg", true),           // 12 Uranus
        ("neptune-voyager.jpg", true),          // 13 Neptune
    ];
    private static readonly string[] MapUniforms =
        ["uEarth", "uMoon", "uHeight", "uNight", "uClouds", "uEarthHeight", "uMars", "uMarsHeight", "uMercury", "uVenus", "uJupiter", "uSaturn", "uUranus", "uNeptune"];

    /// <summary>Which of the optional surface maps were found (by file name in
    /// Textures/Astronomy), for the release notes and the ICONS-style ledger.</summary>
    public IEnumerable<string> LoadedMaps => MapNames.Where((m, i) => _maps[i] != null).Select(m => m.File);
    /// <summary>The Blue Marble map as a texture, for a module that draws a flat map in its overlay (GroundTrack, 21.10, tail1543); null when the file is absent.</summary>
    public Texture? EarthMap => _maps[0];
    public CosmosPainter(string texturesDirectory)
    {
        // Colour maps are photographs: sRGB-encoded, decoded by the sampler
        // (19.10 - read as linear and gamma-encoded again, the Earth was pale).
        // The height map is data and stays linear. Anisotropy keeps coastlines
        // sharp towards the limb, where the map is seen at a grazing angle.
        Texture? Load(string name, bool colour)
        {
            string path = Path.Combine(texturesDirectory, "Astronomy", name);
            if (!File.Exists(path))
                return null;
            using var bitmap = new System.Drawing.Bitmap(path);
            var texture = new Texture(bitmap, srgb: colour);
            texture.SetWrapRepeat();
            texture.SetAnisotropy(8);
            return texture;
        }

        for (int i = 0; i < MapNames.Length; i++)
            _maps[i] = Load(MapNames[i].File, MapNames[i].Colour);
        _sphere = Primitives.Sphere(96, 64);
        _surface = new Shader(Vertex, Fragment);
        _depthOnly = new Shader(Vertex, DepthFragment);
        _stars = new GlowPoints(80000);
        _gas = new GlowPoints(80000);
        _lines = new ColorLineMesh(100000)
        {
            Mode = GL.LINES
        };
        _hardware = new StudioSurface();
    }

    /// <summary>
    /// Draw the scene over whatever the caller has on screen.
    ///
    /// With <paramref name="hdr"/> the celestial bodies (and the photosphere
    /// patch) are rendered first in linear HDR through an opaque PostFxStack -
    /// multisampled, with bloom on anything brighter than white, composited
    /// over the backbuffer where the bodies are - and then repeated depth-only
    /// into the backbuffer so the lines, particles, volumes and hardware that
    /// follow still hide behind them. The bodies then see no depth the caller
    /// drew before this call: a module that draws its own solids first and
    /// expects a body to pass behind them keeps hdr off (EclipseGeometry's
    /// Globe, Kepler's star).
    /// </summary>
    public void Draw(CosmosScene scene, Mat4 view, Mat4 projection, Vec3 eye, int height, float exposure, float glow = 1, bool hdr = false)
    {
        if (hdr) DrawBodiesHdr(scene, view, projection, eye, exposure);
        if(!scene.FloatingEmission) {DrawCore(scene,view,projection,eye,height,exposure,glow,hdr);return;}
        uint target=(uint)GL.GetInteger(0x8CA6),read=(uint)GL.GetInteger(0x8CAA);
        var viewport=GL.PhysicalViewport();int scale=GL.RenderScale;bool scissor=GL.IsEnabled(GL.SCISSOR_TEST);
        try
        {
            GL.RenderScale=1;GL.Disable(GL.SCISSOR_TEST);
            _particleLight??=new PostFxStack{Exposure=1,BloomStrength=.10f,BloomThreshold=1.1f,Vignette=0};
            _particleLight.ViewportX=viewport.X;_particleLight.ViewportY=viewport.Y;
            _particleLight.BeginScene(viewport.Width,viewport.Height,0);
            DrawCore(scene,view,projection,eye,height,exposure,glow,hdr);
            _particleLight.EndScene();_particleLight.CompositeTo(target);
        }
        finally
        {
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,target);GL.BindFramebuffer(GL.READ_FRAMEBUFFER,read);
            GL.RenderScale=scale;GL.RestorePhysicalViewport(viewport.X,viewport.Y,viewport.Width,viewport.Height);
            if(scissor)GL.Enable(GL.SCISSOR_TEST);
        }
    }

    /// <summary>Samples of the opaque body target, 0 before the first HDR frame
    /// or when the driver gives none.</summary>
    public int BodyMsaaSamples => _bodyLight?.SceneMsaaSamples ?? 0;

    // The linear-HDR body pass: see Draw. The stack's curve is the one the
    // shader applied itself before (1 - exp(-x·exposure), gamma 2.2), so a
    // planet keeps its look; the Sun, a photosphere and a neutron star's hot
    // spots exceed white and bloom.
    private void DrawBodiesHdr(CosmosScene scene, Mat4 view, Mat4 projection, Vec3 eye, float exposure)
    {
        if (scene.Bodies.Count == 0 && scene.GranulationScale <= 0) return;
        uint target=(uint)GL.GetInteger(0x8CA6),read=(uint)GL.GetInteger(0x8CAA);
        var viewport=GL.PhysicalViewport();int scale=GL.RenderScale;bool scissor=GL.IsEnabled(GL.SCISSOR_TEST);
        try
        {
            GL.RenderScale=1;GL.Disable(GL.SCISSOR_TEST);
            _bodyLight??=new PostFxStack{Opaque=true,BloomStrength=.55f,BloomThreshold=1.0f,BloomCap=2.5f,Vignette=0};
            _bodyLight.Exposure=exposure;
            _bodyLight.ViewportX=viewport.X;_bodyLight.ViewportY=viewport.Y;
            _bodyLight.BeginScene(viewport.Width,viewport.Height,0);
            DrawBodies(scene, view, projection, eye, exposure, hdr: true);
            if (scene.GranulationScale > 0)
                (_photosphere ??= new CosmicPhotosphere()).Draw(view, projection, eye, scene.GranulationScale, scene.GranulationPhase, exposure, hdr: true, center: scene.GranulationCenter, extent: scene.GranulationExtent, contrast: scene.GranulationContrast);
            _bodyLight.EndScene();_bodyLight.CompositeTo(target);
        }
        finally
        {
            GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,target);GL.BindFramebuffer(GL.READ_FRAMEBUFFER,read);
            GL.RenderScale=scale;GL.RestorePhysicalViewport(viewport.X,viewport.Y,viewport.Width,viewport.Height);
            if(scissor)GL.Enable(GL.SCISSOR_TEST);
        }
    }

    private void DrawBodies(CosmosScene scene, Mat4 view, Mat4 projection, Vec3 eye, float exposure, bool hdr)
    {
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
        GL.Disable(GL.CULL_FACE);
        _surface.Use();
        _surface.SetMatrix4("uView", view);
        _surface.SetMatrix4("uProj", projection);
        _surface.SetVec3("uEye", eye);
        _surface.SetFloat("uExposure", exposure);
        _surface.SetFloat("uHdr", hdr ? 1 : 0);
        _surface.SetFloat("uPhase",scene.SurfacePhase);
        for (int i = 0; i < MapNames.Length; i++)
        {
            _surface.SetInt(MapUniforms[i], i);
            _surface.SetFloat($"uHas[{i}]", _maps[i] != null ? 1 : 0);
            _maps[i]?.Bind((uint)i);
        }
        var light = SceneLight(scene);
        for (int i = 0; i < scene.Bodies.Count; i++)
        {
            var body = scene.Bodies[i];
            _surface.SetVec3("uCenter", body.Center);
            _surface.SetVec3("uSize", body.Size);
            _surface.SetVec3("uTint", body.Color);
            _surface.SetVec3("uSun", body.Sun);
            _surface.SetFloat("uRotation", body.Rotation);
            _surface.SetFloat("uKind", (int)body.Surface);
            _surface.SetFloat("uDetail", body.Detail);
            _surface.SetFloat("uCut", body.Cut);
            _surface.SetFloat("uTilt", body.Tilt);
            _surface.SetFloat("uAzimuth", body.Azimuth);
            _surface.SetFloat("uIrradiance", body.Irradiance);
            SetLighting(_surface, scene, i, light);
            SetRingShadow(_surface, scene, body);
            _sphere.Draw();
        }
    }

    // The sheet around this body, for its shadow on the planet (W5).
    private static void SetRingShadow(Shader shader, CosmosScene scene, CosmicBody body)
    {
        shader.Use(); // defensive: the body shader is already bound by DrawBodies
        foreach (var ring in scene.Rings)
            if ((ring.Center - body.Center).Length() < 1e-4f * Math.Max(1, ring.Outer))
            {
                shader.SetFloat("uHasRing", 1);
                shader.SetVec3("uRingAxis", ring.Axis.Normalized());
                shader.SetFloat("uRingInner", ring.Inner);
                shader.SetFloat("uRingOuter", ring.Outer);
                shader.SetInt("uRingProfile", ring.Profile);
                return;
            }
        shader.SetFloat("uHasRing", 0);
    }

    /// <summary>The scene's light as a sphere: explicit, or the first Star/Solar body.</summary>
    private static (Vec3 Pos, float Radius, bool Any) SceneLight(CosmosScene scene)
    {
        if (scene.LightPosition is { } p) return (p, scene.LightRadius, true);
        if (scene.AutoLight)
            foreach (var b in scene.Bodies)
                if (b.Surface is CosmicSurface.Star or CosmicSurface.Solar)
                    return (b.Center, b.Size.X, true);
        return (Vec3.Zero, 0, false);
    }

    /// <summary>Whether body <paramref name="index"/> is lit by the scene's
    /// point light: it must exist, the body must not be self-luminous, and the
    /// body's own Sun direction must point at it (within about 30°), so that
    /// an inset lit from the side keeps its directional light.</summary>
    private static bool PointLit(CosmosScene scene, int index, (Vec3 Pos, float Radius, bool Any) light)
    {
        var body = scene.Bodies[index];
        if (!light.Any || body.Emissive) return false;
        var toLight = light.Pos - body.Center;
        float d = toLight.Length();
        if (d <= 1e-6f) return false;
        return Vec3.Dot(toLight / d, body.Sun.Normalized()) > .85f;
    }

    private const int MaxOccluders = 8;
    private readonly List<CosmicOccluder> _occluders = new();

    // Per-body light and shadow uniforms. Shadows come from the explicit
    // occluders and, under the point light, from the other non-emissive bodies
    // of the scene; the eight nearest are uploaded. The GLSL `lit` mirrors
    // MakarovPhysics.Numerics.Shadowing.Lit, pinned in ShadowingTests.
    private void SetLighting(Shader shader, CosmosScene scene, int index, (Vec3 Pos, float Radius, bool Any) light)
    {
        shader.Use(); // defensive: the body or atmosphere shader is already bound by the caller
        var body = scene.Bodies[index];
        bool point = PointLit(scene, index, light);
        shader.SetFloat("uPointLight", point ? 1 : 0);
        shader.SetVec3("uLightPos", point ? light.Pos : body.Center + body.Sun.Normalized() * FarLight);
        shader.SetFloat("uLightRadius", point ? light.Radius : FarLight * (float)Shadowing.SolarAngularRadius);
        shader.SetFloat("uLightAngular", point ? scene.LightAngularRadius : 0);
        _occluders.Clear();
        if (!body.Emissive)
        {
            _occluders.AddRange(scene.Occluders);
            if (point && scene.AutoOccluders)
                for (int j = 0; j < scene.Bodies.Count; j++)
                {
                    if (j == index) continue;
                    var other = scene.Bodies[j];
                    if (other.Emissive) continue;
                    _occluders.Add(new(other.Center, other.Size.X, other.Surface == CosmicSurface.Ocean ? 1 : 0));
                }
            if (_occluders.Count > MaxOccluders)
                _occluders.Sort((a, b) => (a.Center - body.Center).Length().CompareTo((b.Center - body.Center).Length()));
        }
        int count = Math.Min(MaxOccluders, _occluders.Count);
        shader.SetInt("uOccluderCount", count);
        for (int k = 0; k < count; k++)
        {
            var o = _occluders[k];
            shader.SetVec4($"uOccluder[{k}]", o.Center.X, o.Center.Y, o.Center.Z, o.Radius);
            shader.SetFloat($"uOccluderAtmo[{k}]", o.Atmosphere);
        }
    }

    /// <summary>Where a directional sun is placed for the shadow test: far
    /// enough that its angular radius is the real one and the rays parallel.</summary>
    private const float FarLight = 1e4f;

    // The same spheres (same vertex displacement, same cutaway) written to the
    // depth buffer only, so content drawn after the HDR composite is hidden by
    // the bodies exactly as it was when they were drawn in place.
    private void DrawBodiesDepthOnly(CosmosScene scene, Mat4 view, Mat4 projection)
    {
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
        GL.Disable(GL.CULL_FACE);
        GL.ColorMask(false, false, false, false);
        _depthOnly.Use();
        _depthOnly.SetMatrix4("uView", view);
        _depthOnly.SetMatrix4("uProj", projection);
        foreach (var body in scene.Bodies)
        {
            _depthOnly.SetVec3("uCenter", body.Center);
            _depthOnly.SetVec3("uSize", body.Size);
            _depthOnly.SetVec3("uSun", body.Sun);
            _depthOnly.SetFloat("uRotation", body.Rotation);
            _depthOnly.SetFloat("uKind", (int)body.Surface);
            _depthOnly.SetFloat("uDetail", body.Detail);
            _depthOnly.SetFloat("uCut", body.Cut);
            _sphere.Draw();
        }
        if (scene.GranulationScale > 0)
            _photosphere?.DrawDepth(view, projection, scene.GranulationCenter, scene.GranulationExtent);
        GL.ColorMask(true, true, true, true);
    }

    private void DrawCore(CosmosScene scene, Mat4 view, Mat4 projection, Vec3 eye, int height, float exposure, float glow, bool bodiesDrawn = false)
    {
        if (bodiesDrawn) DrawBodiesDepthOnly(scene, view, projection);
        else DrawBodies(scene, view, projection, eye, exposure, hdr: false);

        if (scene.Geometry.SurfacePoints.Count > 0 || scene.Landscape != null)
        {
            _hardware.DetailedFinish=scene.Geometry.BevelBoxes;
            _hardware.WorkshopFinish=scene.HardwareWorkshop;
            _hardware.Update(scene.Geometry.SurfacePoints);
            if (scene.Landscape != null && !ReferenceEquals(_landscapeSource,scene.Landscape))
            {
                _landscape ??= new StudioSurface(800000);
                _landscape.Update(scene.Landscape); _landscapeSource=scene.Landscape;
            }
            if(_landscape!=null) _landscape.WorkshopFinish=false;
            var light=scene.HardwareSun;
            var lightSpace=Mat4.Identity;uint shadow=0;
            if(scene.HardwareShadows)
            {
                var viewport=GL.PhysicalViewport();uint draw=(uint)GL.GetInteger(0x8CA6),read=(uint)GL.GetInteger(0x8CAA);
                bool scissor=GL.IsEnabled(GL.SCISSOR_TEST);
                try
                {
                    _hardwareShadow??=new ShadowMap(2048);GL.Disable(GL.SCISSOR_TEST);
                    lightSpace=ShadowMap.LightSpace(Vec3.Zero,light,scene.ShadowRadius);
                    _hardwareShadow.BeginDepthPass();_hardware.DrawDepth(lightSpace);
                    if(scene.Landscape != null) _landscape?.DrawDepthTranslated(lightSpace,scene.LandscapeOffset);
                    shadow=_hardwareShadow.DepthTexture;
                }
                finally
                {
                    GL.BindFramebuffer(0x8CA9,draw);GL.BindFramebuffer(0x8CA8,read);
                    GL.RestorePhysicalViewport(viewport.X,viewport.Y,viewport.Width,viewport.Height);
                    if(scissor)GL.Enable(GL.SCISSOR_TEST);
                }
            }
            _hardware.Draw(view, projection, eye, light, new(2, 1.85f, 1.65f), lightSpace, shadow, scene.SurfacePhase, ambient:scene.HardwareShadows?scene.HardwareAmbient:.52f, environmentFill:scene.HardwareFill);
        }

        if(scene.Landscape != null)
            _landscape?.DrawTranslated(view,projection,eye,scene.HardwareSun,new(2,1.85f,1.65f),
                ShadowMap.LightSpace(Vec3.Zero,scene.HardwareSun,scene.ShadowRadius),
                scene.HardwareShadows?(_hardwareShadow?.DepthTexture??0):0,scene.SurfacePhase,
                scene.LandscapeOffset,ambient:scene.HardwareAmbient,environmentFill:0);

        if (scene.Rocks.Count > 0)
        {
            var light = SceneLight(scene);
            _rocks ??= new InstancedRocks();
            _rocks.Update(scene.Rocks);
            _rocks.Draw(view, projection, eye, scene.HardwareSun, light.Any ? light.Pos : null);
        }
        if (scene.Rings.Count > 0)
            (_rings ??= new CosmicRings()).Draw(scene, view, projection, eye, exposure, SceneLight(scene));
        if (scene.GranulationScale > 0 && !bodiesDrawn)
            (_photosphere ??= new CosmicPhotosphere()).Draw(view, projection, eye, scene.GranulationScale, scene.GranulationPhase, exposure, center: scene.GranulationCenter, extent: scene.GranulationExtent, contrast: scene.GranulationContrast);
        if (scene.DustTorusDepth > 0)
            (_dustVolume ??= new CosmicDustVolume()).Draw(view, projection, eye, scene.DustTorusDepth, exposure, scene.DustTorusCenter, scene.DustTorusScale);
        foreach (var volume in scene.Volumes)
            (_nebula ??= new CosmicNebula()).Draw(volume, scene.Bodies, view, projection, eye, exposure * glow);
        DrawAtmospheres(scene, view, projection, eye, exposure);
        _halo.Glow = .7f * glow;
        var physical = GL.PhysicalViewport();
        float aspect = physical.Height > 0 ? (float)physical.Width / physical.Height : 1;
        foreach (var body in scene.Bodies)
            if (scene.StellarHalos && body.Surface is (CosmicSurface.Star or CosmicSurface.Solar))
            {
                _halo.DrawCorona(view, projection, body.Center, body.Size.X, body.Color * exposure, scene.SurfacePhase * 4);
                if (scene.SunGlare > 0)
                    _halo.DrawGlare(view, projection, body.Center, body.Color * exposure, aspect, scene.SunGlare * glow);
            }
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(false);
        if(scene.GuideWidth>0)
            (_guidePlot??=new ScientificPlot()).Lines(scene.Lines,view,projection,eye,height,scene.GuideWidth);
        else { _lines.Update(scene.Lines);_lines.Draw(view, projection); }
        GL.DepthMask(false);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE);
        for (int i = 0; i < scene.Gas.Count; i++)
        {
            var p = scene.Gas[i];
            scene.Gas[i] = new(p.Pos, p.Color * (exposure * glow));
        }

        for (int i = 0; i < scene.Stars.Count; i++)
        {
            var p = scene.Stars[i];
            scene.Stars[i] = new(p.Pos, p.Color * (exposure * glow));
        }

        Grow(ref _gas,scene.Gas.Count);Grow(ref _stars,scene.Stars.Count);
        _gas.Update(scene.Gas);_stars.Update(scene.Stars);
        void Emission(){GL.Enable(GL.BLEND);GL.BlendFunc(GL.ONE,GL.ONE);GL.DepthMask(false);_gas.Draw(view,projection,scene.GasRadius,height);_stars.Draw(view,projection,scene.StarRadius,height);}
        if(scene.DiffuseGalaxy && !scene.FloatingEmission && scene.Bodies.Count==0 && scene.Geometry.SurfacePoints.Count==0 && scene.Landscape==null)
        {
            // Small contributions must accumulate in floating point: an 8-bit
            // drawable rounds each faint particle to black before they overlap.
            uint destination=(uint)GL.GetInteger(0x8CA6),read=(uint)GL.GetInteger(0x8CAA);
            var viewport=GL.PhysicalViewport();int scale=GL.RenderScale;
            try
            {
                GL.RenderScale=1;
                _galaxyLight??=new PostFxStack{Exposure=.9f,BloomStrength=.12f,BloomThreshold=1.1f,Vignette=0};
                _galaxyLight.BeginScene(viewport.Width,viewport.Height,0);GL.Disable(GL.DEPTH_TEST);Emission();
                GL.Disable(GL.BLEND);_galaxyLight.EndScene();_galaxyLight.CompositeTo(destination);
            }
            finally {GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER,destination);GL.BindFramebuffer(GL.READ_FRAMEBUFFER,read);GL.RenderScale=scale;GL.RestorePhysicalViewport(viewport.X,viewport.Y,viewport.Width,viewport.Height);GL.Enable(GL.DEPTH_TEST);}
        }
        else Emission();
        if (_gas.Dropped != 0 || _stars.Dropped != 0)
            throw new InvalidOperationException("Cosmic scene exceeded its particle budget");
        GL.Disable(GL.BLEND);
        GL.DepthMask(true);
        GL.Enable(GL.CULL_FACE);
    }

    private static void Grow(ref GlowPoints points,int count)
    {
        if(count<=points.Capacity)return;
        if(count>2100000)throw new InvalidOperationException("Cosmic particle budget exceeded");
        var next=new GlowPoints(Math.Max(count,Math.Min(2100000,points.Capacity*2)));
        points.Dispose();points=next;
    }

    public void Dispose()
    {
        _galaxyLight?.Dispose();_particleLight?.Dispose();_bodyLight?.Dispose();
        _guidePlot?.Dispose();
        _sphere.Dispose();
        _surface.Dispose();
        _depthOnly.Dispose();
        _atmosphere.Dispose();
        _nebula?.Dispose();
        _photosphere?.Dispose();
        _stars.Dispose();
        _gas.Dispose();
        _lines.Dispose();
        _hardware.Dispose();
        _landscape?.Dispose();
        _hardwareShadow?.Dispose();
        _halo.Dispose();
        foreach (var map in _maps) map?.Dispose();
        _dustVolume?.Dispose();
        _rings?.Dispose();
        _rocks?.Dispose();
    }

    /// <summary>Outer radius of the scattering shell over the body's radius:
    /// six scale heights of the (exaggerated) atmosphere, where the density
    /// has fallen to exp(-6).</summary>
    public const float AtmosphereShell = 1.09f;

    // A separate, depth-tested atmosphere gives a real silhouette thickness:
    // single scattering marched through a thin shell (wave W3). An
    // illustration of the morphology, not a radiative-transfer calculation.
    private void DrawAtmospheres(CosmosScene scene, Mat4 view, Mat4 projection, Vec3 eye, float exposure)
    {
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(false);
        GL.Enable(GL.CULL_FACE);
        GL.Enable(GL.BLEND);
        GL.BlendFunc(GL.ONE, GL.ONE);
        _atmosphere.Use();
        _atmosphere.SetMatrix4("uView", view);
        _atmosphere.SetMatrix4("uProj", projection);
        _atmosphere.SetVec3("uEye", eye);
        _atmosphere.SetFloat("uExposure", exposure);
        var light = SceneLight(scene);
        for (int i = 0; i < scene.Bodies.Count; i++)
        {
            var b = scene.Bodies[i];
            if (!b.HasAtmosphere)
                continue;
            _atmosphere.SetVec3("uCenter", b.Center);
            _atmosphere.SetVec3("uBody", b.Size);
            _atmosphere.SetVec3("uSize", b.Size * AtmosphereShell);
            _atmosphere.SetVec3("uSun", b.Sun);
            bool point = PointLit(scene, i, light);
            _atmosphere.SetFloat("uPointLight", point ? 1 : 0);
            _atmosphere.SetVec3("uLightPos", point ? light.Pos : b.Center + b.Sun.Normalized() * FarLight);
            _atmosphere.SetFloat("uCut", b.Cut);
            _atmosphere.SetFloat("uKind", (int)b.Surface);
            _atmosphere.SetVec3("uTint", b.Color);
            _sphere.Draw();
        }

        GL.Disable(GL.BLEND);
        GL.DepthMask(true);
    }

    // Single scattering, Nishita-style, in the body's own normalised space,
    // where the ellipsoid is a unit sphere: N view samples, M sun samples each,
    // Rayleigh (1/lambda^4 colour) plus a Mie haze (forward lobe g = 0.76).
    // The scale height is exaggerated so the shell reads at display size -
    // the real Earth's 8 km would be 0.13 % of the radius - with the zenith
    // optical depths of the real atmosphere scaled by 0.4 so the disc keeps
    // its surface; gain and depths tuned on a numpy mirror (20.10). The sun
    // march stops at the ground, so the night side and the twilight band come
    // out of the geometry, the limb reddens where the sun path is long.
    private const string AtmosphereFragment = """
        #version 330 core
        in vec3 world, normal, local;
        uniform vec3 uEye,uSun,uTint,uLightPos,uCenter,uBody;
        uniform float uExposure, uCut, uPointLight, uKind;
        out vec4 color;
        vec2 hit(vec3 o,vec3 d,float r){float b=dot(o,d),c=dot(o,o)-r*r,h=b*b-c;if(h<0.)return vec2(-1.);h=sqrt(h);return vec2(-b-h,-b+h);}
        void main(){
            if(uCut>0. && local.z>0. && local.x>1.-uCut*2.) discard;
            const float H=0.015, TOP=1.+6.*H; const int N=10, M=4;
            // Per-kind recipe (W4): Earth as measured; giants a tinted haze;
            // Mars thin and dusty; Venus a thick pale deck; ice giants blue.
            bool giant=(uKind>1.5&&uKind<2.5)||(uKind>13.5&&uKind<15.5), ice=uKind>15.5&&uKind<17.5, mars=uKind>10.5&&uKind<11.5, venus=uKind>12.5&&uKind<13.5, titan=uKind>17.5&&uKind<18.5;
            vec3 rayleigh=vec3(.046,.108,.265);
            vec3 betaR=rayleigh*(giant?.25:ice?.6:mars?.12:venus?.3:titan?.12:.4)/H;
            vec3 betaM=(giant?.03*uTint:ice?.01*uTint:mars?.02*vec3(.75,.45,.30):venus?.08*vec3(.95,.85,.60):titan?.13*vec3(.92,.56,.19):vec3(.012))/H;
            vec3 sunW=uPointLight>.5?normalize(uLightPos-world):normalize(uSun);
            vec3 o=(uEye-uCenter)/uBody, dW=normalize(world-uEye), d=normalize(dW/uBody), s=normalize(sunW/uBody);
            vec2 a=hit(o,d,TOP); if(a.y<=0.) discard;
            float t0=max(a.x,0.), t1=a.y;
            vec2 g=hit(o,d,1.); if(g.x>0.) t1=min(t1,g.x);
            if(t1<=t0) discard;
            float ds=(t1-t0)/float(N), odR=0., odM=0.;
            float mu=dot(dW,sunW);
            float phR=3./(16.*3.14159265)*(1.+mu*mu);
            const float gm=.76; float phM=(1.-gm*gm)/(4.*3.14159265*pow(1.+gm*gm-2.*gm*mu,1.5));
            vec3 sum=vec3(0.);
            for(int i=0;i<N;i++){
                vec3 p=o+d*(t0+(float(i)+.5)*ds);
                float h=length(p)-1., dR=exp(-h/H), dM=exp(-h/(H*.5));
                odR+=dR*ds; odM+=dM*ds;
                float dl=hit(p,s,TOP).y/float(M), sR=0., sM=0.; bool night=false;
                for(int j=0;j<M;j++){ vec3 q=p+s*((float(j)+.5)*dl); float hq=length(q)-1.; if(hq<0.){night=true;break;} sR+=exp(-hq/H)*dl; sM+=exp(-hq/(H*.5))*dl; }
                if(night) continue;
                vec3 tr=exp(-(betaR*(odR+sR)+betaM*(odM+sM)));
                sum+=tr*(betaR*dR*phR+betaM*dM*phM)*ds;
            }
            color=vec4(sum*5.*uExposure,1.);
        }
        """;
    // Depth-only twin of Fragment: the cutaway discard is the only thing a
    // depth write needs to know.
    private const string DepthFragment = """
        #version 330 core
        in vec3 world, normal, local;
        uniform float uCut;
        void main(){ if(uCut>0. && local.z>0. && local.x>1.-uCut*2.) discard; }
        """;
    private const string Vertex = """
        #version 330 core
        layout(location=0) in vec3 aPos;
        layout(location=1) in vec3 aNormal;
        uniform mat4 uView, uProj;
        uniform vec3 uCenter, uSize;
        out vec3 world, normal, local;
        uniform vec3 uSun;
        uniform float uKind,uDetail,uRotation;
        float harmonic(vec3 q){
            int l=int(floor(uDetail/10.+.01)),m=int(mod(uDetail,10.)+.01);
            float p=1.,s=sqrt(max(0.,1.-q.y*q.y));
            for(int k=1;k<=8;k++){if(k>m)break;p*=-float(2*k-1)*s;}
            float p1=q.y*float(2*m+1)*p;
            if(l>m){for(int k=2;k<=8;k++){if(k<=m+1)continue;if(k>l)break;float pn=(float(2*k-1)*q.y*p1-float(k+m-1)*p)/float(k-m);p=p1;p1=pn;}p=p1;}
            return p*cos(float(m)*atan(q.z,q.x))/max(1e-12,uSun.x);
        }
        void main() { local=aPos; float radius=1.;if(uKind>8.5 && uKind<9.5)radius+=uSun.y*harmonic(normalize(aPos))*cos(uRotation);world=uCenter+aPos*uSize*radius;normal=normalize(aNormal/uSize);gl_Position=uProj*uView*vec4(world,1.); }
        """;
    private const string Fragment = """
        #version 330 core
        in vec3 world, normal, local;
        uniform vec3 uCenter, uSize, uTint, uSun, uEye;
        uniform float uRotation, uKind, uDetail, uCut, uExposure, uPhase, uTilt, uIrradiance, uAzimuth, uHdr;
        uniform sampler2D uEarth, uMoon, uHeight, uNight, uClouds, uEarthHeight, uMars, uMarsHeight, uMercury, uVenus, uJupiter, uSaturn, uUranus, uNeptune;
        uniform float uHas[14];  // which maps were found on disk, by sampler slot
        uniform float uHasRing, uRingInner, uRingOuter;
        uniform vec3 uRingAxis;
        uniform int uRingProfile;
        out vec4 color;
        """ + "\n" + CosmicRings.ProfileGlsl + "\n" + """
        // The ring sheet's shadow on its planet: the sun ray from the point
        // against the ring plane through the centre (W5).
        float ringShade(vec3 p,vec3 sunDir){
            if(uHasRing<.5) return 0.;
            float denom=dot(sunDir,uRingAxis); if(abs(denom)<1e-5) return 0.;
            float t=-dot(p-uCenter,uRingAxis)/denom; if(t<=0.) return 0.;
            vec3 hit=p+sunDir*t-uCenter; float r=length(hit-uRingAxis*dot(hit,uRingAxis));
            return ringDensity((r-uRingInner)/(uRingOuter-uRingInner),uRingProfile)*.85;
        }
        """ + "\n" + DefaultShaders.EclipseShadow + "\n" + """
        float hash(vec3 p) { return fract(sin(dot(p,vec3(127.1,311.7,74.7)))*43758.5453); }
        float noise(vec3 p) {
            vec3 i=floor(p),f=fract(p); f=f*f*(3.-2.*f);
            return mix(mix(mix(hash(i),hash(i+vec3(1,0,0)),f.x),mix(hash(i+vec3(0,1,0)),hash(i+vec3(1,1,0)),f.x),f.y),
                       mix(mix(hash(i+vec3(0,0,1)),hash(i+vec3(1,0,1)),f.x),mix(hash(i+vec3(0,1,1)),hash(i+vec3(1,1,1)),f.x),f.y),f.z);
        }
        float fbm(vec3 p) { return .53*noise(p)+.27*noise(p*2.03+2.)+.13*noise(p*4.11)+.07*noise(p*8.23); }
        // Longitude/latitude of a point of the rotated unit sphere.
        vec2 lonlat(vec3 q){ return vec2(atan(q.z,q.x)/6.2831853+.5,asin(clamp(q.y,-1.,1.))/3.14159265+.5); }
        // A normal perturbation from a height map, in the sphere's own frame
        // (east, north); the caller rotates it back into the world (W4 - the
        // Moon's LOLA path of 07.10, made general).
        vec3 heightBump(sampler2D h, vec2 uv, vec3 q, vec2 texel, float strength){
            float h0=texture(h,uv).r;
            vec2 slope=vec2(texture(h,uv+vec2(texel.x,0)).r-h0,texture(h,uv+vec2(0,texel.y)).r-h0);
            vec3 east=normalize(vec3(-q.z,0.,q.x)),north=normalize(cross(q,east));
            return (east*slope.x+north*slope.y)*strength;
        }
        // The banded weather of a giant: zonal bands warped by a slow flow.
        float gasBands(vec3 q,float scale,vec3 amp){
            float lat=asin(clamp(q.y,-1.,1.));
            vec3 flow=q*13.+vec3(2.8*sin(lat*17.),0.,2.1*cos(lat*19.));
            float warp=.075*(fbm(flow)-.5)+.008*sin(atan(q.z,q.x)*36.+lat*15.);
            return .5+amp.x*sin((lat+warp)*scale)+amp.y*sin((lat+warp)*63.)+amp.z*sin(lat*133.);
        }
        float harmonic(vec3 q){
            int l=int(floor(uDetail/10.+.01)),m=int(mod(uDetail,10.)+.01);
            float p=1.,s=sqrt(max(0.,1.-q.y*q.y));
            for(int k=1;k<=8;k++){if(k>m)break;p*=-float(2*k-1)*s;}
            float p1=q.y*float(2*m+1)*p;
            if(l>m){for(int k=2;k<=8;k++){if(k<=m+1)continue;if(k>l)break;float pn=(float(2*k-1)*q.y*p1-float(k+m-1)*p)/float(k-m);p=p1;p1=pn;}p=p1;}
            return p*cos(float(m)*atan(q.z,q.x))/max(1e-12,uSun.x);
        }
        void main() {
            if(uCut>0. && local.z>0. && local.x>1.-uCut*2.) discard;
            vec3 q=normalize(local);float ca=cos(uAzimuth),sa=sin(uAzimuth);q.xz=mat2(ca,-sa,sa,ca)*q.xz;
            float ct=cos(uTilt),st=sin(uTilt);q.yz=mat2(ct,-st,st,ct)*q.yz; float c=cos(uRotation),s=sin(uRotation); q.xz=mat2(c,-s,s,c)*q.xz;
            vec3 n=normalize(normal),v=normalize(uEye-world);
            vec3 sunDir=uPointLight>.5?normalize(uLightPos-world):normalize(uSun);
            // the sun in the map's frame, for cloud shadows
            vec3 sq=normalize(sunDir/uSize); sq.xz=mat2(ca,-sa,sa,ca)*sq.xz; sq.yz=mat2(ct,-st,st,ct)*sq.yz; sq.xz=mat2(c,-s,s,c)*sq.xz;
            vec2 uv=lonlat(q);
            float f=fbm(q*5.),small=noise(q*80.);
            vec3 albedo=uTint; float water=0.,emission=0.,clouds=0.;
            vec3 bump=vec3(0.), night=vec3(0.);  // bump in the map's frame, night lights
            float day=smoothstep(-.06,.14,dot(n,sunDir));
            if(uKind>7.5 && uKind<8.5 && uHas[1]>.5 && uHas[2]>.5) {
                albedo=texture(uMoon,uv).rgb;
                bump=heightBump(uHeight,uv,q,vec2(.001,.002),.8);
            } else if(uKind<.5 || (uKind>7.5 && uKind<8.5) || (uKind>11.5 && uKind<12.5)) {
                // bare rock: Rock, the Moon without maps, Mercury (its map when present)
                float craters=smoothstep(.59,.68,noise(q*38.))*smoothstep(.38,.55,noise(q*13.));
                if(uKind>11.5 && uHas[8]>.5) albedo=texture(uMercury,uv).rgb*(1.-.12*craters);
                else albedo*=.42+1.03*f-.24*craters+.08*small;
                vec3 grain=vec3(noise(q*65.+.03),noise(q*65.+vec3(0,.03,0)),noise(q*65.+vec3(0,0,.03)))-noise(q*65.);
                n=normalize(n+grain*.7);
            } else if(uKind<1.5) {
                float continent=fbm(q*3.2+vec3(.5,1.1,0));
                float land=smoothstep(.49,.52,continent); water=1.-land;
                vec3 terrain=mix(vec3(.055,.19,.09),vec3(.34,.29,.15),smoothstep(.5,.68,continent));
                float polar=smoothstep(.77,.94,abs(q.y)+.06*f);
                albedo=mix(vec3(.012,.095,.22),terrain,land); albedo=mix(albedo,vec3(.82,.89,.93),polar);
                if(uHas[0]>.5) { albedo=texture(uEarth,uv).rgb; water=step(albedo.r*1.45,albedo.b)*step(albedo.g,albedo.b); }
                // relief: the height map when present (weak - the Earth is smooth at this scale), else fine noise
                if(uHas[5]>.5) bump=heightBump(uEarthHeight,uv,q,vec2(.0005,.001),.35)*(1.-water);
                else {
                    float mountains=fbm(q*130.)*(1.-water);
                    vec3 surfGrad=cross(dFdy(world),n)*dFdx(mountains)+cross(n,dFdx(world))*dFdy(mountains);
                    n=normalize(n-.00045*surfGrad/max(.00001,abs(dot(dFdx(world),cross(dFdy(world),n)))));
                }
                // a drifting cloud deck (its own map or fbm) that shadows the ground
                // towards the sun: the cloud over the point q + sq*h is the one in the way
                vec3 cloudq=q; cloudq.x+=uRotation*.025;
                vec2 cuv=uv+vec2(uRotation*.004,0.);
                vec3 qs=normalize(q+sq*.03); vec2 suv=lonlat(qs)+vec2(uRotation*.004,0.);
                float shade;
                if(uHas[4]>.5){ clouds=texture(uClouds,cuv).r; shade=texture(uClouds,suv).r; }
                else {
                    clouds=smoothstep(.57,.74,fbm(cloudq*9.+vec3(0,noise(q*17.)*.5,0)))*(.65+.35*fbm(cloudq*55.));
                    vec3 shq=qs; shq.x+=uRotation*.025;
                    shade=smoothstep(.57,.74,fbm(shq*9.+vec3(0,noise(qs*17.)*.5,0)))*(.65+.35*fbm(shq*55.));
                }
                albedo*=1.-.55*shade*day;
                albedo=mix(albedo,vec3(.80,.86,.92),clouds*.75); water*=1.-clouds;
                if(uHas[3]>.5) night=texture(uNight,uv).rgb*(1.-clouds*.8);
            } else if(uKind<2.5) {
                // a generic giant: the module's tint, no Great Red Spot (that is Jupiter's, kind 14)
                albedo=mix(uTint*.42,uTint*1.18,clamp(gasBands(q,max(8.,uDetail*26.),vec3(.28,.14,.08)),0.,1.));
            } else if(uKind<3.5 || (uKind>5.5 && uKind<6.5)) {
                // Advected granulation: cells evolve as well as rotate. The
                // phase follows the experiment timeline and freezes on pause.
                vec3 flow=vec3(sin(q.y*13.+uPhase*.7),cos(q.z*11.-uPhase*.55),sin(q.x*9.+uPhase*.8));
                vec3 cellp=q*92.+flow*.9;
                float cells=noise(cellp)*.6+noise(cellp*2.1+uPhase*.25)*.3+.10*noise(cellp*4.3);
                cells=smoothstep(.22,.75,cells);
                float convection=fbm(q*21.+flow*.24);
                cells*=.72+.45*convection;
                float limb=.28+.72*pow(max(dot(n,v),0.),.45);
                float spotField=fbm(q*9.+vec3(uRotation*.1,0,0));
                float belt=exp(-pow((abs(q.y)-.25)/.23,2.));
                float penumbra=smoothstep(.68,.74,spotField),umbra=smoothstep(.74,.79,spotField);
                float spots=1.-clamp(uDetail,0.,1.5)*belt*(.38*penumbra+.35*umbra);
                albedo=uTint*(.95+1.05*cells)*limb*max(.12,spots); emission=1.;
            } else if(uKind<4.5) {
                float cracks=pow(1.-abs(sin(q.x*29.+fbm(q*8.)*7.)*sin(q.y*33.+q.z*8.)),14.);
                albedo=mix(uTint,vec3(.10,.28,.36),cracks*.65)*(.68+.54*f);
            } else if(uKind<5.5) {
                float f1=fbm(q*max(2.,uDetail)); float f2=noise(q*max(8.,uDetail*3.));
                float signal=clamp(.5+(f1-.5)*2.6+(f2-.5)*.3,0.,1.);
                albedo=mix(vec3(.04,.22,.55),vec3(.85,.14,.05),signal); albedo=mix(albedo,vec3(.98,.88,.55),pow(1.-abs(signal-.5)*2.,3.)); emission=1.;
            } else if(uKind>10.5 && uKind<11.5) {
                // Mars: rust plains, dark basins, polar caps; MOLA relief when present
                if(uHas[6]>.5) albedo=texture(uMars,uv).rgb;
                else {
                    albedo=mix(vec3(.50,.26,.14),vec3(.80,.52,.33),fbm(q*4.+1.7));
                    float basin=smoothstep(.55,.72,fbm(q*2.3+vec3(7.,0,0)));
                    albedo=mix(albedo,vec3(.36,.20,.12),basin*.55);
                    float cap=smoothstep(.84,.94,abs(q.y)+.03*f);
                    albedo=mix(albedo,vec3(.93,.92,.90),cap);
                }
                if(uHas[7]>.5) bump=heightBump(uMarsHeight,uv,q,vec2(.0005,.001),.9);
                else { vec3 grain=vec3(noise(q*55.+.03),noise(q*55.+vec3(0,.03,0)),noise(q*55.+vec3(0,0,.03)))-noise(q*55.); n=normalize(n+grain*.45); }
            } else if(uKind>12.5 && uKind<13.5) {
                // Venus: an unbroken pale deck with a slow chevron swirl
                if(uHas[9]>.5) albedo=texture(uVenus,uv).rgb;
                else {
                    float lat=asin(clamp(q.y,-1.,1.));
                    float swirl=fbm(q*3.+vec3(0,uRotation*.3,0));
                    float chevron=.5+.5*sin(lat*6.+atan(q.z,q.x)*1.5+swirl*4.);
                    albedo=vec3(.93,.85,.62)*(.80+.22*fbm(q*7.+swirl*2.))*(1.-.10*chevron);
                }
            } else if(uKind>13.5 && uKind<14.5) {
                // Jupiter: the bands with the Great Red Spot
                if(uHas[10]>.5) albedo=texture(uJupiter,uv).rgb;
                else {
                    vec3 tint=vec3(.91,.72,.47);
                    albedo=mix(tint*.42,tint*1.18,clamp(gasBands(q,max(8.,uDetail*26.),vec3(.28,.14,.08)),0.,1.));
                    float storm=exp(-90.*pow(q.y+.24,2.)-18.*pow(q.x-.46,2.)-12.*pow(q.z-.81,2.));
                    albedo=mix(albedo,vec3(.62,.20,.10),storm*.65);
                }
            } else if(uKind>14.5 && uKind<15.5) {
                // Saturn: softer, paler bands
                if(uHas[11]>.5) albedo=texture(uSaturn,uv).rgb;
                else { vec3 tint=vec3(.93,.85,.65); albedo=mix(tint*.70,tint*1.08,clamp(gasBands(q,22.,vec3(.10,.05,.02)),0.,1.)); }
            } else if(uKind>15.5 && uKind<16.5) {
                // Uranus: a nearly featureless cyan
                if(uHas[12]>.5) albedo=texture(uUranus,uv).rgb;
                else { vec3 tint=vec3(.62,.84,.88); albedo=tint*(.94+.06*gasBands(q,20.,vec3(.5,.1,0.))); }
            } else if(uKind>16.5 && uKind<17.5) {
                // Neptune: deep blue, a dark spot, bright methane streaks
                if(uHas[13]>.5) albedo=texture(uNeptune,uv).rgb;
                else {
                    vec3 tint=vec3(.22,.40,.86);
                    albedo=mix(tint*.75,tint*1.15,clamp(gasBands(q,18.,vec3(.18,.06,.02)),0.,1.));
                    float spot=exp(-70.*pow(q.y+.30,2.)-16.*pow(q.x-.52,2.)-12.*pow(q.z-.76,2.));
                    albedo=mix(albedo,tint*.45,spot*.8);
                    float streak=pow(noise(q*vec3(9.,40.,9.)+vec3(0,0,uRotation*.2)),9.);
                    albedo=mix(albedo,vec3(.92,.95,1.),streak*.6);
                }
            } else if(uKind>17.5 && uKind<18.5) {
                // Titan's visible disc is obscured by photochemical haze.
                // Low-contrast polar shading is illustrative, not a measured map.
                float haze=.94+.06*fbm(q*4.+vec3(0,uRotation*.08,0));
                float polar=smoothstep(.60,.96,q.y);
                albedo=mix(vec3(.67,.36,.105),vec3(.39,.29,.14),polar*.28)*haze;
            } else if(uKind>9.5 && uKind<10.5) {
                // Beloborodov relation: cos(alpha)=u+(1-u)cos(psi).
                // The image remains a proper-radius size comparison; only the
                // surface map uses this illustrative light-bending mapping.
                vec3 sight=normalize((uEye-uCenter)/uSize),radial=normalize(local);
                float mu=clamp(dot(radial,sight),0.,1.),compactness=clamp(uDetail,0.,.65);
                float cosPsi=clamp((mu-compactness)/(1.-compactness),-1.,1.);
                vec3 tangent=radial-sight*dot(radial,sight);
                tangent=length(tangent)>.00001?normalize(tangent):vec3(0,1,0);
                vec3 map=sight*cosPsi+tangent*sqrt(max(0.,1.-cosPsi*cosPsi));
                map.xz=mat2(c,-s,s,c)*map.xz;
                float hot=pow(max(0.,dot(map,normalize(vec3(.58,.57,.58)))),40.);
                hot+=.7*pow(max(0.,dot(map,-normalize(vec3(.58,.57,.58)))),40.);
                float grain=.82+.24*noise(map*120.+uPhase*.3);
                albedo=mix(vec3(.12,.30,.61),vec3(1.8,.92,.35),clamp(hot,0.,1.))*grain*(.55+.45*mu);
                emission=1.;
            } else if(uKind>8.5 && uKind<9.5) {
                float mode=harmonic(normalize(local)), motion=mode*cos(uRotation);
                float grain=.68+.45*noise(q*98.+vec3(uPhase*.4,0,0));
                vec3 blue=vec3(.045,.36,1.25),red=vec3(1.65,.20,.065),neutral=vec3(.79,.75,.60);
                albedo=mix(neutral,motion>0.?red:blue,pow(abs(motion),.6));
                albedo*=grain*(.35+.65*pow(max(dot(n,v),0.),.4));
                if(uSun.z>.5) albedo=mix(vec3(1.25,.57,.12)*grain,albedo,.65);
                float node=1.-smoothstep(.003,.010,abs(mode));
                albedo=mix(albedo,vec3(.83,.94,1.1),node*.7); emission=1.;
            } else { albedo*=.80+.2*sin(length(q.xz)*42.); emission=.32; }
            // the map-frame bump back into the world
            if(dot(bump,bump)>0.){ bump.xz=mat2(c,s,-s,c)*bump.xz; bump.yz=mat2(ct,st,-st,ct)*bump.yz; bump.xz=mat2(ca,sa,-sa,ca)*bump.xz; n=normalize(n-bump); }
            float d=max(dot(n,sunDir),0.); float rim=pow(1.-max(dot(n,v),0.),4.);
            // Eclipses: the direct term through the occluders, the umbra of a
            // body with an atmosphere reddened; emission ignores all of it.
            vec2 sh=emission>.5?vec2(1.,0.):shadow(world,sunDir);
            sh.x*=1.-ringShade(world,sunDir);
            vec3 direct=vec3(sh.x)*mix(vec3(1.),vec3(1.,.30,.10),sh.y*.9);
            // a reddened umbra is faint, not black: the atmosphere bends some light in
            direct+=vec3(.045,.012,.004)*sh.y;
            vec3 lit=albedo*mix(.016+1.24*d*uIrradiance*direct,vec3(1.),emission);
            // a trace of atmospheric perspective inside the limb; the shell outside carries the real scattering (W3)
            if(uKind>.5 && uKind<2.5) lit+=vec3(.06,.22,.45)*rim*smoothstep(-.18,.35,dot(n,sunDir))*.25*sh.x;
            // the ocean: a Fresnel glint (bright and wide at grazing incidence, a
            // small hot spot face-on) and a faint sheen, both through cloud and shadow
            if(water>0.) {
                float ndh=max(dot(n,normalize(sunDir+v)),0.), fres=mix(.08,1.,pow(1.-max(dot(n,v),0.),5.));
                lit+=vec3(1.,.95,.85)*water*sh.x*uIrradiance*(fres*1.8*pow(ndh,160.)+fres*.10*d);
            }
            // city lights where the sun has set (Black Marble when present)
            lit+=night*(1.-day)*1.1;
            // Linear radiance for the HDR path: exposure, bloom and gamma are
            // applied once at the composite (PostFxStack.Opaque).
            if(uHdr>.5){ color=vec4(max(lit,vec3(0)),1.); return; }
            // Smooth exposure without clipping the bright photosphere to white.
            lit=1.-exp(-max(lit,vec3(0))*uExposure);
            color=vec4(pow(lit,vec3(1./2.2)),1.);
        }
        """;
}
