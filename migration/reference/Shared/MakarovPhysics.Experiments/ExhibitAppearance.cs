using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>Scene-specific visual roles. Data colours can still be supplied
/// literally by a module; an image or a spectrum is never recoloured here.</summary>
public sealed record ExhibitAppearance
{
    public Vec3 Primary { get; init; } = new(.16f, .38f, .66f);
    public Vec3 Accent { get; init; } = new(.86f, .55f, .20f);
    public Vec3 Signal { get; init; } = new(.82f, .20f, .20f);
    public Vec3 Neutral { get; init; } = new(.77f, .81f, .84f);
    public Vec3 Dark { get; init; } = new(.055f, .068f, .085f);
    public Vec3 Secondary { get; init; } = new(.49f, .33f, .68f);
    public Vec3 Wood { get; init; } = new(.48f, .27f, .12f);
    public Vec3 Backdrop { get; init; } = new(.19f, .22f, .27f);
    public Vec3 Canvas { get; init; } = new(.022f, .028f, .043f);
    public Vec3 Light { get; init; } = new Vec3(-.48f, .78f, .45f).Normalized();
    public Vec3 LightColor { get; init; } = new(1.9f, 1.8f, 1.65f);
    public SurfaceMaterial Material { get; init; } = SurfaceMaterial.Enamel;
    public SurfaceMaterial Worktop { get; init; } = SurfaceMaterial.Wood;
    public bool OpticalBench { get; init; }
    public bool Cosmic { get; init; }

    public static ExhibitAppearance For(string id, bool diagram)
    {
        var classic = new ExhibitAppearance();
        if (id == "EngFilmProjector") return classic with
        {
            Backdrop = new(.12f,.15f,.19f), Canvas = new(.025f,.032f,.045f),
            Light = new Vec3(-.10f,.32f,.94f).Normalized(), LightColor = new(2.7f,2.55f,2.30f),
            Worktop = SurfaceMaterial.Wood
        };
        if (id is "EngWaterClock" or "EngTidePredictor") return classic with
        {
            Backdrop = new(.12f,.15f,.19f), Canvas = new(.025f,.032f,.045f),
            Light = new Vec3(-.32f,.66f,.72f).Normalized(), LightColor = new(2.4f,2.25f,2.05f),
            Worktop = SurfaceMaterial.Wood
        };
        if (id is "EngFlywheelStorage" or "EngBaseIsolation" or "EngEscalator" or "EngPneumaticPost") return classic with
        {
            Backdrop = new(.12f,.17f,.21f), Canvas = new(.025f,.038f,.05f),
            Light = new Vec3(-.3f,.82f,.52f).Normalized(), LightColor = new(2.32f,2.3f,2.22f),
            Worktop = SurfaceMaterial.Concrete
        };
        if (id.StartsWith("Eng", StringComparison.Ordinal)) return classic with
        {
            Primary = new(.12f,.38f,.37f), Accent = new(.80f,.57f,.24f),
            Backdrop = new(.14f,.18f,.23f), Canvas = new(.035f,.045f,.063f),
            Light = new Vec3(-.45f,.72f,.62f).Normalized(), LightColor = new(2.15f,2.08f,1.95f),
            Worktop = SurfaceMaterial.Stone
        };
        var mechanism = classic with
        {
            Primary = new(.22f, .39f, .29f),
            Accent = new(.80f, .57f, .22f),
            Neutral = new(.69f, .74f, .79f),
            Backdrop = new(.24f, .22f, .20f)
        };
        var electric = classic with
        {
            Primary = new(.64f, .27f, .12f),
            Accent = new(.30f, .68f, .95f),
            Signal = new(.89f, .20f, .32f),
            Secondary = new(.68f, .37f, .96f),
            Worktop = SurfaceMaterial.Rubber,
            Backdrop = new(.13f, .17f, .23f)
        };
        var fluid = classic with
        {
            Primary = new(.035f, .34f, .64f),
            Accent = new(.74f, .53f, .23f),
            Signal = new(.90f, .36f, .16f),
            Secondary = new(.20f, .64f, .60f),
            Neutral = new(.83f, .92f, .96f),
            Backdrop = new(.18f, .28f, .34f),
            Worktop = SurfaceMaterial.Stone
        };
        var optic = classic with
        {
            Primary = new(.31f, .56f, .95f),
            Accent = new(.94f, .74f, .25f),
            Signal = new(.98f, .19f, .33f),
            Secondary = new(.64f, .33f, .98f),
            Backdrop = new(.11f, .12f, .18f),
            Worktop = SurfaceMaterial.Rubber,
            OpticalBench = true
        };
        var heat = mechanism with
        {
            Primary = new(.14f, .35f, .56f),
            Accent = new(.74f, .37f, .14f),
            Signal = new(.88f, .16f, .08f),
            Secondary = new(.61f, .18f, .24f)
        };
        var geometry = classic with
        {
            Primary = new(.24f, .56f, .93f),
            Accent = new(.98f, .67f, .26f),
            Signal = new(.94f, .34f, .47f),
            Secondary = new(.67f, .46f, .91f),
            Canvas = new(.029f, .034f, .060f)
        };
        var discrete = geometry with
        {
            Primary = new(.30f, .73f, .49f),
            Accent = new(.94f, .70f, .29f),
            Signal = new(.89f, .30f, .35f),
            Secondary = new(.27f, .57f, .88f),
            Canvas = new(.025f, .041f, .035f)
        };
        var probability = geometry with
        {
            Primary = new(.35f, .63f, .95f),
            Accent = new(.93f, .43f, .31f),
            Signal = new(.88f, .28f, .49f),
            Secondary = new(.46f, .76f, .61f),
            Canvas = new(.038f, .032f, .052f)
        };
        var result = id switch
        {
            "Wilberforce" or "Metronomes" or "Mechanisms" or "Escapement" or "FlyballGovernor" or "Trebuchet" or "Rattleback" or "ChainFountain" or "SlinkyDrop" => mechanism,
            "IonTrap" or "EddyBrake" or "RailGun" or "KelvinDropper" or "Lightning" or "FaradayCage" or "MagLev" or "SuperconductorLevitation" or "ChargeInField" => electric,
            "Capillarity" or "HydraulicJump" or "Tsunami" or "Siphon" or "WaterDipoles" => fluid,
            "Polarization" or "Rainbow" or "Mirage" => optic,
            "SteamEngine" or "Stirling" or "Thermoelectric" or "MaxwellDemon" => heat,
            "Hanoi" or "RubikGroup" or "NonOrientable" or "DelOperator" or "BezierSplines" or "ConvexHull" or "EllipticCurves" or "HarmonicStacking" or "WallpaperGroups" or "MobiusTransformations" or "EulerIdentity" or "GaussianElimination" => geometry,
            "Epidemic" or "Queueing" or "PrisonersDilemma" or "KalmanFilter" or "SecretaryProblem" or "BuffonNeedle" or "Benford" or "SimpsonParadox" => probability,
            _ => diagram ? discrete : classic
        };
        return id switch
        {
            "SlinkyDrop" or "Trebuchet" or "Mirage" or "MagnusEffect" or "FaradayCage" => mechanism with
            {
                Primary = new(.12f,.49f,.58f), Accent = new(.97f,.55f,.12f),
                Backdrop = new(.16f,.20f,.25f),Canvas=new(.025f,.035f,.054f),
                Light = new Vec3(-.25f,.82f,.51f).Normalized(), LightColor = new(1.85f,1.82f,1.75f),
                Worktop = SurfaceMaterial.Stone
            },
            "MechanicalZoo" => mechanism with
            {
                Primary = new(.18f, .20f, .26f),
                Accent = new(.87f, .67f, .32f),
                Material = SurfaceMaterial.Steel,
                Worktop = SurfaceMaterial.Stone
            },
            "CurvedSpacetime" => optic with
            {
                Cosmic = true,
                Primary = new(.24f, .36f, .68f),
                Accent = new(.96f, .67f, .24f)
            },
            "Hourglass" => mechanism with
            {
                Primary = new(.23f, .12f, .07f),
                Accent = new(.77f, .57f, .29f),
                Signal = new(.62f, .32f, .12f)
            },
            "RubikGroup" => geometry with
            {
                Primary = new(.055f, .56f, .16f),
                Accent = new(1f, .78f, .04f),
                Signal = new(.85f, .04f, .025f),
                Neutral = new(.94f, .94f, .92f)
            },
            "Bicycle" => classic with
            {
                Primary = new(.82f, .10f, .07f),
                Accent = new(.96f, .77f, .27f)
            },
            "Cubli" => classic with
            {
                Primary = new(.35f, .39f, .46f),
                Accent = new(.86f, .23f, .06f)
            },
            "PidLab" => classic with
            {
                Primary = new(.20f, .40f, .73f),
                Accent = new(.90f, .48f, .14f)
            },
            "Enigma" => mechanism with
            {
                Primary = new(.12f, .16f, .11f),
                Accent = new(.82f, .68f, .42f),
                Secondary = new(.52f, .40f, .24f)
            },
            "MazeLab" => discrete with
            {
                Primary = new(.17f, .54f, .45f),
                Neutral = new(.72f, .79f, .81f)
            },
            "Burnside" => geometry with
            {
                Primary = new(.22f, .70f, .59f),
                Accent = new(.90f, .40f, .54f)
            },
            "ContinuedFractions" => geometry with
            {
                Primary = new(.62f, .48f, .87f),
                Accent = new(.43f, .78f, .65f)
            },
            "Interpolation" => geometry with
            {
                Primary = new(.20f, .70f, .77f),
                Accent = new(.91f, .47f, .26f)
            },
            "LeastSquares" => geometry with
            {
                Primary = new(.76f, .46f, .82f),
                Accent = new(.98f, .69f, .27f)
            },
            "KMeans" => geometry,
            _ => result
        };
    }
}
