namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private sealed class SupportCache
    {
        public object? Layout;
        public (double X,double Z,double Angle,int Level,int Phase,double Width,double Axle) Key;
        public double Ground,Pitch,Roll;
    }
    private readonly System.Runtime.CompilerServices.ConditionalWeakTable<CityCar,SupportCache> _supportCache=new();
    public void UpdateTerrainSupport(CityCar c, bool preserveAirHeight = false)
    {
        if (c.Falling) return;
        if (!_useScenery)
        {
            c.TerrainActive = false;
            c.GroundHeight = c.TerrainPitch = c.TerrainRoll = 0;
            return;
        }

        var cache=_supportCache.GetOrCreateValue(c);
        var key=(c.Body.X,c.Body.Y,c.Body.Angle,c.RoadLevel,c.Phase,c.HalfWidth,c.Wheelbase);
        // Stopped far traffic has exactly the same four contact points. Reuse
        // its rigid support plane, with layout/pose/layer invalidation.
        if(!c.Physical&&c.TerrainActive&&ReferenceEquals(cache.Layout,CityLayout.Current)&&cache.Key==key&&
            c.GroundHeight==cache.Ground&&c.TerrainPitch==cache.Pitch&&c.TerrainRoll==cache.Roll)return;
        long stamp=MeasureStep?System.Diagnostics.Stopwatch.GetTimestamp():0;
        var terrain = CityTerrain.Current;
        double x = c.Body.X, z = c.Body.Y, h = -c.Body.Angle;
        double sx = Math.Sin(h), sz = Math.Cos(h), rx = sz, rz = -sx;
        double axle = c.Wheelbase * .5, track = c.HalfWidth * .88;
        
        int previousLevel=c.RoadLevel;
        if(c.Phase!=3)c.RoadLevel=CityRoadLevels.Select(x,z,c.RoadLevel,c.TerrainActive?c.GroundHeight+c.Lift:terrain.Height(x,z));
        // Keep all wheel probes on the body's selected road. A tyre extending
        // beyond a tunnel wall must not sample the district above the ceiling
        // before the horizontal contact solver has separated the vehicle.
        var supportRoad=CityRoadLevels.ById(c.RoadLevel);
        double H(double f, double r) {
            double wx=x+sx*f+rx*r,wz=z+sz*f+rz*r;
            return supportRoad is null?CityTerrain.Ground(wx,wz):supportRoad.Floor(wx,wz);
        }

        double fl = H(axle, -track), fr = H(axle, track), rl = H(-axle, -track), rr = H(-axle, track);
        double ground = (fl + fr + rl + rr) * .25;
        // A twisted four-corner patch cannot fit a rigid plane exactly. Lift the
        // plane by its largest residual instead of burying either low-side tyre.
        double twist = (fl + rr - fr - rl) * .25;
        ground += Math.Abs(twist);
        // Lift remains an offset from support. Preserve absolute height for airborne
        // cars, so crossing a hollow does not teleport them down to its surface.
        bool lostSupport = previousLevel != c.RoadLevel && supportRoad is not null &&
            supportRoad.Open(x,z) && c.GroundHeight > ground + .15;
        if (c.TerrainActive && (lostSupport || (preserveAirHeight || c.Kind==12) && c.Lift > .03))
            c.Lift = Math.Max(0, c.Lift + c.GroundHeight - ground);
        c.GroundHeight = ground;
        c.TerrainPitch = Math.Atan2((fl + fr - rl - rr) * .5, c.Wheelbase);
        c.TerrainRoll = -Math.Atan2((fr + rr - fl - rl) * .5, 2 * track);
        if(c.Kind==12&&c.Lift>.3)c.TerrainPitch=c.TerrainRoll=0;
        c.TerrainActive = true;
        cache.Layout=CityLayout.Current;cache.Key=(x,z,c.Body.Angle,c.RoadLevel,c.Phase,c.HalfWidth,c.Wheelbase);
        cache.Ground=c.GroundHeight;cache.Pitch=c.TerrainPitch;cache.Roll=c.TerrainRoll;
        if(MeasureStep)LastStepMilliseconds[11]+=(System.Diagnostics.Stopwatch.GetTimestamp()-stamp)*1000d/System.Diagnostics.Stopwatch.Frequency;
    }

    private readonly List<(CityPedestrian Actor, double Ground)> _airPeople = [];
    private readonly List<(CityAnimal Actor, double Ground)> _airAnimals = [];
    private readonly List<(StreetProp Prop, double Ground)> _airProps = [];

    private void CaptureAirSupport()
    {
        _airPeople.Clear();
        _airAnimals.Clear();
        _airProps.Clear();

        if (!_useScenery) return;

        foreach (CityPedestrian p in Pedestrians)
        {
            if (p.Physical && (p.FlightHeight > .001 || p.VerticalSpeed > 0))
            {
                _airPeople.Add((p, CityRoadLevels.Ground(p)));
            }
        }

        foreach (CityAnimal a in Animals)
        {
            if (a is { Physical: true, Flying: false } && (a.Height > .001 || a.VerticalSpeed > 0))
            {
                _airAnimals.Add((a, CityTerrain.Ground(a.Position)));
            }
        }

        foreach (StreetProp p in _liveProps)
        {
            if (p.Body is not null && (p.Height > .001 || p.Vy > 0))
            {
                _airProps.Add((p, CityTerrain.Ground(p.Position)));
            }
        }
    }

    private void RestoreAirSupport()
    {
        foreach ((CityPedestrian p, double ground) in _airPeople)
        {
            p.FlightHeight = Math.Max(0, p.FlightHeight + ground - CityRoadLevels.Surface(p.Body.X, p.Body.Y,p.RoadLevel));

            if (p.FlightHeight == 0)
            {
                p.VerticalSpeed = Math.Max(0, p.VerticalSpeed);
            }
        }

        foreach ((CityAnimal a, double ground) in _airAnimals)
        {
            if (a.Body is { } b)
            {
                a.Height = Math.Max(0, a.Height + ground - CityTerrain.Ground(b.X, b.Y));
                if (a.Height == 0) a.VerticalSpeed = Math.Max(0, a.VerticalSpeed);
            }
        }

        foreach ((StreetProp p, double ground) in _airProps)
        {
            p.Height = Math.Max(0, p.Height + ground - CityTerrain.Ground(p.Position));

            if (p.Height == 0)
            {
                p.Vy = Math.Max(0, p.Vy);
            }
        }

        _airPeople.Clear();
        _airAnimals.Clear();
        _airProps.Clear();
    }

    internal static void PropSlope(StreetProp p, double dt)
    {
        if (p.Body is not { } b || p.Height > .02) return;
        CityPoint g = CityTerrain.Current.Gradient(b.X, b.Y);
        double slope = g.Length;
        bool rolling = p.Kind is StreetKind.Bottle or StreetKind.Can;

        // Static friction keeps furniture in place. Dislodged litter can roll or
        // slide downhill; only already-local prop bodies participate.
        double friction = rolling ? .025 : p.Moved ? .30 : .55;
        if (slope <= friction) return;
        double force = 9.81 / (1 + slope * slope) * (1 - friction / slope);
        b.Wake();
        b.Vx -= g.X * force * dt;
        b.Vy -= g.Z * force * dt;
    }
}
