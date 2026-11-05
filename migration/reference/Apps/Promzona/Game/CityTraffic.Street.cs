namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public List<CityPoint> Puddles { get; } = [];
    public List<StreetProp> Props { get; } = [];
    // Props never leave the list, so the per-substep passes only need the few
    // hundred that currently own a body, and point queries need a cell index
    // rather than all ~15 000 bins and lamps of the island.
    private readonly List<StreetProp> _liveProps = [];
    private readonly Dictionary<(int, int), List<StreetProp>> _propCells = [];
    public IReadOnlyList<StreetProp> LiveProps => _liveProps;
    public List<CityParticle> Particles { get; } = [];
    public List<CityMark> Marks { get; } = [];
    public int PropRespawns { get; private set; }
    private double _propClock;
    private uint _effectSeed = 4191;

    private double Noise()
    {
        _effectSeed ^= _effectSeed << 13;
        _effectSeed ^= _effectSeed >> 17;
        _effectSeed ^= _effectSeed << 5;
        return (_effectSeed + .5) / 4294967296d;
    }

    private void ResetStreet()
    {
        Puddles.Clear();
        for (int j = 0; j < Nodes - 1; j++)
        for (int i = 0; i < Nodes - 1; i++)
            if ((i * 7 + j * 11) % 13 == 0 && HasRoad(i, j, 0))
                Puddles.Add(new(Coordinate(i) + 1.8, BlockCentre(j)));
        Props.Clear();
        Particles.Clear();
        Marks.Clear();
        PropRespawns = 0;
        _propClock = 0;
        _effectSeed = 4191;
        int id = 0;
        foreach (var b in CityModel.Furniture.Where(b => b.Height < 2))
            Props.Add(new()
            {
                Id = ++id, Home = new(b.X, b.Z),
                Kind = id % 9 == 0 ? StreetKind.Dumpster : id % 3 == 0 ? StreetKind.Hydrant : StreetKind.Bin
            });
        for (int j = 0; j < Nodes - 1; j++)
        for (int i = 0; i < Nodes - 1; i++)
        {
            if(CityLayout.CanalColumn(i))continue;
            double x = BlockCentre(i), z = BlockCentre(j);
            if (CityModel.ParkBlock(i, j))
                for (int n = -1; n <= 1; n += 2)
                    Props.Add(new() { Id = ++id, Home = new(x + n * 8, z + 8), Kind = StreetKind.Bench });
            if ((i * 13 + j * 7) % 11 == 0)
                Props.Add(new() { Id = ++id, Home = new(x - (CityLayout.WideRoads?8.5:11.8), z - (CityLayout.WideRoads?8.5:11.6)), Kind = (StreetKind)(8 + (i + j) % 3) });
            for (int d = 0; d < 2; d++)
                if (!HasRoad(i, j, d))
                {
                    var f = Forward(d);
                    var mid = new CityPoint(Coordinate(i), Coordinate(j)) + f * 18;
                    for (int side = -1; side <= 1; side += 2)
                        Props.Add(new()
                            { Id = ++id, Home = mid + f * (side * 4.5) + Right(f) * 2.5, Kind = StreetKind.Bench });
                }

            for (int n = 0; n < 3 + (int)(Noise() * 4); n++)
            {
                bool lawn = CityModel.ParkBlock(i, j) && n % 2 == 0;
                var at = new CityPoint(x - 11 + Noise() * 22, lawn ? z - 10 + Noise() * 20 : z - (CityLayout.WideRoads?9.5:12.4) + Noise() * .65);
                Props.Add(new()
                {
                    Id = ++id, Home = at,
                    Kind = n==2&&(i+j)%3==0 ? StreetKind.GarbageBag : lawn ? (n % 4 == 0 ? StreetKind.Bottle : StreetKind.Can) : (StreetKind)(3 + n % 3)
                });
            }
        }
        Props.RemoveAll(p=>!CityWater.UrbanSite(p.Home,p.Radius+.2)||!CityAmenities.ClinicClear(p.Home)||!CityAmenities.PropClear(p.Home,p.Radius+.15));
        RebuildPropGrid();
    }

    // Rebuilt once per frame and after every streaming pass; a pushed bin moves
    // under half a metre between rebuilds, which the query margins cover.
    private int _propGridCount = -1;

    private void RebuildPropGrid()
    {
        foreach (List<StreetProp> list in _propCells.Values) list.Clear();
        _liveProps.Clear();
        _propGridCount = Props.Count;
        foreach (var p in Props)
        {
            CityPoint at = p.Position;
            (int, int) key = ((int)Math.Floor(at.X / 16), (int)Math.Floor(at.Z / 16));
            if (!_propCells.TryGetValue(key, out List<StreetProp>? list)) _propCells[key] = list = [];
            list.Add(p);
            if (p.Body is not null) _liveProps.Add(p);
        }
    }

    public IEnumerable<StreetProp> PropsInBox(double x0, double z0, double x1, double z1)
    {
        // A test fixture adds or clears props between steps; a changed count means a stale index.
        if (_propGridCount != Props.Count) RebuildPropGrid();
        for (int x = (int)Math.Floor(x0 / 16); x <= (int)Math.Floor(x1 / 16); x++)
        for (int z = (int)Math.Floor(z0 / 16); z <= (int)Math.Floor(z1 / 16); z++)
            if (_propCells.TryGetValue((x, z), out List<StreetProp>? list))
                foreach (StreetProp p in list) yield return p;
    }

    private IEnumerable<StreetProp> NearbyProps(CityPoint p, double radius) => PropsInBox(p.X - radius, p.Z - radius, p.X + radius, p.Z + radius);

    private void StreamProps()
    {
        if (!_useScenery || Cars.Count == 0) return;
        var hero = PlayerPosition;
        _unloadBatch.Clear();
        foreach (var p in Props)
        {
            double home = (p.Home - hero).Length, away = (p.Position - hero).Length;
            if (p.Body is { } b)
            {
                // Neither a reset nor an unload may occur near another physical actor.
                if (home > 145 && away > 145 &&
                    !NearbyCars(p.Home, 12).Any(c => c.Physical && (c.Position - p.Home).Length < 12) &&
                    !NearbyCars(p.Position, 12).Any(c => c.Physical && (c.Position - p.Position).Length < 12)
                    && !NearbyPeople(p.Home, 3).Any(a => a.Physical && (a.Position - p.Home).Length < 3) &&
                    !NearbyPeople(p.Position, 3).Any(a => a.Physical && (a.Position - p.Position).Length < 3))
                {
                    bool moved = p.Moved;
                    _unloadBatch.Add(b);
                    p.Body = null;
                    p.Height = p.Vy = p.Tilt = p.Spin = p.Water = p.DisplacedFor = 0;
                    if (moved) PropRespawns++;
                }
            }
            else if (home < 105)
            {
                // Do not materialize a prop inside an actor.
                if (NearbyCars(p.Home, 12).Any(c =>
                        CarPersonOverlap(c.Position, c.Heading, p.Home, p.Radius, c.HalfLength, c.HalfWidth))
                    || NearbyPeople(p.Home, 3)
                        .Any(a => a.Physical && (a.Position - p.Home).Length < p.Radius + .35)) continue;
                p.Body = World.Bodies[AddPropBody(p, p.Home.X, p.Home.Z)];
            }
        }
        if (_unloadBatch.Count > 0) { World.RemoveBodies(_unloadBatch); _unloadBatch.Clear(); }
        RebuildPropGrid();
    }

    /// <summary>The one footprint, mass and material of a street prop's body,
    /// shared by streaming and by the save loader: a dumpster restored from a
    /// file used to come back as a 1.7 m square instead of its 1.5 × 1.1 m box.</summary>
    private int AddPropBody(StreetProp p, double x, double z) => p.Kind switch
    {
        StreetKind.PhoneBooth or StreetKind.DrinkMachine => World.AddBox(x, z, p.Radius, p.Radius, p.Mass / (4 * p.Radius * p.Radius), .5, .15, 0),
        StreetKind.Dumpster => World.AddBox(x, z, .75, .55, p.Mass / 1.65, .45, .12, 0),
        StreetKind.Bench => World.AddBox(x, z, 1, .28, p.Mass / 1.12, .4, .18, 0),
        _ => World.AddCircle(x, z, p.Radius, p.Mass / (Math.PI * p.Radius * p.Radius), .35, .22, 0)
    };

    private readonly List<MakarovPhysics.RigidBodies2D.Body2D> _unloadBatch = [];

    public void Emit(double x, double y, double z, int kind, int count, double speed = 3,int roadLevel=0)
    {
        for (int i = 0; i < count && Particles.Count < 900; i++)
        {
            double angle = Noise() * Math.Tau, lift = Noise();
            Particles.Add(new()
            {
                RoadLevel=roadLevel, X = x, Y = y + (_useScenery ? CityRoadLevels.Surface(x, z,roadLevel) : 0), Z = z, Vx = Math.Cos(angle) * speed * lift,
                Vz = Math.Sin(angle) * speed * lift, Vy = speed * (.3 + Noise()), Kind = kind,
                Life = kind == 4 ? .5 + Noise() * .5 :
                    kind == 1 ? 2 + Noise() * 2 :
                    kind == 2 ? .5 + Noise() :
                    kind == 3 ? 1.5 : .3 + Noise() * .6,
                Size = kind == 4 ? .25 + Noise() * .4 :
                    kind == 1 ? .25 + Noise() * .4 :
                    kind == 2 ? .025 : .035 + Noise() * .06
            });
        }
    }

    private void BeforeContacts(double dt)
    {
        _propClock -= dt;
        if (_propClock <= 0)
        {
            _propClock = .3;
            StreamProps();
        }

        foreach (var c in Cars)
        {
            if (!c.Physical) continue;
            if (!c.Destroyed && c.RoadLevel==0 && c.Grounded && Math.Abs(c.Speed) > 4 && c.ImpactCooldown <= 0)
            {
                int i = BlockIndex(c.Position.X),
                    j = BlockIndex(c.Position.Z);
                for (int di = -1; di <= 1; di++)
                for (int dj = -1; dj <= 1; dj++)
                    foreach (var hole in CityRoadDetail.Holes(i + di, j + dj))
                        if ((c.Position - hole.Position).Length < hole.Size * .65)
                        {
                            c.VerticalVelocity = Math.Min(.8, Math.Abs(c.Speed) * .035);
                            c.PitchVelocity += .18;
                            c.ImpactCooldown = .5;
                        }
            }

            c.LastVx = c.Body.Vx;
            c.LastVz = c.Body.Vy;
            c.ImpactCooldown = Math.Max(0, c.ImpactCooldown - dt);
        }

        foreach (var p in _liveProps)
            if (p.Body is { } b)
            {
                if (_useScenery) PropSlope(p, dt);
                double speed = Math.Sqrt(b.Vx * b.Vx + b.Vy * b.Vy);
                if (p.Kind == StreetKind.Bag)
                {
                    double wind = 1.6 + Math.Sin(Time * .38 + p.Id) * .7;
                    b.Wake();
                    b.Vx += (wind - b.Vx) * dt * .8;
                    b.Vy += (Math.Sin(Time * .23 + p.Id * .7) - b.Vy) * dt * .6;
                    if (p.Height < .01 && Math.Sin(Time * .7 + p.Id) > .97)
                    {
                        p.Vy = 1.5;
                        p.Spin = 1.2;
                    }
                }

                foreach (var c in NearbyCars(p.Position, 8))
                {
                    double relative = Math.Sqrt(Math.Pow(c.Body.Vx - b.Vx, 2) + Math.Pow(c.Body.Vy - b.Vy, 2));
                    if ((!_useScenery||Math.Abs(c.SurfaceHeight-CityTerrain.Ground(p.Position)-p.Height)<2) && relative > 2 && p.DisplacedFor < .1 && CarPersonOverlap(c.Position, c.Heading, p.Position,
                            p.Radius + .05, c.HalfLength, c.HalfWidth))
                    {
                        p.DisplacedFor = .5;
                        p.Vy = Math.Min(4, relative * .19);
                        p.Spin = Math.CopySign(Math.Min(7, relative * .4), c.Body.Vx + .01);
                        Emit(b.X, .55, b.Y, p.Kind == StreetKind.Hydrant ? 3 : 0, 10, 3);
                        if (p.Kind == StreetKind.Hydrant) p.Water = 16;
                    }
                }

                p.DisplacedFor = Math.Max(0, p.DisplacedFor - dt);
                if (p.Height > 0 || p.Vy > 0)
                {
                    p.Vy -= 9.81 * dt;
                    p.Height += p.Vy * dt;
                    if (p.Height < 0)
                    {
                        p.Height = 0;
                        p.Vy = -p.Vy * .28;
                        if (p.Vy < .15) p.Vy = 0;
                    }
                }

                p.Tilt += p.Spin * dt;
                p.Spin *= Math.Exp(-dt * (p.Height > .02 ? .2 : 3));
                double drag = Math.Exp(-dt * (p.Height > .02 ? .1 : 2.2));
                b.Vx *= drag;
                b.Vy *= drag;
                b.W *= drag;
                if (speed < .01 && p.Kind != StreetKind.Bag && !p.Moved) b.Sleeping = true;
                if (p.Water > 0)
                {
                    p.Water -= dt;
                    // Water doubles as the "soaked" timer for any prop pushed into a
                    // canal (WaterStep); only a broken hydrant sprays at its pipe.
                    if (p.Kind == StreetKind.Hydrant && Noise() < dt * 65) Emit(p.Home.X, .7, p.Home.Z, 3, 1, 6);
                }
            }
    }

    private void AfterContacts(double dt)
    {
        foreach (CityCar c in Cars.Where(c => c.Physical && !c.Falling))
        {
            if (c.Destroyed) { WreckAfterContacts(c, dt); continue; }
            double dx = c.Body.Vx - c.LastVx, dz = c.Body.Vy - c.LastVz, hit = Math.Sqrt(dx * dx + dz * dz);

            if (hit > 1.5 && c.ImpactCooldown <= 0)
            {
                Sound("impact", c.Position, Math.Min(1, hit / 10));
                c.ImpactCooldown = .4;
                if (PlayerControls(c) && hit > 2.5) ReportCrime(Math.Min(.3, hit * .018));
                double previousDamage = c.Damage;
                DamageCar(c, hit * .025,
                    !PlayerControls(c) && NearbyCars(c.Position, 10).Any(v =>
                        PlayerControls(v) && (v.Position - c.Position).Length < c.HalfLength + v.HalfLength + 1));

                // A one-off stylised vehicle fire burst, not every ordinary bump.
                if (previousDamage < .95 && c.Damage >= .95 && hit > 6)
                {
                    Emit(c.Body.X, 1.1, c.Body.Y, 4, 36, 5,c.RoadLevel);
                    Emit(c.Body.X, 1, c.Body.Y, 1, 24, 2,c.RoadLevel);
                    Emit(c.Body.X, .8, c.Body.Y, 2, 24, 6,c.RoadLevel);
                }

                double side = dx * Math.Cos(c.Heading) - dz * Math.Sin(c.Heading);
                c.RollVelocity += Math.Clamp(side * .20, -4, 4);
                c.PitchVelocity += Math.Clamp((dx * Math.Sin(c.Heading) + dz * Math.Cos(c.Heading)) * .11, -2, 2);
                c.VerticalVelocity = Math.Max(c.VerticalVelocity, hit > 6 ? Math.Min(2.8, (hit - 6) * .14) : 0);
                Emit(c.Body.X, .65, c.Body.Y, 0, 18, 4,c.RoadLevel);
                Emit(c.Body.X, .6, c.Body.Y, 2, 8, 3,c.RoadLevel);
                Marks.Add(new() { Point = new(c.Body.X, c.Body.Y), CarId = c.Id, Height = .7, Life = 100 });

                // Contact patch on the nearest facade, in its actual plane.
                foreach (CityModel.Obstacle b in CityModel.Blocks)
                {
                    double px = Math.Clamp(c.Body.X, b.X - b.HalfX, b.X + b.HalfX),
                        pz = Math.Clamp(c.Body.Y, b.Z - b.HalfZ, b.Z + b.HalfZ);
                    if (Math.Sqrt(Math.Pow(px - c.Body.X, 2) + Math.Pow(pz - c.Body.Y, 2)) > c.HalfLength + 1) continue;
                    bool along = Math.Abs(c.Body.X - b.X) / b.HalfX > Math.Abs(c.Body.Y - b.Z) / b.HalfZ;

                    Marks.Add(new CityMark
                    {
                        Point = along
                            ? new CityPoint(b.X + Math.Sign(c.Body.X - b.X) * (b.HalfX + .022), pz)
                            : new CityPoint(px, b.Z + Math.Sign(c.Body.Y - b.Z) * (b.HalfZ + .022)),
                        Height = .8, Heading = along ? Math.PI / 2 : 0, Life = 90
                    });

                    break;
                }
            }

            // Reduced suspension/roll model, driven by resolved planar impulses.
            // Ground clearance follows the oriented body support; not a full 3D chassis solver.
            if(c.Kind==12&&!c.Destroyed&&!c.Falling){if(c.Recovering)RecoverOverturned(c,dt);continue;}
            c.VerticalVelocity -= 9.81 * dt;
            c.Lift += c.VerticalVelocity * dt;
            if (c.Lift < 0)
            {
                c.Lift = 0;
                c.VerticalVelocity = -c.VerticalVelocity * .22;
                if (c.VerticalVelocity < .15) c.VerticalVelocity = 0;
            }

            double restoring = c.Lift > .04 ? 0 : 12 * Math.Sin(2 * c.Roll);
            c.RollVelocity -= restoring * dt;
            c.RollVelocity *= Math.Exp(-dt * (c.Lift > .04 ? .15 : 2.1));
            c.PitchVelocity -= Math.Sin(c.Pitch) * 18 * dt;
            c.PitchVelocity *= Math.Exp(-dt * 2.2);
            c.Roll += c.RollVelocity * dt;
            c.Pitch += c.PitchVelocity * dt;
            if (PlayerControls(c) && !c.Burning) RecoverOverturned(c, dt);
            double lateral = c.Body.Vx * Math.Cos(c.Heading) - c.Body.Vy * Math.Sin(c.Heading);
            c.SkidClock += dt;
            c.WetTires = Math.Max(0, c.WetTires - dt);

            if (Puddles.Any(p => (p - c.Position).Length < 1.6)) c.WetTires = 8;

            if (IsBeach(c.Position) && c is { Grounded: true, SkidClock: > .09 } && Math.Abs(c.Speed) > .7)
            {
                var f = new CityPoint(Math.Sin(c.Heading), Math.Cos(c.Heading));
                var r = Right(f);
                for (int side = -1; side <= 1; side += 2)
                {
                    var a = c.Position + r * (side * c.HalfWidth * .9) - f * (c.Wheelbase * .5);
                    Marks.Add(new()
                    {
                        RoadLevel=c.RoadLevel, Point = a, End = a + new CityPoint(c.Body.Vx, c.Body.Vy) * .13, Skid = true, Sand = true,
                        Life = 150
                    });
                }

                c.SkidClock = 0;
                if (Noise() < .14) Emit(c.Body.X, .15, c.Body.Y, 5, 2, 1.2,c.RoadLevel);
            }

            if (c is { WetTires: > 0, SkidClock: > .08 } && Math.Abs(c.Speed) > 1)
            {
                var f = new CityPoint(Math.Sin(c.Heading), Math.Cos(c.Heading));
                CityPoint r = Right(f);

                for (int side = -1; side <= 1; side += 2)
                {
                    CityPoint a = c.Position + r * (side * c.HalfWidth * .9) - f * (c.Wheelbase * .5);

                    Marks.Add(new CityMark
                    {
                        RoadLevel=c.RoadLevel, Point = a, End = a + new CityPoint(c.Body.Vx, c.Body.Vy) * .10, Skid = true, Wet = true,
                        Life = 18
                    });
                }

                c.SkidClock = 0;
            }

            if (c is { Grounded: true, SkidClock: > .08 } && (Math.Abs(lateral) > 2.2 || (c.Braking && Math.Abs(c.Speed) > 5)))
            {
                if (PlayerControls(c) && Noise() < .04)
                {
                    Sound("brake", c.Position, .4);
                }

                c.SkidClock = 0;
                var f = new CityPoint(Math.Sin(c.Heading), Math.Cos(c.Heading));
                var r = new CityPoint(f.Z, -f.X);

                for (int side = -1; side <= 1; side += 2)
                {
                    CityPoint a = c.Position + r * (side * c.HalfWidth * .9) - f * (c.Wheelbase * .5);

                    Marks.Add(new CityMark
                    {
                        RoadLevel=c.RoadLevel, Point = a, 
                        End = a + new CityPoint(c.Body.Vx, c.Body.Vy) * .09,
                        Skid = true
                    });
                }

                if (Math.Abs(lateral) > 4) Emit(c.Body.X, .2, c.Body.Y, 1, 1, .5,c.RoadLevel);
            }

            if (c.Damage > .4 && Noise() < dt * (c.Damage * 6))
            {
                Emit(c.Body.X + Math.Sin(c.Heading), .8, c.Body.Y + Math.Cos(c.Heading), 1, 1, .6,c.RoadLevel);
            }
        }

        for (int i = Particles.Count - 1; i >= 0; i--)
        {
            CityParticle p = Particles[i];
            p.Age += dt;

            if (p.Age >= p.Life)
            {
                Particles.RemoveAt(i);
                continue;
            }

            p.X += p.Vx * dt;
            p.Z += p.Vz * dt;
            p.Y += p.Vy * dt;

            if(p.Kind==6){p.Vy+=1.4*dt;p.Size+=dt*.55;}
            else if (p.Kind == 1)
            {
                p.Vy += .5 * dt;
                p.Size += dt * .22;
                p.Vx += .4 * dt;
            }
            else
            {
                p.Vy -= 9.81 * dt;
            }

            double floor = .03 + (_useScenery ? CityRoadLevels.Surface(p.X, p.Z,p.RoadLevel) : 0);

            if (p.Y < floor)
            {
                p.Y = floor;
                p.Vy = -p.Vy * .22;
                p.Vx *= .8;
                p.Vz *= .8;
            }
        }

        for (int i = Marks.Count - 1; i >= 0; i--)
        {
            if ((Marks[i].Age += dt) > Marks[i].Life)
            {
                Marks.RemoveAt(i);
            }
        }

        if (Marks.Count > 1000)
        {
            Marks.RemoveRange(0, Marks.Count - 1000);
        }
    }

    public static bool IsBeach(CityPoint p) => Math.Max(Math.Abs(p.X), Math.Abs(p.Z)) > Coordinate(Nodes - 1) + 1;

    public void RecoverPlayer()
    {
        CityCar car = Hero;
        if (OnFoot || car.Destroyed || car.Falling || car.Recovering || Math.Abs(car.Speed) > 1.5) return;
        if(_useScenery&&!RecoverToRoad(car))return;
        HelicopterLanding = false;
        car.RecoveryTime = .001;
        car.RecoveryRoll = Math.IEEERemainder(car.Roll, Math.Tau);
        car.RecoveryPitch = Math.IEEERemainder(car.Pitch, Math.Tau);
    }
}
