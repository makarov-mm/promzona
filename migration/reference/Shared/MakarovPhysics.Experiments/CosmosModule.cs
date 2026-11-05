using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
using MakarovPhysics.UI.Overlays;
using MakarovPhysics.Charts;

namespace MakarovPhysics.Experiments;
/// <summary>A space experiment's presentation, never its scientific model.
/// One depth buffer is shared by surfaces, dust, orbit guides and emission.</summary>
public abstract partial class CosmosModule : PhysicsModuleBase
{
    protected readonly OrbitCamera Camera = new()
    {
        Distance = 10,
        Yaw = .22f,
        Pitch = .30f,
        MinDistance = 1.6f,
        MaxDistance = 180,
        Far = 1500
    };
    protected readonly CosmosScene Scene = new();
    public double Epoch { get; set; } = .25;

    protected bool Guides = true;
    protected float Exposure = 1;
    protected virtual float Extent => 3.5f;
    protected virtual float VerticalExtent => 2.1f;
    protected virtual int GraphCount => 2;
    protected virtual double InitialEpoch => .25;
    protected virtual float InitialPitch => .30f;
    protected virtual float InitialYaw => .22f;
    protected virtual float InitialStars => .12f;
    protected float StarFieldStrength => _starField;
    protected virtual float NebulaStrength => .025f;
    protected virtual bool FlatPresentation => false;
    protected virtual bool ShowTimeline => true;
    /// <summary>Celestial bodies through the linear-HDR opaque pass (bloom on
    /// the Sun, multisampled silhouettes). A module that draws its own depth
    /// before the painter would turn it off; none of the Cosmos modules do.</summary>
    protected virtual bool HdrBodies => true;
    /// <summary>Orientation of the real sky behind the scene (identity: the
    /// celestial pole up, RA 0 along +x). A module whose frame is the ecliptic
    /// or an observer's horizon turns it here.</summary>
    protected virtual Mat4 SkyRotation => Mat4.Identity;
    private bool _constellations;
    protected virtual bool InstrumentsEnabled => true;
    protected virtual Vec3[]? ExplicitFraming => null;
    protected virtual float ExtraTopInset(float logicalHeight) => 0;
    protected virtual float ExtraBottomInset(float logicalHeight) => 0;
    protected virtual void DrawOverlay(Draw2D draw, UiRect bounds) { }
    protected virtual bool Repeat => true;
    protected virtual double CycleSeconds => 45;

    private CosmosPainter? _painter;
    private EnvironmentStage? _space;
    protected virtual void ConfigureEnvironment(EnvironmentStage environment) { }
    private SceneLabels? _labels;
    private UiLabel? _readout;
    private SceneDashboard? _dashboard;
    private float _glow = 1, _starField = .12f;
    private bool _instruments = true;
    private int _cameraView;
    private readonly SceneInsets _framing = new();
    private readonly List<Series> _series = [];
    private double _sample;
    private readonly List<Func<double>> _inputs = [];
    private double[] _framedInputs = [];
    private Vec3[] _envelope = [];
    public override SceneBackground Background => FlatPresentation ? SceneBackground.Diagram : SceneBackground.Stars;
    public override ModuleCapabilities Capabilities => ModuleCapabilities.RunPause | ModuleCapabilities.AutoStart;

    protected CosmosModule()
    {
        Dt = 1d / 60;
        MaxSubSteps = 8;
    }

    protected abstract void Compose();
    protected abstract void Controls(UiPanel panel);
    protected virtual void AppearanceControls(UiPanel panel) { }
    protected virtual void ComputationControls(UiPanel panel) { }
    protected abstract (string Key, double Value, string Unit)[] Readings();
    protected virtual void ResetParameters()
    {
    }

    protected override void ResetCore()
    {
        Epoch = InitialEpoch;
        ResetParameters();
        ClearHistory();
    }

    protected override void StepFixed(double dt)
    {
        Epoch += dt / CycleSeconds;
        if (Epoch > 1)
        {
            Epoch = Repeat ? Epoch % 1 : 1;
            if (Repeat)
                ClearHistory();
        }
    }

    public override void Initialize(IModuleContext context)
    {
        base.Initialize(context);
        Camera.Pitch = InitialPitch;
        Camera.Yaw = InitialYaw;
        _starField = InitialStars;
        _painter = new CosmosPainter(context.Shared.TexturesDirectory);
        _space = new EnvironmentStage
        {
            Kind = EnvironmentKind.Space,
            StarDensity = .12f,
            NebulaAmount = NebulaStrength,
            TexturesDirectory = context.Shared.TexturesDirectory,
            SkyRotation = SkyRotation
        };
        if (context.GetService<UiContext>()is { } ui)
        {
            _labels = ui.Root.Add(new SceneLabels { Insets = context.GetService<SceneInsets>(), FontSize = 13 });
            _dashboard = ui.Root.Add(new SceneDashboard(context, (d,a)=>{DrawOverlay(d,a);DrawInstruments(d,a);}));
        }
    }

    protected string T(string key) => Tr("Module.Lab." + key);
    protected void Parameter(UiPanel panel, string key, Func<double> get, Action<double> set, double low, double high, string unit = "", bool logarithmic = false)
    {
        _inputs.Add(get);
        panel.Add(new Slider(T(key), get, v =>
        {
            set(v);
            ClearHistory();
        }, low, high, unit) { Format = logarithmic && low < .001 || high < .02 ? "G4" : "0.###", Logarithmic = logarithmic });
    }

    protected void Action(UiPanel panel, string key, Action action) => panel.Add(new UiButton(T(key), () =>
    {
        action();
        ClearHistory();
    }));
    private void ClearHistory()
    {
        foreach (var s in _series)
            s.Clear();
        _sample = 0;
    }

    public override void BuildUi(object uiRoot)
    {
        if (uiRoot is not UiPanel panel)
            return;
        panel.Add(new UiLabel(T("Hint")) { Wrap = true });
        var mode = panel.Add(new Dropdown(Tr("Cosmos.Panel"), [Tr("Cosmos.Experiment"), Tr("Cosmos.View"), Tr("Cosmos.Measurements")]));
        var experiment = panel.Add(new UiPanel { DrawBackground = false });
        var appearance = panel.Add(new UiPanel { DrawBackground = false, Visible = false });
        var measurements = panel.Add(new UiPanel { DrawBackground = false, Visible = false });
        mode.Changed += i =>
        {
            experiment.Visible = i == 0;
            appearance.Visible = i == 1;
            measurements.Visible = i == 2;
        };
        ComputationControls(experiment);
        Controls(experiment);
        if (ShowTimeline) experiment.Add(new Slider(T("Epoch"), () => Epoch, v =>
        {
            Epoch = Math.Clamp(v, 0, 1);
            ClearHistory();
        }, 0, 1) { Format = "0.00" });
        var guides = appearance.Add(new Checkbox(T("Guides"), Guides));
        guides.Changed += v => Guides = v;
        appearance.Add(new Slider(T("Exposure"), () => Exposure, v => Exposure = (float)v, .45, 2) { Format = "0.00" });
        AppearanceControls(appearance);
        if (!FlatPresentation)
        {
        var views = appearance.Add(new Dropdown(Tr("Cosmos.Camera"), [Tr("Camera.Overview"), Tr("Cosmos.Pole"), Tr("Cosmos.Equator"), Tr("Cosmos.Follow"), Tr("Cosmos.Cinematic")]));
        views.Changed += i =>
        {
            _cameraView = i;
            ResetView();
        };
        appearance.Add(new Slider(Tr("Cosmos.Glow"), () => _glow, v => _glow = (float)v, .15, 1.8) { Format = "0.00" });
        appearance.Add(new Slider(Tr("Cosmos.Stars"), () => _starField, v => _starField = (float)v, 0, .6) { Format = "0.00" });
        var constellations = appearance.Add(new Checkbox(Tr("Cosmos.Constellations"), false));
        constellations.Changed += v => _constellations = v;
        var instruments = appearance.Add(new Checkbox(Tr("Cosmos.Instruments"), true));
        instruments.Changed += v => _instruments = v;
        appearance.Add(new UiLabel(Tr("Cosmos.Mouse")) { Wrap = true, Dim = true });
        Action(appearance, "Camera", () =>
        {
            ResetView();
        });
        }
        _readout = measurements.Add(new UiLabel("") { Wrap = true });
        foreach (var r in Readings().Take(GraphCount))
        {
            var chart = measurements.Add(new LineChart { Title = T(r.Key) + (r.Unit.Length == 0 ? "" : " (" + r.Unit + ")"), Height = 100 });
            var s = new Series(1400, "", _series.Count == 0 ? new UiColor(.45f, .79f, 1) : new UiColor(1, .67f, .32f));
            _series.Add(s);
            chart.Series.Add(s);
        }

        measurements.Add(new UiLabel(T("Limits")) { Wrap = true, Dim = true });
        panel.Add(new Slider(Tr("Module.TimeScale"), () => TimeScale, v => TimeScale = v, .05, 4) { Logarithmic = true, Format = "0.00" });
    }

    protected override void OnUiFrame()
    {
        if (_readout != null)
            _readout.Text = string.Join("\n", Readings().Select(r => $"{T(r.Key)}: {r.Value:G5} {r.Unit}"));
    }

    protected override void OnAfterSteps(int steps, double simulatedDt)
    {
        _sample += simulatedDt;
        if (_sample < .08)
            return;
        _sample = 0;
        var data = Readings();
        for (int i = 0; i < _series.Count && i < data.Length; i++)
            if (double.IsFinite(data[i].Value))
                _series[i].Add(Epoch, data[i].Value);
    }

    protected override void OnInput(InputState input)
    {
        if (input.ConsumedByUi || input.ModalOpen || FlatPresentation)
            return;
        if ((input.Buttons & (MouseButton.Left | MouseButton.Right)) != 0)
            Camera.Drag(input.MouseDx, input.MouseDy, .006f);
        if (input.WheelDelta != 0)
            Camera.Zoom(input.WheelDelta > 0 ? 1 / 1.12f : 1.12f);
    }

    protected void InvalidateFraming()=>_envelope=[];
    protected void ValueLabel(Vec3 at,string text,UiColor color)=>_labels?.AddEssential(at,text,color);
    /// <summary>The Blue Marble texture for a flat map in the overlay, when the painter found it.</summary>
    protected Texture? EarthMap => _painter?.EarthMap;

    protected void Label(Vec3 at, string key)
    {
        if (Guides)
            _labels?.Add(at, T(key), new UiColor(.83f, .9f, 1));
    }

    // Fit the actual experiment across its timeline. Recompute only when inputs
    // change: expansion remains visible, and a moving object cannot pump the camera.
    private void PrepareEnvelope()
    {
        if (ExplicitFraming is { } fixedPoints) { _envelope = fixedPoints; return; }
        var values = _inputs.Select(get => get()).ToArray();
        if (_envelope.Length != 0 && values.SequenceEqual(_framedInputs))
            return;
        _framedInputs = values;
        var points = new List<Vec3>();
        double saved = Epoch;
        bool savedGuides = Guides;
        try
        {
            Guides = true;
            for (int phase = 0; phase <= 8; phase++)
            {
                Epoch = phase / 8d;
                Scene.Clear();
                Scene.Guides = true;
                Compose();
                foreach (var b in Scene.Bodies)
                    for (int k = 0; k < 96; k++)
                    {
                        var d = CosmosScene.Direction(k, 77);
                        float halo = b.Surface == CosmicSurface.Oscillation ? 1 + Math.Abs(b.Sun.Y) : b.Surface is CosmicSurface.Star or CosmicSurface.Solar ? 1.2f : 1;
                        points.Add(b.Center + new Vec3(d.X * b.Size.X, d.Y * b.Size.Y, d.Z * b.Size.Z) * halo);
                    }

                foreach (var p in Scene.Lines)
                    points.Add(p.Pos);
                for (int i = 0; i < Scene.Stars.Count; i += 16)
                    points.Add(Scene.Stars[i].Pos);
                for (int i = 0; i < Scene.Gas.Count; i += 16)
                    points.Add(Scene.Gas[i].Pos);
                // Sampling every twelfth vertex can omit three corners of a quad
                // and the far end of a strut. Preserve the complete hardware bound.
                if (Scene.Geometry.SurfacePoints.Count > 0)
                {
                    var lo = Scene.Geometry.SurfacePoints[0].Position;
                    var hi = lo;
                    foreach (var vertex in Scene.Geometry.SurfacePoints)
                    {
                        var p = vertex.Position;
                        lo = new(Math.Min(lo.X, p.X), Math.Min(lo.Y, p.Y), Math.Min(lo.Z, p.Z));
                        hi = new(Math.Max(hi.X, p.X), Math.Max(hi.Y, p.Y), Math.Max(hi.Z, p.Z));
                    }

                    for (int k = 0; k < 8; k++)
                        points.Add(new((k & 1) == 0 ? lo.X : hi.X, (k & 2) == 0 ? lo.Y : hi.Y, (k & 4) == 0 ? lo.Z : hi.Z));
                }

                foreach (var volume in Scene.Volumes)
                    for (int k = 0; k < 96; k++)
                    {
                        var d = CosmosScene.Direction(k, 91);
                        points.Add(volume.Center + new Vec3(d.X * volume.Size.X, d.Y * volume.Size.Y, d.Z * volume.Size.Z));
                    }
            }
        }
        finally
        {
            Epoch = saved;
            Guides = savedGuides;
        }

        _envelope = points.Where(p => p.IsFinite).ToArray();
    }

    public override void Render()
    {
        if (Context is not { } c || _painter == null)
            return;
        if(FlatPresentation){ GL.Clear(GL.DEPTH_BUFFER_BIT); return; }
        float aspect = (float)c.ViewportWidth / Math.Max(1, c.ViewportHeight);
        PrepareEnvelope();
        var reserved = c.GetService<SceneInsets>();
        float logicalHeight = c.ViewportHeight / (c.GetService<UiContext>()?.Scale ?? 1);
        _framing.Top = Math.Max(reserved?.Top ?? 0, .06f) + ExtraTopInset(logicalHeight);
        _framing.Bottom = (reserved?.Bottom ?? 0) + (_instruments && InstrumentsEnabled && logicalHeight > 430 ? 82 / logicalHeight : 0) + ExtraBottomInset(logicalHeight);
        SteerCamera(aspect, Time);
        var view = Camera.View();
        var projection = Camera.Projection(aspect);
        var eye = Camera.Position();
        if (_space != null)
        {
            _space.StarDensity = _starField;
            _space.Constellations = _constellations;
            _space.ViewportHeight = c.ViewportHeight;
            ConfigureEnvironment(_space);
        }
        _space?.Draw(view, projection, eye, (float)Time, Camera.Far);
        if (_labels != null)
        {
            _labels.Projection = new SceneProjection(view, projection, c.ViewportWidth, c.ViewportHeight, c.GetService<UiContext>()?.Scale ?? 1);
            _labels.Begin();
        }

        Scene.Clear();
        Scene.Guides = Guides;
        Compose();
        Scene.SurfacePhase = (float)(Epoch * Math.Tau);
        // The dome carries no depth, so the bodies may take the HDR path: linear
        // radiance, bloom on the Sun, multisampled silhouettes against the stars.
        _painter.Draw(Scene, view, projection, eye, c.ViewportHeight, Exposure, _glow, hdr: HdrBodies);
        _labels?.End();
    }

    public override void Dispose()
    {
        _painter?.Dispose();
        _space?.Dispose();
        _painter = null;
        _space = null;
        base.Dispose();
    }
}
