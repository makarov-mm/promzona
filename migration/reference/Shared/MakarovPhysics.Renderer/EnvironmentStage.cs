namespace MakarovPhysics.Renderer;

/// <summary>
/// THE VISUAL ENVIRONMENT, as a concept (24.08).
///
/// The suite grew four kinds of "what the apparatus stands in", each excellent
/// and each adopted by hand:
///
///   * <see cref="Background"/> - the flat PAINT the shell draws behind every
///     module (starfield / laboratory gradient / engineering grid, from
///     <c>IPhysicsModule.Background</c>). Camera-free, always there.
///   * <see cref="LabRoom"/>   - the raycast laboratory, 28 adopters.
///   * <see cref="SkyDome"/>   - the procedural day sky, 4 adopters.
///   * <see cref="LabBench"/>  - the wooden bench with contact shadows;
///     scene FURNITURE rather than a backdrop, and deliberately outside this
///     class - it draws meshes into the depth buffer and belongs to the
///     module's own geometry pass.
///
/// Every adopter wrote the same three lines with a different class name, and
/// two modules even called their bench "_room". This class is the concept
/// those three lines were reaching for: a module owns ONE stage, declares
/// WHICH backdrop it wants, and draws it first - the stage carries the
/// parameters of both backdrops under the very names LabRoom and SkyDome
/// use, so a migration is a rename, not a rewrite.
///
///     _env = new EnvironmentStage
///     {
///         Kind = EnvironmentKind.Laboratory,
///         FloorY = benchTopY - 0.95f,        // the same fields as before
///     };
///     _env.Draw(view, proj, _camera.Position(), (float)Time, _camera.Far);
///     _env.Dispose();
///
/// The flat paint stays with the shell: it costs one triangle, it needs no
/// camera, and a stage that fails to cover a corner falls back to it instead
/// of to garbage. The stage draws OVER the paint and UNDER the apparatus,
/// writes no depth, and restores no state - the same contract its two
/// backdrops always had.
///
/// Parameters are plain mutable fields and they are FORWARDED EVERY DRAW:
/// RollingRace moves the room floor with the track each frame, and a stage
/// that snapshots its spec at construction would quietly freeze that.
/// Construction of the underlying GL resources is lazy (first Draw) and
/// transactional, so the stage itself may be built off the render thread.
/// </summary>
public enum EnvironmentKind
{
    /// <summary>No camera-anchored backdrop: the shell's flat paint only.</summary>
    None,

    /// <summary>The raycast laboratory - see <see cref="LabRoom"/>.</summary>
    Laboratory,

    /// <summary>The procedural day sky - see <see cref="SkyDome"/>.</summary>
    DaySky,

    /// <summary>The star dome - see <see cref="SpaceDome"/>. Stars sit at
    /// infinity: rotation pans them, translation leaves them be - which the
    /// old screen-space star PAINT could not do.</summary>
    Space,

    /// <summary>The photographer's sweep - see <see cref="StudioCyclorama"/>.
    /// For the table modules, which until 18.09 had a choice between a
    /// laboratory they are not in and a starfield they have nothing to do
    /// with (screenshot review §1.2: some thirty non-cosmic modules stood in
    /// space).</summary>
    Studio,
}

public sealed class EnvironmentStage : IDisposable
{
    public EnvironmentKind Kind = EnvironmentKind.None;

    // ---- laboratory fields, names and defaults verbatim from LabRoom ------
    public float FloorY = -1.0f;
    public float RoomHalf = 7.0f;
    public float CeilingHeight = 3.4f;
    public Vec3 LightDir = new(0.38f, 0.80f, 0.48f);
    public Vec3 WallColor = new(0.335f, 0.350f, 0.375f);
    public Vec3 FloorColor = new(0.200f, 0.205f, 0.220f);
    // the ceiling was the brightest thing in seven modules' frames (visual
    // audit, 02.09): the panels at 1.0 outshone the apparatus. Half strength
    // and a darker ceiling for every laboratory at once - one number to
    // revert if the bench reads too dark on screen
    public Vec3 CeilingColor = new(0.360f, 0.370f, 0.385f);
    public float PanelStrength = 0.5f;
    public float Haze = 0.55f;

    // ---- space fields, names and defaults verbatim from SpaceDome ---------
    public float StarDensity = 1f;
    public float NebulaAmount = 1f;
    /// <summary>Where Textures/ lives (the Milky Way map); null keeps the
    /// procedural sky. The catalogue stars need it too.</summary>
    public string? TexturesDirectory;
    public bool CatalogStars = true;
    public bool Constellations;
    public Mat4 SkyRotation = Mat4.Identity;
    /// <summary>Viewport height in pixels, for the star sprites' size; a
    /// module that knows it sets it before Draw.</summary>
    public int ViewportHeight = 1080;

    // ---- day-sky fields, names and defaults verbatim from SkyDome ---------
    public Vec3 SunDir = new(0.35f, 0.55f, 0.30f);
    public Vec3 HorizonColor = new(0.74f, 0.85f, 0.96f);
    public Vec3 ZenithColor = new(0.18f, 0.42f, 0.82f);
    public float CloudAmount = 1f;
    /// <summary>Under the day sky, draw the heightfield meadow (see
    /// <see cref="Meadow"/>) instead of leaving the ground to the module.
    /// Off by default: the benches that stand on their own slab keep it.</summary>
    public bool MeadowGround;
    public bool DetailedWeather;
    public float SkyExposure = 1f;
    public float Daylight = 1f;
    public uint GroundShadowTexture;
    public Mat4 GroundShadowMatrix = Mat4.Identity;
    public Vec3 GrassColor = new(0.30f, 0.44f, 0.22f);
    public float FogDistance = 110f;

    // ---- studio fields, names and defaults verbatim from StudioCyclorama --
    public float StudioFloorY = -0.35f;
    public float StudioRadius = 9.0f;
    public float CoveRadius = 3.2f;
    public Vec3 BackdropColor = new(0.300f, 0.315f, 0.355f);
    public Vec3 StudioFloorColor = new(0.150f, 0.158f, 0.180f);
    public float FloorSheen = 0.55f;
    public float Falloff = 0.65f;

    private LabRoom? _room;
    private SkyDome? _sky;
    private Meadow? _meadow;
    private SpaceDome? _space;
    private StudioCyclorama? _studio;

    /// <summary>Draw the declared backdrop around the camera. Safe to call
    /// with <see cref="EnvironmentKind.None"/> - it does nothing, so a module
    /// can key the kind off a checkbox without branching at the call site.
    /// The <paramref name="time"/> feeds the sky's drifting clouds and is
    /// ignored by the room.</summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 camPos, float time, float farPlane)
    {
        switch (Kind)
        {
            case EnvironmentKind.Laboratory:
                _room ??= new LabRoom();
                _room.FloorY = FloorY;
                _room.RoomHalf = RoomHalf;
                _room.CeilingHeight = CeilingHeight;
                _room.LightDir = LightDir;
                _room.WallColor = WallColor;
                _room.FloorColor = FloorColor;
                _room.CeilingColor = CeilingColor;
                _room.PanelStrength = PanelStrength;
                _room.Haze = Haze;
                _room.Draw(view, proj, camPos, farPlane);
                break;

            case EnvironmentKind.DaySky:
                _sky ??= new SkyDome();
                _sky.SunDir = SunDir;
                _sky.HorizonColor = HorizonColor;
                _sky.ZenithColor = ZenithColor;
                _sky.CloudAmount = CloudAmount;
                _sky.DetailedWeather = DetailedWeather;
                _sky.Exposure = SkyExposure;
                _sky.Daylight = Daylight;
                _sky.Draw(view, proj, camPos, time, farPlane);
                if (MeadowGround)
                {
                    _meadow ??= new Meadow();
                    _meadow.SunDir = SunDir;
                    _meadow.HorizonColor = HorizonColor;
                    _meadow.GrassColor = GrassColor;
                    _meadow.CloudAmount = CloudAmount;
                    _meadow.DetailedWeather = DetailedWeather;
                    _meadow.FogDistance = FogDistance;
                    _meadow.ShadowTexture = GroundShadowTexture;
                    _meadow.ShadowMatrix = GroundShadowMatrix;
                    _meadow.Draw(view, proj, camPos, time);
                }
                break;

            case EnvironmentKind.Space:
                _space ??= new SpaceDome();
                _space.StarDensity = StarDensity;
                _space.NebulaAmount = NebulaAmount;
                _space.TexturesDirectory = TexturesDirectory;
                _space.CatalogStars = CatalogStars;
                _space.Constellations = Constellations;
                _space.SkyRotation = SkyRotation;
                _space.Draw(view, proj, camPos, time, farPlane, ViewportHeight);
                break;

            case EnvironmentKind.Studio:
                _studio ??= new StudioCyclorama();
                _studio.FloorY = StudioFloorY;
                _studio.Radius = StudioRadius;
                _studio.CoveRadius = CoveRadius;
                _studio.BackdropColor = BackdropColor;
                _studio.FloorColor = StudioFloorColor;
                _studio.LightDir = LightDir;
                _studio.FloorSheen = FloorSheen;
                _studio.Falloff = Falloff;
                _studio.Draw(view, proj, camPos, farPlane);
                break;

            case EnvironmentKind.None:
                break;
        }
    }

    public void Dispose()
    {
        _room?.Dispose();
        _room = null;
        _sky?.Dispose();
        _sky = null;
        _meadow?.Dispose();
        _meadow = null;
        _space?.Dispose();
        _space = null;
        _studio?.Dispose();
        _studio = null;
    }
}
