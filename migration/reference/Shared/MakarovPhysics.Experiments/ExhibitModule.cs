using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
using MakarovPhysics.UI.Overlays;
using MakarovPhysics.Charts;

namespace MakarovPhysics.Experiments;
/// <summary>Common presentation of an experiment: camera, safe framing,
/// instruments and history. Numerical models and apparatus belong to modules.</summary>
public abstract class ExhibitModule : PhysicsModuleBase
{
    protected readonly OrbitCamera Camera = new()
    {
        Target = new(0, 1, 0),
        Distance = 8,
        Yaw = .3f,
        Pitch = .22f,
        MinDistance = 2,
        MaxDistance = 35
    };
    protected readonly DioramaGeometry Geometry = new();
    private ExhibitAppearance? _appearance;
    protected ExhibitAppearance Appearance => _appearance ??= ExhibitAppearance.For(Info.Id, this is DiagramModule);
    // Retain the role names used by existing modules; colours now belong to the scene.
    protected Vec3 Teal => Appearance.Primary;
    protected Vec3 Gold => Appearance.Accent;
    protected Vec3 Coral => Appearance.Signal;
    protected Vec3 Ivory => Appearance.Neutral;
    protected Vec3 Ink => Appearance.Dark;
    protected Vec3 Violet => Appearance.Secondary;
    protected SceneLabels? Labels;
    protected virtual KeyboardHint[] ManualControlHints => [];
    protected virtual bool ShowManualControlHints => true;
    private KeyboardGuide? _keyboardGuide;
    protected virtual bool Table => true;
    protected virtual bool StageVisible => true;
    protected virtual float SurfaceFill => 0;
    protected virtual OutdoorLighting? SceneLighting => null;
    protected virtual float SkyDaylight => 1;
    protected virtual float EmissionGlowStrength => .55f;
    protected virtual bool EmissionGlow => Info.Id is "Lightning" or "Polarization" or "Rainbow" or "IonTrap";
    protected virtual bool DetailedSite => false;
    /// <summary>A production workshop's finish on the exhibit's own geometry and its site (bevelled precision parts,
    /// the workshop shading of StudioSurface); the engineering modules that are machines rather than mechanisms say
    /// true. Lives here because EngineeringSiteExhibit and the stage both read it.</summary>
    protected virtual bool WorkshopFinish => false;
    protected virtual double EnvironmentTime => Time;
    protected virtual double SurfaceTime => Time / Math.Max(1, TimeScale);
    protected virtual float StudioFloorY => -1.28f;
    protected virtual void FrameCamera(float aspect, SceneInsets? insets) => Camera.FrameScene(Lower, Upper, aspect, insets);
    /// <summary>Direction of the key light and its shadows. A site whose sun moves (a solar field) follows it; every
    /// other exhibit keeps its appearance's fixed light.</summary>
    protected virtual Vec3 LightDirection => Appearance.Light;
    /// <summary>Colour (linear, HDR) of the key light on the stage, the table and the site. The appearance's by
    /// default; a workshop under its own lamps overrides it (EngineeringSiteExhibit).</summary>
    protected virtual Vec3 LightColor => Appearance.LightColor;
    /// <summary>Under the day sky, where its sun is drawn; null keeps the sky's own sun.</summary>
    protected virtual Vec3? SkySun => null;
    /// <summary>Under the day sky, the meadow out to the horizon instead of leaving the ground to the exhibit.</summary>
    protected virtual bool GrassToHorizon => false;
    /// <summary>What the shadow map covers: a centre and a radius, m.</summary>
    protected virtual (Vec3 Centre, float Radius) ShadowVolume => (new(0, 1, 0), 5.4f);
    protected virtual EnvironmentKind EnvironmentStyle => this is DiagramModule ? EnvironmentKind.None : Appearance.Cosmic ? EnvironmentKind.Space : EnvironmentKind.Studio;
    protected virtual Vec3 TableHalfSize => new(3.4f, .14f, 1.9f);
    protected virtual Vec3 Lower => new(-3, -.35f, -1.6f);
    protected virtual Vec3 Upper => new(3, 3, 1.6f);
    protected virtual bool ShowTimeScale => true;
    protected virtual double MinimumTimeScale => .1;
    protected virtual double MaximumTimeScale => 4;
    private StudioSurface? _solid;
    private StudioSurface? _stage;
    private ShadowMap? _shadow;
    private PostFxStack? _glow;
    private GradientBackground? _canvas;
    private EnvironmentStage? _room;
    private UiLabel? _readout;
    private readonly List<Series> _series = [];
    private double _sampleClock;
    public override SceneBackground Background => Table ? SceneBackground.Laboratory : SceneBackground.Diagram;
    public override ModuleCapabilities Capabilities => ModuleCapabilities.RunPause | ModuleCapabilities.SimulationTime | ModuleCapabilities.AutoStart;
    protected ExhibitModule() => Dt = 1d / 240;
    protected abstract void ResetExperiment();
    protected abstract void Advance(double dt);
    protected abstract void DrawExperiment(DioramaGeometry g);
    protected abstract void Controls(UiPanel panel);
    protected abstract (string Key, double Value, string Unit)[] Readings();
    protected virtual void DrawAdditionalDepth(Mat4 lightSpace) { }
    protected virtual void DrawAdditionalEmission(Mat4 view, Mat4 projection, Vec3 eye, Mat4 lightSpace, float time) { }
    protected virtual void DrawAdditionalOpaque(Mat4 view, Mat4 projection, Vec3 eye, Mat4 lightSpace, uint shadow) { }
    protected virtual void DrawTransparent(Mat4 view, Mat4 projection, Vec3 eye) { }
    /// <summary>The exhibit's solids - stage and apparatus - as depth only,
    /// under any view-projection. A module that composites its own light
    /// through a bloom FBO (QuantumSpin's beams, ChargeInField's swarm) draws
    /// this into that FBO first, colour masked off, so the apparatus still
    /// occludes the light: the FBO has its own depth buffer (06.10).</summary>
    protected void DrawSolidsDepth(Mat4 viewProjection)
    {
        if(StageVisible) _stage?.DrawDepth(viewProjection);
        _solid?.DrawDepth(viewProjection);
    }

    protected override void ResetCore()
    {
        ResetExperiment();
        _sampleClock = 0;

        foreach (Series s in _series)
        {
            s.Clear();
        }
    }

    protected override void StepFixed(double dt) => Advance(dt);

    public override void Initialize(IModuleContext context)
    {
        base.Initialize(context);
        Geometry.CaptureSurface = true;
        Geometry.BevelBoxes = DetailedSite;
        Geometry.PrecisionFinish = WorkshopFinish;
        _solid = new StudioSurface { DetailedFinish = DetailedSite, WorkshopFinish = WorkshopFinish, Outdoor = DetailedSite && EnvironmentStyle == EnvironmentKind.DaySky };

        _canvas = new GradientBackground
        {
            BottomColor = Appearance.Canvas * .58f,
            TopColor = Appearance.Canvas * 1.8f
        };

        _room = new EnvironmentStage
        {
            Kind = EnvironmentStyle,
            StudioFloorY = this.StudioFloorY,
            StudioRadius = 9,
            CoveRadius = 3.5f,
            BackdropColor = Appearance.Backdrop,
            StudioFloorColor = Appearance.Backdrop * .48f,
            LightDir = Appearance.Light,
            FloorSheen = .28f,
            Falloff = .6f,
            StarDensity = .35f,
            NebulaAmount = .12f,
            TexturesDirectory = context.Shared.TexturesDirectory
        };

        if (this is not DiagramModule && !Appearance.Cosmic)
        {
            _shadow = new ShadowMap(this is InstrumentExhibit ? 2048 : 1536);
        }

        if (EmissionGlow)
        {
            _glow = new PostFxStack
            {
                SceneScale = .5f,
                GlowOnly = true,
                BloomStrength = EmissionGlowStrength,
                BloomThreshold = .25f,
                BloomCap = 1.5f
            };
        }

        var g = new DioramaGeometry
        {
            CaptureSurface = true
        };

        if (Table)
        {
            g.Material = Appearance.Worktop;
            g.Box(new Vec3(0, -.18f, 0), TableHalfSize, Appearance.Worktop == SurfaceMaterial.Wood ? Appearance.Wood : new Vec3(.19f, .21f, .24f));
            g.Material = SurfaceMaterial.Steel;
            g.Box(new Vec3(0, -.35f, 0), new Vec3(TableHalfSize.X-.05f, .035f, TableHalfSize.Z-.05f), new Vec3(.34f, .38f, .44f));

            for (int x = -1; x <= 1; x += 2)
            {
                for (int z = -1; z <= 1; z += 2)
                {
                    float lx = x * (TableHalfSize.X - .38f), lz = z * (TableHalfSize.Z - .37f);
                    g.Tube(new Vec3(lx, -1.22f, lz), new Vec3(lx, -.3f, lz), .07f, new Vec3(.40f, .45f, .50f));
                    g.Material = SurfaceMaterial.Rubber;
                    g.Tube(new Vec3(lx, -1.26f, lz), new Vec3(lx, -1.19f, lz), .105f, Ink);
                    g.Material = SurfaceMaterial.Steel;
                }
            }

            if (Appearance.OpticalBench)
            {
                g.Material = SurfaceMaterial.Rubber;

                for (int i = -15; i <= 15; i++)
                {
                    for (int j = -7; j <= 7; j++)
                    {
                        g.Tube(new Vec3(i * .20f, -.038f, j * .20f), new Vec3(i * .20f, -.036f, j * .20f), .014f, new Vec3(.035f, .038f, .045f));
                    }
                }
            }
        }
        else if (this is not DiagramModule && !Appearance.Cosmic)
        {
            g.Material = SurfaceMaterial.Rubber;
            g.Box(new Vec3(0, -.25f, 0), new Vec3(Math.Min(3.3f, (Upper.X - Lower.X) * .5f), .06f, Math.Min(1.7f, (Upper.Z - Lower.Z) * .5f)), Appearance.Dark);
        }

        _stage = new StudioSurface(Math.Max(3, g.SurfacePoints.Count));
        _stage.Update(g.SurfacePoints);
        if (context.GetService<UiContext>() is not { } ui) return;
        Labels = ui.Root.Add(new SceneLabels { Insets = context.GetService<SceneInsets>(), FontSize = 14 });
        KeyboardHint[] hints=ManualControlHints;
        if(hints.Length>0) _keyboardGuide=ui.Root.Add(new KeyboardGuide(context,Tr("KeyboardGuide.Title"),hints));
    }

    protected override void OnInput(InputState input)
    {
        _keyboardGuide?.Update(input);

        if (input.ConsumedByUi || input.ModalOpen)
            return;

        if ((input.Buttons & MouseButton.Right) != 0)
            Camera.Drag(input.MouseDx, input.MouseDy, .006f);

        if (input.WheelDelta != 0)
            Camera.Zoom(input.WheelDelta > 0 ? 1 / 1.12f : 1.12f);

        if ((input.Pressed & MouseButton.Left) != 0 && Context is { } c)
        {
            var p = new SceneProjection(Camera.View(), Camera.Projection((float)c.ViewportWidth / Math.Max(1, c.ViewportHeight)), c.ViewportWidth, c.ViewportHeight, 1);

            if (p.TryPhysicalRay(input.MouseX, input.MouseY, out var origin, out var direction))
                Pick(origin, direction);
        }
    }

    protected virtual SceneInsets? FrameInsets => Context?.GetService<SceneInsets>();

    protected virtual void Pick(Vec3 origin, Vec3 direction)
    {
    }

    protected override void OnUiFrame()
    {
        _readout?.Text = string.Join("\n", Readings().Select(r => $"{Tr("Module.Lab." + r.Key)}: {FormatReading(r.Value)} {r.Unit}"));
    }

    private static string FormatReading(double value) => double.IsNaN(value) ? "—" : double.IsPositiveInfinity(value) ? "∞" : double.IsNegativeInfinity(value) ? "−∞" : value.ToString(Math.Abs(value) > 0 && Math.Abs(value) < .001 ? "0.##e0" : "0.###");
    protected override void OnAfterSteps(int steps, double simulatedDt)
    {
        _sampleClock += simulatedDt;
        if (_sampleClock < 1d / 30) return;
        _sampleClock = 0;
        (string Key, double Value, string Unit)[] values = Readings();

        for (int i = 0; i < _series.Count && i < values.Length; i++)
            if (double.IsFinite(values[i].Value))
                _series[i].Add(Time, values[i].Value);
    }

    public override void BuildUi(object uiRoot)
    {
        if (uiRoot is not UiPanel panel)
            return;

        panel.Add(new UiLabel(Tr("Module.Lab.Hint")) { Wrap = true });
        Controls(panel);
        _readout = panel.Add(new UiLabel("") { Wrap = true });
        (string Key, double Value, string Unit)[] values = Readings();

        for (int i = 0; i < Math.Min(2, values.Length); i++)
        {
            LineChart chart = panel.Add(new LineChart { Title = Tr("Module.Lab." + values[i].Key) + (values[i].Unit.Length > 0 ? " (" + values[i].Unit + ")" : ""), Height = 110 });
            Vec3 color = InkColor(i == 0 ? Teal : Gold);
            Series s = new Series(1800, "", new UiColor(color.X, color.Y, color.Z));
            _series.Add(s);
            chart.Series.Add(s);
        }

        if(ShowTimeScale) panel.Add(new Slider(Tr("Module.TimeScale"), () => TimeScale, v => TimeScale = v, MinimumTimeScale, MaximumTimeScale) { Format = MaximumTimeScale < .1 ? "0.0000" : MaximumTimeScale <= .5 ? "0.00" : "0.0", Logarithmic = true });
        OnUiFrame();
    }

    protected void Slider(UiPanel p, string key, Func<double> get, Action<double> set, double lo, double hi, string unit = "", bool reset = true)
    {
        p.Add(new Slider(Tr("Module.Lab." + key), get, v =>
        {
            set(v);
            if (reset) NewExperiment();
        }, lo, hi, unit) { Format = hi < .01 ? "0.00000" : hi < 1 ? "0.###" : "0.##" });
    }

    protected void Choice(UiPanel p, string key, string[] choices, int index, Action<int> set)
    {
        Dropdown d = p.Add(new Dropdown(Tr("Module.Lab." + key), choices.Select(k => Tr("Module.Lab." + k)).ToArray(), index));

        d.Changed += i =>
        {
            set(i);
            NewExperiment();
        };
    }

    protected void Toggle(UiPanel p, string key, bool value, Action<bool> set, bool reset = false)
    {
        Checkbox c = p.Add(new Checkbox(Tr("Module.Lab." + key), value));

        c.Changed += v =>
        {
            set(v);
            if (reset)
                NewExperiment();
        };
    }

    protected void Button(UiPanel p, string key, Action action) => p.Add(new UiButton(Tr("Module.Lab." + key), action));

    // Paint and label ink have different contrast requirements.
    private static Vec3 InkColor(Vec3 color)
    {
        float maximum = Math.Max(color.X, Math.Max(color.Y, color.Z));
        return maximum < .72f ? color * (.72f / Math.Max(.01f, maximum)) + new Vec3(.12f, .12f, .12f) : color;
    }

    protected void Pin(Vec3 p, string text, Vec3 color)
    {
        Vec3 ink = InkColor(color);
        Labels?.AddEssential(p, text, new UiColor(ink.X, ink.Y, ink.Z));
    }

    public override void Render()
    {
        if (Context is not { } c || _solid is null) return;
        float aspect = (float)c.ViewportWidth / Math.Max(1, c.ViewportHeight);
        var frameInsets=FrameInsets;
        if (_keyboardGuide is not null) _keyboardGuide.Visible = ShowManualControlHints;
        FrameCamera(aspect, _keyboardGuide is { Visible: true } guide ? guide.FrameInsets(frameInsets) : frameInsets);
        Mat4 view = Camera.View();
        Mat4 proj = Camera.Projection(aspect);
        Vec3 eye = Camera.Position();
        _canvas?.Draw();
        if (_room is {} room)
        {
            room.Kind=EnvironmentStyle;
            room.LightDir=LightDirection;
            if(SkySun is {} sun)room.SunDir=sun;
            room.MeadowGround=GrassToHorizon;
        }
        if (_room is not null) { _room.DetailedWeather = DetailedSite; _room.Daylight = SkyDaylight; }
        if(!DetailedSite) _room?.Draw(view, proj, eye, (float)EnvironmentTime, Camera.Far);
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
        GL.Disable(GL.BLEND);
        GL.Disable(GL.CULL_FACE);

        if (Labels is not null)
        {
            Labels.Projection = new SceneProjection(view, proj, c.ViewportWidth, c.ViewportHeight, c.GetService<UiContext>()?.Scale ?? 1);
            Labels.Begin();
        }

        Geometry.Clear();
        Geometry.Eye = eye;
        DrawExperiment(Geometry);

        if (Geometry.Points.Count > 600000)
            throw new InvalidOperationException("Experiment exceeded its geometry budget");

        // Existing scenes name parts by semantic colour. Preserve explicit
        // materials and literal data colours; add metal only to known roles.
        if (this is not DiagramModule)
        {
            for (int i = 0; i < Geometry.SurfacePoints.Count; i++)
            {
                SurfaceVertex v = Geometry.SurfacePoints[i];

                if (v.Material != SurfaceMaterial.Automatic)
                    continue;

                var material = v.Color.Equals(Ivory) 
                    ? SurfaceMaterial.Steel 
                    : v.Color.Equals(Gold) 
                        ? SurfaceMaterial.Brass 
                        : v.Color.Equals(Ink) 
                            ? SurfaceMaterial.Rubber 
                            : Appearance.Material;

                Geometry.SurfacePoints[i] = v with
                {
                    Material = material
                };
            }
        }

        // Flat diagrams encode values in colour; scene lighting must not dim
        // fine axes, graph edges or scatter points in the catalogue previews.
        _solid.Update(Geometry.SurfacePoints, this is DiagramModule ? SurfaceMaterial.Flat : Appearance.Material);
        var (shadowCentre, shadowRadius) = ShadowVolume;
        Vec3 light = LightDirection;
        Mat4 lightSpace = ShadowMap.LightSpace(shadowCentre, light, shadowRadius);

        if (_shadow is not null)
        {
            int renderScale = GL.RenderScale;
            uint draw = (uint)GL.GetInteger(0x8CA6), read = (uint)GL.GetInteger(0x8CAA);
            (int X, int Y, int Width, int Height) viewport = GL.PhysicalViewport();
            bool scissor = GL.IsEnabled(GL.SCISSOR_TEST);

            try
            {
                GL.RenderScale = 1;
                GL.Disable(GL.SCISSOR_TEST);
                _shadow.BeginDepthPass();
                if(StageVisible) _stage?.DrawDepth(lightSpace);
                _solid.DrawDepth(lightSpace, (float)SurfaceTime);
                DrawAdditionalDepth(lightSpace);
            }
            finally
            {
                GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, draw);
                GL.BindFramebuffer(GL.READ_FRAMEBUFFER, read);
                GL.RenderScale = renderScale;
                GL.RestorePhysicalViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
                if (scissor) GL.Enable(GL.SCISSOR_TEST);
            }
        }

        if(DetailedSite && _room is {} detailedRoom)
        {
            detailedRoom.GroundShadowTexture = _shadow?.DepthTexture ?? 0;
            detailedRoom.GroundShadowMatrix = lightSpace;
            detailedRoom.Draw(view,proj,eye,(float)EnvironmentTime,Camera.Far);
            GL.Enable(GL.DEPTH_TEST);GL.DepthMask(true);
            GL.Disable(GL.BLEND);GL.Disable(GL.CULL_FACE);
        }
        float surfaceTime = (float)SurfaceTime;
        if (StageVisible) _stage?.Draw(view, proj, eye, light, LightColor, lightSpace, _shadow?.DepthTexture ?? 0, surfaceTime,environmentFill:SurfaceFill);
        _solid.Lighting = SceneLighting;
        _solid.Draw(view, proj, eye, light, LightColor, lightSpace, _shadow?.DepthTexture ?? 0, surfaceTime,environmentFill:SurfaceFill);
        DrawAdditionalOpaque(view, proj, eye, lightSpace, _shadow?.DepthTexture ?? 0);

        if (_glow is not null)
        {
            int scale = GL.RenderScale;
            var viewport = GL.PhysicalViewport();
            uint draw = (uint)GL.GetInteger(0x8CA6), read = (uint)GL.GetInteger(0x8CAA);
            bool scissor = GL.IsEnabled(GL.SCISSOR_TEST);

            try
            {
                GL.RenderScale = 1;
                GL.Disable(GL.SCISSOR_TEST);
                _glow.BeginScene(viewport.Width, viewport.Height);
                // The single-sample fallback has no depth; preserve correct
                // occlusion by omitting the optional bloom on that hardware.
                if (_glow.SceneMsaaSamples > 0)
                {
                    GL.Enable(GL.DEPTH_TEST);
                    GL.Disable(GL.BLEND);
                    if(StageVisible) _stage?.Draw(view, proj, eye, light, LightColor, lightSpace, 0, surfaceTime, emissionOnly: true);
                    _solid.Draw(view, proj, eye, light, LightColor, lightSpace, 0, surfaceTime, emissionOnly: true);
                    DrawAdditionalEmission(view, proj, eye, lightSpace, surfaceTime);
                }

                _glow.EndScene();
                if (_glow.SceneMsaaSamples > 0) _glow.CompositeTo(draw);
            }
            finally
            {
                GL.BindFramebuffer(GL.DRAW_FRAMEBUFFER, draw);
                GL.BindFramebuffer(GL.READ_FRAMEBUFFER, read);
                GL.RenderScale = scale;
                GL.RestorePhysicalViewport(viewport.X, viewport.Y, viewport.Width, viewport.Height);
                if (scissor) GL.Enable(GL.SCISSOR_TEST);
                GL.Enable(GL.DEPTH_TEST);
                GL.Disable(GL.BLEND);
            }
        }

        DrawTransparent(view, proj, eye);
        Labels?.End();
        GL.Enable(GL.CULL_FACE);
    }

    public override void Dispose()
    {
        _solid?.Dispose();
        _stage?.Dispose();
        _shadow?.Dispose();
        _glow?.Dispose();
        _canvas?.Dispose();
        _room?.Dispose();
        base.Dispose();
    }
}
