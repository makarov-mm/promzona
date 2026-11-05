using MakarovPhysics.RigidBodies2D;
namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public const double PhysicsRadius = 90, PhysicsExitRadius = 112;
    private double _detailClock;
    public int PhysicalCars => Cars.Count(c => c.Physical);

    private bool Detailed(CityPoint at, bool was) =>
        !_useScenery || (at - PlayerPosition).Length < (was ? PhysicsExitRadius : PhysicsRadius);

    private void Attach(Body2D b, bool active)
    {
        if (active)
        {
            if (!World.Bodies.Contains(b)) World.Bodies.Add(b);
        }
        else Remove(b);
    }

    // The bubble refresh detaches many bodies at once when the player drives on;
    // they are collected and removed in one compaction (see RemoveBodies).
    private readonly List<Body2D> _detachBatch = [];

    private void Detach(Body2D b, bool active)
    {
        if (active) Attach(b, true);
        else _detachBatch.Add(b);
    }

    private void RefreshDetail()
    {
        _detachBatch.Clear();
        foreach (var c in Cars)
        {
            bool active = PlayerControls(c) || (!c.AirPatrol || c.Destroyed || c.Driverless) && Detailed(c.Position, c.Physical);
            UpdateTerrainSupport(c);
            if (active != c.Physical)
            {
                c.Physical = active;
                Detach(c.Body, active && !c.Falling);
            }
        }

        foreach (var p in Pedestrians)
        {
            bool active = p.IsPlayer || Detailed(p.Position, p.Physical);
            if (active != p.Physical)
            {
                p.FarRoutine = default;
                p.Physical = active;
                Detach(p.Body, active && !p.Detached);
            }
        }

        foreach (var a in Animals)
        {
            bool active = Detailed(a.Position, a.Physical);
            if (active != a.Physical)
            {
                a.Physical = active;
                if (a.Body is { } b) Detach(b, active && !a.Detached);
            }
        }

        if (_detachBatch.Count > 0) { World.RemoveBodies(_detachBatch); _detachBatch.Clear(); }
    }

    // Offscreen actors retain routes, signals, clocks and population identity.
    // They do not enter the impulse solver. Poses move continuously at frame rate;
    // expensive steering/contact work is reserved for the hysteretic local bubble.
    private void RuleStep(double dt)
    {
        long stamp=MeasureStep?System.Diagnostics.Stopwatch.GetTimestamp():0;
        foreach (var c in Cars)
        {
            if (c.Destroyed && !c.Physical && !c.Falling) { FarWreckStep(c, dt); continue; }
            if (c.AirPatrol || c.TransitRoute>0 || c.Physical || c.Destroyed || c.Falling) continue;
            if (c.Driverless)
            {
                c.Body.Vx = c.Body.Vy = c.Body.W = 0;
                continue;
            }

            if (c.Phase!=3&&PoliceRuleStep(c, dt))
            {
                UpdateTerrainSupport(c);
                continue;
            }

            if (c.Reservation >= 0 && c.Phase != 1)
            {
                _reservations[c.Reservation] = -1;
                c.Reservation = -1;
            }

            ConsiderLaneChange(c,dt);
            if (BusStop(c)||ServiceParked(c))
            {
                c.Body.Vx = c.Body.Vy = 0;
                continue;
            }

            double speed = c.Phase == 1 ? Math.Min(3.5, c.Cruise) : c.Cruise;
            if (c.Phase == 0 && c.Path.Length - c.Along < 1 && !RuleCanEnter(c)) speed = 0;
            var forward = new CityPoint(Math.Sin(c.Heading), Math.Cos(c.Heading));
            foreach (var other in NearbyCars(c.Position, 16))
            {
                if (other == c) continue;
                var gap = other.Position - c.Position;
                double ahead = CityPoint.Dot(gap, forward);
                if (ahead > 0 && Math.Abs(CityPoint.Dot(gap, Right(forward))) < c.HalfWidth + other.HalfWidth + .1 &&
                    Math.Cos(c.Heading - other.Heading) > .7 && SameHeight(c,other))
                    speed = Math.Min(speed,
                        Math.Max(0, (ahead - c.HalfLength - other.HalfLength - .8) / Math.Max(dt, .05)));
            }

            c.Along = Math.Min(c.Path.Length, c.Along + speed * dt);
            var pose = c.Path.Sample(c.Along);
            c.Body.X = pose.Point.X;
            c.Body.Y = pose.Point.Z;
            c.Body.Angle = -pose.Heading;
            c.Body.Vx = Math.Sin(pose.Heading) * speed;
            c.Body.Vy = Math.Cos(pose.Heading) * speed;
            c.Body.W = 0;
            UpdateTerrainSupport(c);
            if (c.Along >= c.Path.Length - .001)
            {
                if (c.Phase == 0 && speed > 0)
                {
                    Cross(c);
                }
                else if(c.Phase==3){c.Position=pose.Point;FinishLevelRoute(c);}
                else if (c.Phase == 1)
                {
                    c.Position = pose.Point;
                    Continue(c);
                }
            }
        }

        if(MeasureStep){long now=System.Diagnostics.Stopwatch.GetTimestamp();LastStepMilliseconds[9]+=(now-stamp)*1000d/System.Diagnostics.Stopwatch.Frequency;stamp=now;}
        foreach (var p in Pedestrians)
        {
            if (p.Physical) continue;
            p.Fear = Math.Max(0, p.Fear - dt * .2);
            p.Aiming = false;
            if (p.Dead || p.Detached || p.KnockedFor > 0)
            {
                WalkBody(p, dt);
                continue;
            }

            if (ScooterWalk(p,dt) || TransitWaitWalk(p,dt) || ServiceWalk(p, dt) || LevelWalk(p,dt) || JobWalk(p,dt) || FarRoutine(p,dt))
            {
                p.Body.X += p.Body.Vx * dt;
                p.Body.Y += p.Body.Vy * dt;
                continue;
            }

            if (p.Wait > 0)
            {
                p.Wait -= dt;
                p.Body.Vx = p.Body.Vy = 0;
                continue;
            }

            if (p.Path is null) ChooseWalk(p);
            if (p.Path is not { } path) continue;
            p.Along = Math.Min(path.Length, p.Along + p.Cruise * dt);
            var pose = path.Sample(p.Along);
            p.Body.X = pose.Point.X;
            p.Body.Y = pose.Point.Z;
            p.Body.Vx = Math.Sin(pose.Heading) * p.Cruise;
            p.Body.Vy = Math.Cos(pose.Heading) * p.Cruise;
            if (p.OriginKey >= 0 && p.Along > .9)
            {
                _pedPlaces[p.OriginKey] = -1;
                p.OriginKey = -1;
            }

            if (p.Along >= path.Length - .001)
            {
                p.I = p.NextI;
                p.J = p.NextJ;
                p.Sx = p.NextSx;
                p.Sz = p.NextSz;
                p.Path = null;
                p.Crossing = false;
                p.OriginKey = p.DestinationKey;
                p.DestinationKey = -1;
                p.Wait = .2;
            }
        }
        if(MeasureStep)LastStepMilliseconds[10]+=(System.Diagnostics.Stopwatch.GetTimestamp()-stamp)*1000d/System.Diagnostics.Stopwatch.Frequency;
    }

    private bool MoveFarSocial(CityPedestrian p, CityPoint velocity, double dt)
    {
        // Distant groups follow their shared trail without local actor/contact queries.
        // Keep the building guard, so a corner shortcut cannot cross a facade.
        if (!CityRoadLevels.ClearAt(p.Position+velocity*.35,p.RoadLevel)&&!ClearWalk(p.Position + velocity * .35))
        {
            var side = Right(velocity);
            velocity = ClearWalk(p.Position + side * .35) ? side :
                ClearWalk(p.Position - side * .35) ? side * -1 : default;
        }

        double mix = 1 - Math.Exp(-dt * 7);
        p.Body.Vx += (velocity.X - p.Body.Vx) * mix;
        p.Body.Vy += (velocity.Z - p.Body.Vy) * mix;
        return true;
    }

    private bool RuleCanEnter(CityCar c)
    {
        int key = Key(c.I, c.J);
        if ((!IsResponding(c) && !Green(c.I, c.J, c.Direction)) ||
            (_reservations[key] >= 0 && _reservations[key] != c.Id)) return false;
        // Traffic rules survive the physics LOD; a far car must not overwrite
        // another crossing reservation or enter an occupied outgoing lane.
        var path = CrossingPath(c);
        var exit = path.Sample(path.Length);
        return CarClearNear(exit.Point, exit.Heading, c);
    }

    private void FarAnimal(CityAnimal a, double dt)
    {
        a.Fear = Math.Max(0, a.Fear - dt * .2);
        if (a.Flying)
        {
            if (a.Kind == AnimalKind.Bird) FlyBird(a, dt);
            else
            {
                a.FlightTime += dt;
                a.Height += dt * 4;
                a.Position += new CityPoint(Math.Sin(a.Heading), Math.Cos(a.Heading)) * dt * 6;
                if (a.Body is { } fb)
                {
                    Remove(fb);
                    a.Body = null;
                }
            }

            return;
        }

        if (a.Body is not { } b) return;
        if (a.Dead || a.KnockedFor > 0)
        {
            a.KnockedFor = Math.Max(0, a.KnockedFor - dt);
            a.VerticalSpeed -= 9.81 * dt;
            a.Height = Math.Max(0, a.Height + a.VerticalSpeed * dt);
            if (a.Height == 0) a.VerticalSpeed = 0;
            double drag = Math.Exp(-dt * (a.Height > 0 ? .25 : 4));
            b.Vx *= drag;
            b.Vy *= drag;
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            return;
        }

        if (a.Kind == AnimalKind.Bird && Time >= a.FlyAt)
        {
            StartBirdFlight(a);
            return;
        }

        if (a.Kind == AnimalKind.Pigeon)
        {
            if (a.Fear > .18)
            {
                LaunchFlock(a);
                return;
            }

            var v = FlockVelocity(a);
            b.Vx = v.X;
            b.Vy = v.Z;
            b.X += v.X * dt;
            b.Y += v.Z * dt;
            return;
        }

        var d = a.Target - a.Position;
        if (d.Length < .5)
        {
            a.Wait -= dt;
            if (a.Wait <= 0)
            {
                a.Target = a.Position + new CityPoint(Math.Sin(a.Id + Time * .01), Math.Cos(a.Id + Time * .01)) * 6;
                a.Wait = 15;
            }

            return;
        }

        double speed = a.Kind is AnimalKind.Pigeon or AnimalKind.Bird ? .3 : a.Kind == AnimalKind.Cat ? .85 : 1.2;
        a.Heading = Math.Atan2(d.X, d.Z);
        b.X += d.X / d.Length * speed * dt;
        b.Y += d.Z / d.Length * speed * dt;
        b.Vx = d.X / d.Length * speed;
        b.Vy = d.Z / d.Length * speed;
    }
}
