using MakarovPhysics.Base;
using MakarovPhysics.Experiments;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
namespace Promzona.Game;

// Owns the playable city scene and its graphics resources.
public sealed class CityGame : InstrumentExhibit
{
    public override ModuleInfo Info { get; } = new("Promzona", "PROMZONA", "Drive through a living city.");
    public CityModel Model { get; }
    private CityModel _model => Model;
    public ChaseCamera Chase { get; } = new();
    private double _cameraDistance = 12;
    public int CameraMode {get;private set;}
    public void CycleCamera()=>CameraMode=(CameraMode+1)%3;
    private double ViewDistance=>Chase.Distance*(CameraMode==1?2.6:CameraMode==2?5:1);
    private double ViewPitch=>CameraMode==2?1.36:CameraMode==1?.72:Model.Traffic.OnFoot?Math.Clamp(.42-Model.Traffic.PlayerViewPitch,-.15,1.30):.42;

    public void LookPlayer(double dx, double dy)
    {
        CityTraffic t = Model.Traffic;
        t.PlayerViewHeading = Math.IEEERemainder(t.PlayerViewHeading - Math.Clamp(dx, -500, 500) * .0035, Math.Tau);
        t.PlayerViewPitch = Math.Clamp(t.PlayerViewPitch - Math.Clamp(dy, -500, 500) * .003, -.95, 1.05);
    }

    public int Quality = 2;
    public int DrawDistance = CityLod.DefaultDistance;
    public bool EffectsEnabled = true, GlowEnabled = true;
    private CitySurface? _dynamic;
    private CityDistricts? _districts;
    private CityEffects? _effects;
    private CityWeatherSky? _weatherSky;
    private CityAtmosphere _atmosphere;
    private CityTerrainLighting? _terrainLighting;
    private CityVisibility? _visibility;
    private CityVehicleModels? _vehicles;
    private CityActorModels? _actors;
    private CityPostProcess? _post;
    private CityReflections? _reflections;

    public override void Render()
    {
        if(Quality==0){base.Render();return;}
        _post ??= new();
        _post.Render(RenderScene,Camera,Quality);
    }
    private void RenderScene()=>base.Render();

    public override void Initialize(IModuleContext c)
    {
        CityAtlas.AssetRoot = c.Shared.AppDirectory;
        base.Initialize(c);
        _terrainLighting = new();
        _vehicles = new(c.Shared.AppDirectory);
        _actors = new(c.Shared.AppDirectory);
        _reflections = new();
        _vehicles.Reflections = _reflections;
    }

    public double PrepareView()
    {
        double actors=_actors?.PrepareResources()??1;
        if(actors<1)return actors*.2;
        _districts ??= new();
        _districts.Distance = DrawDistance;
        _districts.Quality = Quality;
        _districts.Update(new((float)Model.X, 0, (float)Model.Z));
        return _districts.PendingCount == 0
            ? 1
            : .2 + .8 * _districts.LoadedCount / (_districts.LoadedCount + _districts.PendingCount);
    }

    public int LoadedDistricts => _districts?.LoadedCount ?? 0;
    private readonly CityCar?[] _litCars = new CityCar?[4];

    private readonly OutdoorLighting _lighting = new()
    {
        StreetSpacing = (float)CityTraffic.Spacing, GridLow = (float)CityTraffic.Coordinate(0),
        GridHigh = (float)CityTraffic.Coordinate(CityTraffic.Nodes - 1)
    };

    protected override bool ShowCameraViews => false;
    protected override bool EmissionGlow => GlowEnabled;
    protected override float EmissionGlowStrength => .13f;
    protected override OutdoorLighting? SceneLighting => _lighting;
    protected override float SkyDaylight => (float)(_model.Daylight * (1 - .35 * _model.Rain));

    protected override Vec3? SkySun =>
        new Vec3((float)Math.Cos((_model.Hour - 6) * Math.PI / 12), (float)_model.SunHeight, .3f).Normalized();

    protected override Vec3 Lower => new((float)_model.X - 8, (float)CityTerrain.Ground(_model.X, _model.Z) - .1f,
        (float)_model.Z - 5);

    protected override Vec3 Upper => new((float)_model.X + 8, (float)CityTerrain.Ground(_model.X, _model.Z) + 5,
        (float)_model.Z + 9);

    protected override float SurfaceFill => 3.2f;

    protected override Vec3 LightDirection => new Vec3((float)Math.Cos((_model.Hour - 6) * Math.PI / 12),
        MathF.Max(.18f, MathF.Abs((float)_model.SunHeight)), .3f).Normalized();

    protected override Vec3 LightColor
    {
        get
        {
            var direct = Vec3.Lerp(new(2.3f, 1.48f, .95f), new(2.7f, 2.55f, 2.3f),
                Math.Clamp((float)_model.SunHeight * 3, 0, 1));
            var lowSun = _model.Hour > 12 ? new Vec3(3.08f, 1.56f, .85f) : new Vec3(3.0f, 1.90f, 1.17f);
            // The wet street still reflects the sky when clouds cover the solar disc.
            float directTransmission = 1 - .90f * _atmosphere.Rain * _atmosphere.Daylight;
            return Vec3.Lerp(direct, lowSun, _atmosphere.GoldenLight) * directTransmission;
        }
    }

    protected override EnvironmentKind EnvironmentStyle => EnvironmentKind.None; // Game-owned weather sky below.
    protected override bool GrassToHorizon => false;

    protected override (Vec3 Centre, float Radius) ShadowVolume => (
        new Vec3((float)_model.X, 4 + (float)CityTerrain.Ground(_model.X, _model.Z), (float)_model.Z), 54);

    public CityGame(CityModel? model = null)
    {
        Model = model ?? new();
        Dt = 1d / 60;
        MaxSubSteps = 16;
        TimeScale = 1;
        Camera.Fovy = MathF.PI / 3;
        Camera.Far = 650;
        Chase.Reset(_model.Traffic.Hero);
    }

    private bool _remoteFireRelease;
    protected override void OnInput(InputState input)
    {
        var drone=_model.Traffic;
        if((input.Buttons&MouseButton.Left)==0)_remoteFireRelease=false;
        if(!input.SceneKeyboardBlocked&&input.WasKeyPressed(0x4B))drone.ToggleDrone();
        if(drone.DroneActive){
            _remoteFireRelease=true;
            if(!input.SceneKeyboardBlocked&&input.WasKeyPressed(0x46)){drone.ToggleDrone();return;}
            bool blocked=input.SceneKeyboardBlocked;
            if(!blocked){drone.DroneHeading=Math.IEEERemainder(drone.DroneHeading-input.MouseDx*.0035,Math.Tau);drone.DronePitch=Math.Clamp(drone.DronePitch-input.MouseDy*.003,-.8,1.4);}
            var f=new CityPoint(Math.Sin(drone.DroneHeading),Math.Cos(drone.DroneHeading));var r=CityTraffic.Right(f);
            drone.DroneInput=blocked?default:f*((input.IsKeyDown(0x57)?1:0)-(input.IsKeyDown(0x53)?1:0))+r*((input.IsKeyDown(0x44)?1:0)-(input.IsKeyDown(0x41)?1:0));
            drone.DroneLiftInput=blocked?0:(input.IsKeyDown(0x51)?1:0)-(input.IsKeyDown(0x5A)?1:0);
            drone.DroneScanHeld=!blocked&&(input.Buttons&MouseButton.Left)!=0;
            drone.WalkInput=default;drone.PlayerFireHeld=false;_model.Input=default;return;
        }
        if (!input.SceneKeyboardBlocked)
        {
            if (input.WasKeyPressed(0x4e)) _model.SetHour(_model.Hour + 6);
            if (input.WasKeyPressed(0x50)) _model.SetRain(!_model.RainTarget);
            if (input.WasKeyPressed(0x52)) _model.Traffic.RecoverPlayer();
            if (input.WasKeyPressed(0x4C)) _model.Traffic.ToggleHelicopterLanding();
            if (input.WasKeyPressed(0x56)) CycleCamera();
            if (input.WasKeyPressed(0x46)) _model.Traffic.InteractVehicle();
        }

        var traffic = _model.Traffic;
        if (traffic.OnFoot && !input.SceneKeyboardBlocked)
            LookPlayer(input.MouseDx,input.MouseDy);
        if(!traffic.OnFoot&&!traffic.PassengerMode&&traffic.Hero.Kind is (11 or 14)&&!input.SceneKeyboardBlocked)
            {
            traffic.Hero.SpecialYaw=CityAim.MouseYaw(traffic.Hero.SpecialYaw,input.MouseDx);
            if (traffic.Hero.Kind == 14)
                traffic.Hero.GunElevation = Math.Clamp(traffic.Hero.GunElevation - input.MouseDy*.003, -.28, .65);
        }
        if (!input.SceneKeyboardBlocked && !traffic.PlayerUnavailable && !traffic.PassengerMode && !traffic.OnScooter &&
            (traffic.OnFoot || traffic.Hero.Kind is not (11 or 14)))
        {
            if (input.WasKeyPressed(0xDB)) traffic.Inventory.Cycle(-1);
            if (input.WasKeyPressed(0xDD)) traffic.Inventory.Cycle(1);
        }
        var forward = new CityPoint(Math.Sin(traffic.PlayerViewHeading), Math.Cos(traffic.PlayerViewHeading));
        var right = CityTraffic.Right(forward);
        traffic.WalkInput = input.SceneKeyboardBlocked
            ? default
            : forward * ((input.IsKeyDown(0x57) ? 1 : 0) - (input.IsKeyDown(0x53) ? 1 : 0)) +
              right * ((input.IsKeyDown(0x44) ? 1 : 0) - (input.IsKeyDown(0x41) ? 1 : 0));
        if (traffic.WalkInput.Length > 1) traffic.WalkInput *= 1 / traffic.WalkInput.Length;
        traffic.HelicopterLiftInput=input.SceneKeyboardBlocked?0:(input.IsKeyDown(0x51)?1:0)-(input.IsKeyDown(0x5A)?1:0);
        traffic.ScooterBrake=!input.SceneKeyboardBlocked&&input.IsKeyDown(0x20);
        traffic.Sprint = !input.SceneKeyboardBlocked && input.IsKeyDown(0x10);
        _model.Traffic.PlayerFireHeld = !_remoteFireRelease && !traffic.PassengerMode && !traffic.OnScooter && !input.SceneKeyboardBlocked && (input.Buttons & MouseButton.Left) != 0;
        _model.Input = input.SceneKeyboardBlocked
            ? default
            : new DriveInput(
                (input.IsKeyDown(0x57) ? 1 : 0) - (input.IsKeyDown(0x53) ? 1 : 0),
                (input.IsKeyDown(0x41) ? 1 : 0) - (input.IsKeyDown(0x44) ? 1 : 0), false, input.IsKeyDown(0x20));
    }

    protected override void OnVisualFrame(double realDt)
    {
        base.OnVisualFrame(realDt);
        _districts ??= new();
        _districts.Distance = DrawDistance;
        _districts.Quality = Quality;
        _districts.Update(new((float)(_model.Traffic.DroneActive?_model.Traffic.DronePosition.X:_model.X), 0, (float)(_model.Traffic.DroneActive?_model.Traffic.DronePosition.Z:_model.Z)));
        Chase.Step(_model.Traffic, realDt);
        double safe = SafeCameraDistance();
        // Retract immediately to avoid clipping; extend gently when the view clears.
        _cameraDistance = safe < _cameraDistance
            ? safe
            : _cameraDistance + (safe - _cameraDistance) * (1 - Math.Exp(-realDt * 3));
    }

    private (double X,double Y,double Z,double Heading,double Pitch,double Distance,CityLayout Layout)? _cameraProbe;
    private double _cameraSafe;
    private double SafeCameraDistance()
    {
        var probe=(Chase.Target.X,1.35+Model.Traffic.PlayerGround,Chase.Target.Z,Chase.Heading,ViewPitch,ViewDistance,CityLayout.Current);
        if(_cameraProbe==probe)return _cameraSafe;
        _cameraProbe=probe;
        double dx=-Math.Sin(Chase.Heading)*ViewDistance*Math.Cos(ViewPitch),dy=ViewDistance*Math.Sin(ViewPitch),dz=-Math.Cos(Chase.Heading)*ViewDistance*Math.Cos(ViewPitch);
        return _cameraSafe=Math.Max(.05,ViewDistance*ChaseCamera.VisibleFraction(probe.Item1,probe.Item2,probe.Item3,dx,dy,dz));
    }

    public (float X,float Y,bool Visible) DroneMarker(float width,float height){
        var d=Model.Traffic;if(!d.DroneActive||d.DroneScans>=d.DroneTargets.Length)return default;
        var b=d.DroneTargets[d.DroneScans];Vec3 p=new((float)b.X,(float)(CityTerrain.Ground(b.X,b.Z)+b.Height+.7),(float)b.Z);
        var m=Camera.Projection(width/height)*Camera.View();var clip=m.TransformPoint(p);
        float w=m[3,0]*p.X+m[3,1]*p.Y+m[3,2]*p.Z+m[3,3];
        return((clip.X/w*.5f+.5f)*width,(.5f-clip.Y/w*.5f)*height,w>0&&Math.Abs(clip.X/w)<.95&&Math.Abs(clip.Y/w)<.85);
    }
    public (float X,float Y,bool Visible) Reticle(float width,float height){
        var hit=_model.Traffic.AimHit();Vec3 p=new((float)hit.Point.X,(float)hit.Height,(float)hit.Point.Z);
        var matrix=Camera.Projection(width/height)*Camera.View();var clip=matrix.TransformPoint(p);
        float w=matrix[3,0]*p.X+matrix[3,1]*p.Y+matrix[3,2]*p.Z+matrix[3,3];
        return((clip.X/w*.5f+.5f)*width,(.5f-clip.Y/w*.5f)*height,w>0&&Math.Abs(clip.X/w)<1&&Math.Abs(clip.Y/w)<1);
    }
    protected override void FrameCamera(float aspect, SceneInsets? insets)
    {
        // Respawn/load may reset Chase after Update: validate the actual render pose.
        _cameraDistance = Math.Min(_cameraDistance, SafeCameraDistance());
        Camera.Target = new((float)Chase.Target.X, 1.35f + (float)Model.Traffic.PlayerGround,
            (float)Chase.Target.Z);
        Camera.Yaw = (float)Chase.Heading + MathF.PI;
        Camera.Pitch = (float)ViewPitch;
        Camera.Distance = (float)_cameraDistance;
        if(Model.Traffic.DroneActive){var d=Model.Traffic;
            Camera.Target=new((float)d.DronePosition.X,(float)d.DroneHeight,(float)d.DronePosition.Z);
            Camera.Yaw=(float)d.DroneHeading+MathF.PI;Camera.Pitch=(float)d.DronePitch;Camera.Distance=.06f;
        }
        Camera.Near = Model.Traffic.DroneActive?.04f:CityLod.NearPlane(Camera.Distance);
        Camera.Far = CityLod.FarPlane(DrawDistance, Camera.Distance);
        _visibility = new(Camera.Projection(aspect) * Camera.View());
        CitySurface.Wetness = (float)Model.Wetness;
        _atmosphere=CityAtmosphere.Create(Model.Rain,Model.Daylight,Model.SunHeight,Model.Wetness,Model.Hour > 12);
        CityLighting.Atmosphere=_atmosphere;
        ConfigureLights();
    }

    protected override void ResetExperiment()
    {
        Chase.Reset(_model.Traffic.Hero);
        _cameraDistance = 12;
    }

    protected override void Advance(double dt) => _model.Step(dt);

    private void ConfigureLights()
    {
        _lighting.Daylight = (float)_model.Daylight;
        Array.Clear(_lighting.Colors);
        var selected = _litCars;
        Array.Clear(selected);
        selected[0] = _model.Traffic.Hero;
        for (int k = 1; k < 4; k++)
        {
            double best = double.MaxValue;
            foreach (var car in _model.Traffic.Cars)
            {
                bool used = false;
                for (int n = 0; n < k; n++) used |= selected[n] == car;
                if (used || car.Destroyed || car.Falling) continue;
                double dx = car.Position.X - Camera.Target.X,
                    dz = car.Position.Z - Camera.Target.Z,
                    d = dx * dx + dz * dz;
                if (d < best)
                {
                    best = d;
                    selected[k] = car;
                }
            }
        }

        for (int k = 0; k < 4; k++)
            if (selected[k] is { } car && !car.Destroyed && !car.Falling)
            {
                Vec3 f = new((float)Math.Sin(car.Heading), 0, (float)Math.Cos(car.Heading)),
                    r = new(f.Z, 0, -f.X),
                    p = new((float)car.Position.X, .78f + (float)car.SurfaceHeight + (float)car.Lift,
                        (float)car.Position.Z);
                for (int side = 0; side < 2; side++)
                {
                    int at = k * 2 + side;
                    _lighting.Positions[at] = p + f * ((float)car.HalfLength + .03f) + r * ((side * 2 - 1) * .65f);
                    _lighting.Colors[at] = new Vec3(3.5f, 3.3f, 2.7f) * (1 - _lighting.Daylight);
                    _lighting.Directions[at] =
                        (f + new Vec3(0, (float)Math.Sin(car.TerrainPitch) - .10f, 0)).Normalized();
                }
            }

        int lamp = 8;
        foreach (var car in _model.Traffic.Cars)
            if (!car.Destroyed && !car.Falling && car.Kind is 3 or 4 or 6 or 7 && lamp < 12)
            {
                double dx = car.Position.X - Camera.Target.X, dz = car.Position.Z - Camera.Target.Z;
                if (dx * dx + dz * dz > 3600) continue;
                bool blue = Math.Sin(_model.Time * 9 + car.Id) > 0;
                _lighting.Positions[lamp] = new((float)car.Position.X,
                    (float)(car.SurfaceHeight + car.Lift + car.Height), (float)car.Position.Z);
                _lighting.Colors[lamp] =
                    (car.Kind == 4 ? new Vec3(.08f, .45f, 2.5f) * (blue ? 1 : .12f) :
                        blue ? new Vec3(.08f, .45f, 2.5f) : new Vec3(2.2f, .08f, .025f)) *
                    (1 - .92f * _lighting.Daylight);
                _lighting.Directions[lamp] = Vec3.Zero;
                lamp++;
            }
    }

    protected override void Controls(UiPanel p)
    {
    }

    protected override (string Key, double Value, string Unit)[] Readings() => [];

    protected override void BuildSite(DioramaGeometry g)
    {
    }

    protected override void DrawAdditionalDepth(Mat4 lightSpace)
    {
        _dynamic?.DrawDepth(lightSpace);
        _districts?.Depth(lightSpace,(float)Model.Time);
        _vehicles?.Depth(Model.Traffic.Cars, Camera.Position(), lightSpace);
        _actors?.Depth(Model.Traffic, Camera.Position(), lightSpace);
    }

    protected override void DrawAdditionalOpaque(Mat4 view, Mat4 projection, Vec3 eye, Mat4 lightSpace, uint shadow)
    {
        _weatherSky ??= new();
        if(_districts is not null)_districts.Reflections=Quality>0?_reflections:null;
        if(_dynamic is not null)_dynamic.Reflections=Quality>0?_reflections:null;
        if(_vehicles is not null)_vehicles.Reflections=Quality>0?_reflections:null;
        _weatherSky.Draw(view,projection,eye,SkySun ?? LightDirection,(float)Model.Time,Camera.Far,_atmosphere);
        if(Quality>0 && _districts is not null && _reflections is not null)
            _reflections.Update(new((float)Model.X,(float)Model.Traffic.PlayerGround+1.8f,(float)Model.Z),Model.Hour,Model.Rain,Model.Time,_districts.LoadedCount,
                (v,p,e)=>{
                    _weatherSky.Draw(v,p,e,SkySun ?? LightDirection,(float)Model.Time,180,_atmosphere);
                    _districts.Draw(v,p,e,LightDirection,LightColor,lightSpace,shadow,(float)Model.Time,_lighting);
                });
        if (_dynamic is { } dyn)
        {
            dyn.Lighting = _lighting;
            dyn.Draw(view, projection, eye, LightDirection, LightColor, lightSpace, shadow, (float)Model.Time,
                environmentFill: 2.3f);
        }

        _districts?.Draw(view, projection, eye, LightDirection, LightColor, lightSpace, shadow, (float)Model.Time,
            _lighting);
        _vehicles?.Draw(Model.Traffic.Cars, eye, view, projection, LightDirection, LightColor, lightSpace, shadow,
            _lighting, (float)Model.Time);
        _actors?.Draw(Model.Traffic, eye, view, projection, LightDirection, LightColor, lightSpace, shadow, _lighting);
        _vehicles?.DrawCabinGlass(Model.Traffic.Cars,eye,view,projection,LightDirection,LightColor,lightSpace,shadow,_lighting);
    }

    protected override void DrawAdditionalEmission(Mat4 view, Mat4 projection, Vec3 eye, Mat4 lightSpace, float time)
        => _districts?.Draw(view, projection, eye, LightDirection, LightColor, lightSpace, 0, time, _lighting, true);

    protected override void DrawTransparent(Mat4 view, Mat4 projection, Vec3 eye)
    {
        base.DrawTransparent(view, projection, eye);
        if (!EffectsEnabled) return;
        _effects ??= new();
        _effects.Draw(Model, view, projection, eye);
    }

    public override void Dispose()
    {
        _dynamic?.Dispose();
        _actors?.Dispose();
        _vehicles?.Dispose();
        _effects?.Dispose();
        _weatherSky?.Dispose();
        _districts?.Dispose();
        _terrainLighting?.Dispose();
        _post?.Dispose();
        _reflections?.Dispose();
        base.Dispose();
    }

    protected override void DrawInstrument(DioramaGeometry g, DioramaGeometry glass)
    {
        if(_vehicles is not null)_vehicles.VisibleDistance=CityLod.VehicleRange(DrawDistance);
        if(_actors is not null)_actors.VisibleDistance=CityLod.ActorRange(DrawDistance);
        CityScene.Draw(g, _model, _vehicles, _actors, _visibility,DrawDistance);
        float edge = (float)CityTraffic.LandEdge, far = 4000;
        g.Material = (SurfaceMaterial)360;

        void Water(float x0, float z0, float x1, float z1, float y)
        {
            g.Quad(new(x0, y, z0), new(x0, y, z1), new(x1, y, z1), new(x1, y, z0), new(1, 1, 1));
        }

        Water(-far, -far, -edge, far, -2.8f);
        Water(edge, -far, far, far, -2.8f);
        Water(-edge, -far, edge, -edge, -2.8f);
        Water(-edge, edge, edge, far, -2.8f);

        int puddleStart = g.SurfacePoints.Count;
        g.Material = (SurfaceMaterial)361;

        foreach (var puddle in Model.Traffic.Puddles.Where(puddle => (puddle - Model.Traffic.PlayerPosition).Length < 100))
        {
            float x = (float)puddle.X, z = (float)puddle.Z;
            for (int n = 0; n < 16; n++)
            {
                float a = n * MathF.Tau / 16, b = (n + 1) * MathF.Tau / 16;
                float ra = 1.35f + .16f * MathF.Sin(n * 4.3f), rb = 1.35f + .16f * MathF.Sin((n + 1) * 4.3f);
                g.Triangle(new(x, .035f, z), new(x + rb * MathF.Cos(b), .035f, z + rb * .65f * MathF.Sin(b)),
                    new(x + ra * MathF.Cos(a), .035f, z + ra * .65f * MathF.Sin(a)), new(1, 1, 1));
            }
        }

        CityTerrainGeometry.Conform(g, puddleStart);
        _dynamic ??= new CitySurface() { Outdoor = true, WorkshopFinish = true };
        _dynamic.DetailedFinish = Quality > 0;
        _dynamic.Update(g.SurfacePoints);
        g.Clear();
    }
}
