using MakarovPhysics.RigidBodies2D;

namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private sealed class WalkDetour
    {
        public CityPoint Goal;
        public List<CityPoint> Points = [];
        public int Next;
        public double Expires;
    }

    // Rare local repairs around parked vehicles, lamps and street furniture.
    // Far citizens retain the cheap sidewalk graph, never this contact-aware search.
    private readonly Dictionary<int, WalkDetour> _walkDetours = [];
    private readonly Dictionary<int, double> _clearanceChecks = [];

    private void RecoverWalkerClearance(CityPedestrian p, double dt)
    {
        if (p.RoadLevel!=0 || !p.Physical || p.Dead || p.Detached || p.KnockedFor > 0) return;
        if (_clearanceChecks.TryGetValue(p.Id, out double next) && Time < next) return;
        _clearanceChecks[p.Id] = Time + .4;
        if (_clearanceChecks.Count > 1024)
            foreach (int id in _clearanceChecks.Where(e => e.Value < Time - 3).Select(e => e.Key).ToArray())
                _clearanceChecks.Remove(id);
        CityPoint at = p.Position;
        bool moved = false;
        for (int pass = 0; pass < 4; pass++)
        {
            int i = (int)Math.Floor((at.X - Coordinate(0)) / Spacing),
                j = (int)Math.Floor((at.Z - Coordinate(0)) / Spacing);
            CityPoint best = at;
            double distance = double.MaxValue;
            foreach (int id in CityLayout.Current.BuildingIds(i, j))
            {
                var b = CityModel.Blocks[id];
                double hx = b.HalfX + .46, hz = b.HalfZ + .46;
                if (Math.Abs(at.X - b.X) >= hx || Math.Abs(at.Z - b.Z) >= hz) continue;
                foreach (var candidate in new[]
                         {
                             new CityPoint(b.X - hx, at.Z), new CityPoint(b.X + hx, at.Z),
                             new CityPoint(at.X, b.Z - hz), new CityPoint(at.X, b.Z + hz)
                         })
                {
                    double d = (candidate - at).Length;
                    if (d < distance && ClearWalk(candidate))
                    {
                        best = candidate;
                        distance = d;
                    }
                }
            }

            if (distance == double.MaxValue) break;
            at = best;
            moved = true;
        }

        // CarPersonOverlap includes a .25 m disc; .05 adds up to the actual .30 m body.
        // Recover a penetrated standing actor to the nearest free side, not by
        // adding a large solver velocity that can throw them back into a wall.
        foreach (var car in NearbyCars(at, 9))
        {
            if (!SameHeight(car,p) || Math.Abs(car.Speed) > .6 ||
                !CarPersonOverlap(car.Position, car.Heading, at, .05, car.HalfLength, car.HalfWidth)) continue;
            var f = new CityPoint(Math.Sin(car.Heading), Math.Cos(car.Heading));
            var r = Right(f);
            var d = at - car.Position;
            double x = CityPoint.Dot(d, r),
                z = CityPoint.Dot(d, f),
                hx = car.HalfWidth + .42,
                hz = car.HalfLength + .42;
            var candidates = new[]
            {
                car.Position + r * hx + f * z, car.Position - r * hx + f * z, car.Position + r * x + f * hz,
                car.Position + r * x - f * hz
            };
            foreach (var candidate in candidates.OrderBy(v => (v - at).Length))
            {
                if (!ClearWalk(candidate) || NearbyCars(candidate, 9).Any(c => SameHeight(c,p) &&
                        CarPersonOverlap(c.Position, c.Heading, candidate, .10, c.HalfLength, c.HalfWidth))) continue;
                at = candidate;
                moved = true;
                break;
            }
        }

        if (moved)
        {
            p.Position = at;
            p.Body.X = at.X;
            p.Body.Y = at.Z;
            p.Body.Vx = p.Body.Vy = 0;
            _walkDetours.Remove(p.Id);
            p.NavigationStall = 2;
            p.NextNavigation = 0;
            if (p.ServiceId == 0) Reanchor(p);
        }
    }

    private CityPoint NavigateWalker(CityPedestrian p, CityPoint desired, CityPoint target, double dt)
    {
        p.NavigationClock += dt;
        if (p.NavigationClock >= 1)
        {
            if (desired.Length > .2 && (p.Position - p.NavigationLast).Length < .35)
                p.NavigationStall += p.NavigationClock;
            else p.NavigationStall = 0;
            p.NavigationLast = p.Position;
            p.NavigationClock = 0;
        }

        if (_walkDetours.TryGetValue(p.Id, out var detour))
        {
            if (Time > detour.Expires || (target - detour.Goal).Length > 3)
            {
                _walkDetours.Remove(p.Id);
                detour = null;
            }
            else
            {
                while (detour.Next < detour.Points.Count && (detour.Points[detour.Next] - p.Position).Length < .42)
                    detour.Next++;
                if (detour.Next >= detour.Points.Count)
                {
                    _walkDetours.Remove(p.Id);
                    detour = null;
                }
            }
        }

        if (detour is null && p.NavigationStall > 1.5 && Time >= p.NextNavigation)
        {
            p.NextNavigation = Time + 3;
            p.NavigationStall = 0;
            foreach (int id in _walkDetours.Where(kv => kv.Value.Expires < Time).Select(kv => kv.Key).ToArray())
                _walkDetours.Remove(id);
            detour = FindWalkDetour(p.Position, target);
            if (detour is not null) _walkDetours[p.Id] = detour;
        }

        if (detour is not null)
        {
            var d = detour.Points[detour.Next] - p.Position;
            return d * (Math.Min(p.ServiceId != 0 ? 1.8 : p.Cruise, d.Length * 3) / Math.Max(.01, d.Length));
        }

        return desired;
    }

    private WalkDetour? FindWalkDetour(CityPoint origin, CityPoint goal)
    {
        const int n = 49, mid = n / 2;
        const double cell = .65;
        int start = mid + n * mid;
        var d = goal - origin;
        var target = d.Length > 12 ? origin + d * (12 / d.Length) : goal;
        var obstacles = _activeScenery.Where(b => Math.Abs(b.X - origin.X) < 20 && Math.Abs(b.Y - origin.Z) < 20)
            .ToList();
        obstacles.AddRange(PropsInBox(origin.X - 19, origin.Z - 19, origin.X + 19, origin.Z + 19).Where(p =>
            p.Mass > 2 && p.Body is not null && Math.Abs(p.Position.X - origin.X) < 19 &&
            Math.Abs(p.Position.Z - origin.Z) < 19).Select(p => p.Body!));
        obstacles.AddRange(Cars.Where(c => c.Physical && Math.Abs(c.Speed) < 2 && (c.Position - origin).Length < 22)
            .Select(c => c.Body));

        bool Occupied(CityPoint point)
        {
            if (!ClearWalk(point)) return true;
            foreach (var b in obstacles)
            {
                double x = point.X - b.X, z = point.Z - b.Y;
                if (b.Shape == Shape2D.Circle)
                {
                    if (x * x + z * z < (b.Radius + .34) * (b.Radius + .34)) return true;
                }
                else
                {
                    double ca = Math.Cos(b.Angle), sa = Math.Sin(b.Angle), lx = x * ca + z * sa, lz = -x * sa + z * ca;
                    if (Math.Abs(lx) < b.HalfW + .36 && Math.Abs(lz) < b.HalfH + .36) return true;
                }
            }

            return false;
        }

        var blocked = new bool[n * n];
        for (int k = 0; k < blocked.Length; k++) blocked[k] = Occupied(At(k));
        blocked[start] = false;
        var cost = new double[n * n];
        Array.Fill(cost, double.PositiveInfinity);
        cost[start] = 0;
        var previous = new int[n * n];
        Array.Fill(previous, -1);
        var closed = new bool[n * n];
        var queue = new PriorityQueue<int, double>();
        queue.Enqueue(start, 0);
        int best = start;
        double bestDistance = (origin - target).Length;
        while (queue.TryDequeue(out int k, out _))
        {
            if (closed[k]) continue;
            closed[k] = true;
            double distance = (At(k) - target).Length;
            if (distance < bestDistance)
            {
                best = k;
                bestDistance = distance;
            }

            if (distance < .65) break;
            int x = k % n, z = k / n;
            for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= n || nz >= n) continue;
                int next = nx + n * nz;
                if (blocked[next] || closed[next] ||
                    dx != 0 && dz != 0 && (blocked[x + dx + n * z] || blocked[x + n * (z + dz)])) continue;
                double value = cost[k] + (dx != 0 && dz != 0 ? 1.41421356 : 1) * cell;
                if (value >= cost[next]) continue;
                cost[next] = value;
                previous[next] = k;
                queue.Enqueue(next, value + (At(next) - target).Length);
            }
        }

        if (best == start || bestDistance > (origin - target).Length - 1) return null;
        var points = new List<CityPoint>();
        for (int k = best; k != start && k >= 0; k = previous[k]) points.Add(At(k));
        points.Reverse();
        return new() { Goal = goal, Points = points, Expires = Time + 25 };

        CityPoint At(int k) => origin + new CityPoint((k % n - mid) * cell, (k / n - mid) * cell);
    }
}
