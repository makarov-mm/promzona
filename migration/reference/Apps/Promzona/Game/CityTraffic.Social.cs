namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public List<CityGroup> Groups { get; } = [];
    public List<CityIncident> Incidents { get; } = [];
    public Queue<CitySound> Sounds { get; } = new();
    private double _socialClock;
    private int _incidentId;
    private readonly Dictionary<int, CityPedestrian> _socialPeople = [];

    private void ResetSocial()
    {
        _socialPeople.Clear();
        Groups.Clear();
        Incidents.Clear();
        Sounds.Clear();
        Shots.Clear();
        ShotsFired = 0;
        _incidentId = 0;
        _socialClock = 0;
        Respawns = 0;
        ResetServices();

        for (int k = 0; k + 3 < Pedestrians.Count; k += 28)
        {
            CityPedestrian lead = Pedestrians[k];
            var g = new CityGroup { Id = Groups.Count };
            Groups.Add(g);

            for (int n = 0; n < 3; n++)
            {
                CityPedestrian p = Pedestrians[k + n];
                ReleasePerson(p);
                p.GroupId = g.Id;
                p.Persona = 3;
                p.Cruise = 1.35;
                g.Members.Add(p.Id);

                if (n > 0)
                {
                    p.Position = lead.Position + new CityPoint(n * .85, 0);
                    p.Body.X = p.Position.X;
                    p.Body.Y = p.Position.Z;
                }
            }

            g.Leader = lead.Id;
            g.Trail.Add(lead.Position);
            Reanchor(lead);
        }

        foreach (var p in Pedestrians)
            if (p.Persona == 2)
                p.Cruise = .85;
    }

    private void Sound(string name, CityPoint at, double gain = 1, int variant = 0, int speakerId = -1,
        int revision = 0, bool ui = false)
    {
        if ((at - PlayerPosition).Length > 85) return;
        while (Sounds.Count >= 64) Sounds.Dequeue();
        Sounds.Enqueue(new(name, at, gain, variant, speakerId, revision, ui));
    }

    private void AddIncident(CityPoint at, bool violent = true, bool playerCaused = false)
    {
        Incidents.Add(new(++_incidentId, at, Time));
        if (Incidents.Count > 48) Incidents.RemoveAt(0);
        if (!violent) return;
        foreach (var p in Pedestrians)
            if (!p.IsPlayer && !p.Dead && p.KnockedFor <= 0 && (p.Position - at).Length < 24 &&
                ClearSight(p.Position, at))
            {
                double old = p.Fear;
                p.Fear = Math.Max(p.Fear, p.Persona == 3 ? .55 : 1);
                p.FearFrom = at;
                if (old <= .25 && p.CanSpeak)
                {
                    Sound(p.Persona == 3 ? "tough_fear" : "fear", p.Position, .8, p.Id, p.Id, p.VoiceRevision);
                    p.VoiceAt = Time + 5;
                }

                if (playerCaused && p.Armed) p.AggressionUntil = Time + (p.Persona == 3 ? 8 : 5);
            }

        foreach (var a in Animals)
            if (!a.Dead && (a.Position - at).Length < 20)
            {
                a.Fear = 1;
                a.FearFrom = at;
            }
    }

    public static bool OnCarriageway(CityPoint p)
    {
        int i = (int)Math.Round((p.X - Coordinate(0)) / Spacing), j = (int)Math.Round((p.Z - Coordinate(0)) / Spacing);
        if (i < 0 || j < 0 || i >= Nodes || j >= Nodes) return false;
        double x = p.X - Coordinate(i), z = p.Z - Coordinate(j);
        if (Math.Abs(x) <= RoadHalf && Math.Abs(z) <= RoadHalf) return true;
        if (Math.Abs(x) <= RoadHalf && HasRoad(i, j, z >= 0 ? 0 : 2)) return true;
        return Math.Abs(z) <= RoadHalf && HasRoad(i, j, x >= 0 ? 1 : 3);
    }

    private bool Threat(CityCar c, CityPoint at, out CityPoint source)
    {
        source = c.Position;
        if (c.Destroyed || c.Falling || OnCarriageway(c.Position)) return false;
        var v = new CityPoint(c.Body.Vx, c.Body.Vy);
        double speed = v.Length;
        if (speed < 3.5) return false;
        double t = Math.Clamp(CityPoint.Dot(at - c.Position, v) / (speed * speed), 0, 1.4);
        var nearest = c.Position + v * t;
        source = nearest;
        return (nearest - at).Length < c.HalfWidth + 2.2 && (c.Position - at).Length < speed * 1.4 + c.HalfLength + 3;
    }

    private static bool ClearWalk(CityPoint p)
    {
        if(CityTransit.PoleNear(p,.55))return false;
        if (CityRoadLevels.OpenCut(p.X,p.Z)||CityAmenities.BlocksWalk(p)) return false;
        if (CityWater.Depth(p) > .65) return false;
        if (Math.Abs(p.X) >= LandEdge - 3 || Math.Abs(p.Z) >= LandEdge - 3) return false;
        int i = (int)Math.Floor((p.X - Coordinate(0)) / Spacing), j = (int)Math.Floor((p.Z - Coordinate(0)) / Spacing);
        foreach (int id in CityLayout.Current.BuildingIds(i, j))
        {
            var b = CityModel.Blocks[id];
            if (Math.Abs(p.X - b.X) < b.HalfX + .4 && Math.Abs(p.Z - b.Z) < b.HalfZ + .4) return false;
        }

        return true;
    }

    private CityPoint Escape(CityPoint at, CityPoint from)
    {
        var d = at - from;
        if (d.Length < .1) d = new(1, 0);
        d *= 1 / d.Length;
        foreach (double a in new[] { 0d, .7, -.7, 1.5, -1.5, Math.PI })
        {
            var v = new CityPoint(d.X * Math.Cos(a) - d.Z * Math.Sin(a), d.X * Math.Sin(a) + d.Z * Math.Cos(a));
            if (ClearWalk(at + v * 2)) return v;
        }

        return default;
    }

    private void Reanchor(CityPedestrian p)
    {
        ReleasePerson(p);
        p.I = Math.Clamp((int)Math.Round((p.Position.X - Coordinate(0)) / Spacing), 0, Nodes - 1);
        p.J = Math.Clamp((int)Math.Round((p.Position.Z - Coordinate(0)) / Spacing), 0, Nodes - 1);
        p.Sx = p.Position.X >= Coordinate(p.I) ? 1 : -1;
        p.Sz = p.Position.Z >= Coordinate(p.J) ? 1 : -1;
        var home = Corner(p.I, p.J, p.Sx, p.Sz);
        p.Path = new(p.Position, home);
        p.NextI = p.I;
        p.NextJ = p.J;
        p.NextSx = p.Sx;
        p.NextSz = p.Sz;
        p.Wait = 0;
    }

    private void SocialStep(double dt)
    {
        _socialClock += dt;
        if (_socialClock < .12) return;
        dt = _socialClock;
        _socialClock = 0;
        Incidents.RemoveAll(e => Time - e.Created > 80);
        _socialPeople.Clear();
        foreach (var person in Pedestrians)
            if (!person.Dead)
                _socialPeople[person.Id] = person;
        foreach (var g in Groups)
        {
            g.Members.RemoveAll(id => !_socialPeople.ContainsKey(id));
            if (g.Members.Count == 0) continue;
            var lead = _socialPeople[g.Members[0]];
            if (g.Leader != lead.Id)
            {
                g.Leader = lead.Id;
                g.Trail.Clear();
                Reanchor(lead);
            }

            if (g.Trail.Count == 0 || (g.Trail[^1] - lead.Position).Length > .35) g.Trail.Add(lead.Position);
            if (g.Trail.Count > 48) g.Trail.RemoveAt(0);
        }

        foreach (var p in Pedestrians)
        {
            if (p.IsPlayer || !p.Physical || p.Dead || p.KnockedFor > 0) continue;
            double old = p.Fear;
            p.Fear = Math.Max(0, p.Fear - dt * (p.Persona == 3 ? .55 : .12));
            foreach (var c in NearbyCars(p.Position, 30))
                if (Threat(c, p.Position, out var source))
                {
                    p.Fear = Math.Min(1, p.Fear + dt * (p.Persona == 3 ? 2 : 5));
                    p.FearFrom = source;
                }

            if (old > .08 && p.Fear <= .08) Reanchor(p);
            if (p.Fear > .25 && (old <= .25 || Time > p.VoiceAt))
            {
                Sound(p.Persona == 3 ? "tough_fear" : "fear", p.Position, .8, p.Id, p.Id, p.VoiceRevision);
                p.VoiceAt = Time + 4 + Random() * 5;
            }

            if (p.Fear < .08 && Time > p.VoiceAt)
            {
                if (p.Persona == 2) Sound("elder", p.Position, .5, p.Id, p.Id, p.VoiceRevision);
                else if (p.Persona == 3) Sound("tough", p.Position, .5, p.Id, p.Id, p.VoiceRevision);
                p.VoiceAt = Time + 30 + Random() * 60;
            }

            if (p.Fear < .15 && p.InterestUntil < Time)
            {
                foreach (var e in Incidents)
                {
                    if (e.Id <= p.InterestId || (e.Position - p.Position).Length > 26 || Time - e.Created > 12)
                        continue;
                    bool visible = true;
                    for (int sample = 1; sample < 8; sample++)
                        if (!ClearWalk(p.Position + (e.Position - p.Position) * (sample / 8d)))
                        {
                            visible = false;
                            break;
                        }

                    if (!visible) continue;
                    p.InterestId = e.Id;
                    p.InterestUntil = Time + 10 + Random() * 18;
                    break;
                }
            }
        }
    }

    private bool SocialVelocity(CityPedestrian p, out CityPoint v)
    {
        v = default;
        if (p.Aiming) return true;
        if (p.Fear > .08)
        {
            v = Escape(p.Position, p.FearFrom) * (p.Persona == 2 ? 2 : 2.5 + 2 * p.Fear);
            return true;
        }

        if (p.InterestUntil > Time)
        {
            var e = Incidents.Find(e => e.Id == p.InterestId);
            if (e is not null)
            {
                var d = e.Position - p.Position;
                double distance = d.Length;
                if (distance > 4 + (p.Id % 5) * .55) v = d * (Math.Min(p.Cruise, distance - 4) / distance);
                return true;
            }
        }

        if (p.GroupId >= 0 && p.GroupId < Groups.Count)
        {
            CityGroup g = Groups[p.GroupId];

            if (g.Members.Count > 0 && g.Members[0] != p.Id)
            {
                _socialPeople.TryGetValue(g.Members[0], out CityPedestrian? lead);
                if (lead?.Dead == true) lead = null;

                if (lead is not null)
                {
                    int rank = g.Members.IndexOf(p.Id);

                    if (rank < 0)
                    {
                        p.GroupId = -1;
                        return false;
                    }

                    var f = new CityPoint(Math.Sin(lead.Heading), Math.Cos(lead.Heading));
                    List<CityPoint> trail = g.Trail;
                    CityPoint target = trail.Count > 0
                        ? trail[Math.Max(0, trail.Count - 1 - rank * 4)]
                        : lead.Position - f * (1.4 + rank * .7);
                    target += Right(f) * ((rank % 2 == 0 ? 1 : -1) * .50);
                    CityPoint d = target - p.Position;
                    v = d * (Math.Min(2.2, d.Length * 1.6) / Math.Max(.01, d.Length));
                    return true;
                }
            }
        }

        return false;
    }

    private void MovePerson(CityPedestrian p, CityPoint v, double dt, CityPoint? destination = null)
    {
        p.Body.Wake();
        if (destination is { } target && p.Physical && p.RoadLevel==0) v = NavigateWalker(p, v, target, dt);

        if (v.Length > .15)
        {
            foreach (var car in NearbyCars(p.Position, 8))
            {
                if (Math.Abs(car.Speed) < 1 && CarPersonOverlap(car.Position, car.Heading, p.Position + v * .6, .55, car.HalfLength, car.HalfWidth))
                {
                    var left = new CityPoint(-v.Z, v.X);
                    CityPoint a = p.Position + left * .6;
                    CityPoint b = p.Position - left * .6;

                    if (ClearWalk(a) && !CarPersonOverlap(car.Position, car.Heading, a, .45, car.HalfLength, car.HalfWidth))
                    {
                        v = left;
                    }
                    else if (ClearWalk(b) && !CarPersonOverlap(car.Position, car.Heading, b, .45, car.HalfLength, car.HalfWidth))
                    {
                        v = left * -1;
                    }
                    else
                    {
                        CityPoint away = p.Position - car.Position;
                        v = away * (p.Cruise / Math.Max(.1, away.Length));
                    }
                }
            }

            CityPoint f = v * (1 / Math.Max(.1, v.Length));
            CityPoint right = Right(f);

            foreach (CityPedestrian other in NearbyPeople(p.Position, 1.7))
            {
                if (other == p || other.Dead || other.Detached) continue;
                CityPoint d = other.Position - p.Position;
                double ahead = CityPoint.Dot(d, f), side = CityPoint.Dot(d, right);
                if (ahead is > .05 and < 1.7 && Math.Abs(side) < .7) v += right * (.65 * (1 - ahead / 2));
            }
        }

        foreach (CityPedestrian other in NearbyPeople(p.Position, 1.1))
        {
            if (other == p || other.Dead || other.Detached) continue;
            CityPoint away = p.Position - other.Position;
            double d = away.Length;

            if (d is > .01 and < 1.1)
            {
                v += away * ((1.1 - d) * 2 / d);
            }
        }

        if (!CityRoadLevels.ClearAt(p.Position+v*.35,p.RoadLevel)&&!ClearWalk(p.Position + v * .35))
        {
            var left = new CityPoint(-v.Z, v.X);
            v = ClearWalk(p.Position + left * .35) ? left : ClearWalk(p.Position - left * .35) ? left * -1 : default;
        }

        double mix = 1 - Math.Exp(-dt * 7);
        p.Body.Vx += (v.X - p.Body.Vx) * mix;
        p.Body.Vy += (v.Z - p.Body.Vy) * mix;
    }

    private void StartBirdFlight(CityAnimal a)
    {
        a.Flying = true;
        a.FlightTime = 0;
        double angle = Random() * Math.Tau;
        var target = a.Position + new CityPoint(Math.Sin(angle), Math.Cos(angle)) * (80 + Random() * 130);
        int i = Math.Clamp((int)Math.Round((target.X - Coordinate(0)) / Spacing), 0, Nodes - 2),
            j = Math.Clamp((int)Math.Round((target.Z - Coordinate(0)) / Spacing), 0, Nodes - 2);
        a.Landing = Corner(i, j, 1, 1);
        Sound("wings", a.Position, .35);
    }

    private void FlyBird(CityAnimal a, double dt)
    {
        a.FlightTime += dt;
        var delta = a.Landing - a.Position;
        double distance = delta.Length;
        a.Heading = Math.Atan2(delta.X, delta.Z);
        double targetHeight = Math.Min(10, Math.Min(a.FlightTime * 4, distance * .3));
        a.Height += (targetHeight - a.Height) * Math.Min(1, dt * 4);

        if (distance > .4) a.Position += delta * (Math.Min(distance, dt * 9) / distance);

        if (a is { Height: > .8, Body: { } b })
        {
            Remove(b);
            a.Body = null;
        }
        else if (a.Body is { } low)
        {
            low.X = a.Position.X;
            low.Y = a.Position.Z;
        }

        if (distance < .5 && a.Height < .25 && !NearbyCars(a.Position, 8).Any(c => CarPersonOverlap(c.Position, c.Heading, a.Position, .8, c.HalfLength, c.HalfWidth)))
        {
            a.Height = 0;
            a.Flying = false;
            a.FlightTime = 0;
            a.FlyAt = Time + 20 + Random() * 60;
            a.Target = a.Position;
            a.Wait = 0;

            if (a.Body is null)
            {
                int at = World.AddCircle(a.Position.X, a.Position.Z, a.Radius, a.Mass / (Math.PI * a.Radius * a.Radius), .13, .04, 0);
                a.Body = World.Bodies[at];
                a.Body.InvI = 0;
                if (!a.Physical) Remove(a.Body);
            }
        }
    }
}
