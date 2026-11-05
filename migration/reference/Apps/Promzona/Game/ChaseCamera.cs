namespace Promzona.Game;

/// <summary>Time-based, rate-limited chase. The camera never inherits a collision's
/// instantaneous yaw; wrapping through +/-pi also follows the short arc.</summary>
public sealed class ChaseCamera
{
    public CityPoint Target { get; private set; }
    public double Heading { get; private set; }
    public double AngularSpeed { get; private set; }
    public double Distance { get; private set; } = 12;

    public void Reset(CityCar car)
    {
        Target = car.Position;
        Heading = car.Heading;
        AngularSpeed = 0;
        Distance = 12;
    }

    public void Reset(CityTraffic t)
    {
        Target = t.PlayerPosition;
        Heading = t.OnFoot ? t.PlayerViewHeading : t.Hero.Heading;
        AngularSpeed = 0;
        Distance = t.OnFoot ? 7 : 12;
    }

    public void Step(CityTraffic t, double dt)
    {
        if (!t.OnFoot)
        {
            Step(t.Hero, dt);
            return;
        }

        if (!double.IsFinite(dt) || dt <= 0) return;
        dt = Math.Min(dt, .1);
        Heading += Math.IEEERemainder(t.PlayerViewHeading - Heading, Math.Tau) * (1 - Math.Exp(-dt * 16));
        AngularSpeed = 0;
        Target += (t.PlayerPosition - Target) * (1 - Math.Exp(-dt * 18));
        Distance += (7 - Distance) * (1 - Math.Exp(-dt * 4));
    }

    public void Step(CityCar car, double dt)
    {
        if (!double.IsFinite(dt) || dt <= 0) return;
        int n = (int)Math.Ceiling(Math.Min(dt, .1) * 120);
        double h = Math.Min(dt, .1) / n;

        for (int i = 0; i < n; i++)
        {
            double error = Math.IEEERemainder(car.Heading - Heading, Math.Tau);
            double accel = Math.Clamp(36 * error - 12 * AngularSpeed, -6, 6);
            AngularSpeed = Math.Clamp(AngularSpeed + accel * h, -2.2, 2.2);
            Heading += AngularSpeed * h;
            Target += (car.Position - Target) * (1 - Math.Exp(-h * 15));
            Distance += (12 + Math.Min(4, Math.Abs(car.Speed) * .14) - Distance) * (1 - Math.Exp(-h * 3));
        }
    }

    // Static obstacles are indexed once per installed city. A camera query only
    // visits cells crossed by its boom; all building extents are inserted, not
    // just their centres. Heights are cached with the immutable layout.
    private readonly record struct CameraBox(double X0,double Y0,double Z0,double X1,double Y1,double Z1);
    private static CityLayout? _cameraLayout;
    private static readonly Dictionary<(int,int),List<CameraBox>> _cameraCells = new();

    private static void PrepareObstacles()
    {
        var layout = CityLayout.Current;
        if (ReferenceEquals(layout, _cameraLayout)) return;
        _cameraCells.Clear();
        _cameraLayout = layout;

        void Add(double x, double low, double z, double hx, double high, double hz)
        {
            var box = new CameraBox(x - hx - .35, low - .35, z - hz - .35, x + hx + .35, high + .35, z + hz + .35);
            for (int j = (int)Math.Floor(box.Z0 / 36); j <= (int)Math.Floor(box.Z1 / 36); j++)
            for (int i = (int)Math.Floor(box.X0 / 36); i <= (int)Math.Floor(box.X1 / 36); i++)
            {
                if (!_cameraCells.TryGetValue((i, j), out var list)) _cameraCells[(i, j)] = list = new();
                list.Add(box);
            }
        }

        foreach (var b in layout.Blocks)
        {
            double y = CityTerrain.Ground(b.X, b.Z);
            Add(b.X, y, b.Z, b.HalfX, y + b.Height, b.HalfZ);
        }

        foreach (var level in layout.Levels)
        {
            if (!level.Tunnel)
            {
                for (double x = level.X - 54; x <= level.X + 54; x += 36)
                {
                    double z = level.CentreZ(x), top = level.Floor(x, z) - .55;

                    for (int side = -1; side <= 1; side += 2)
                    {
                        Add(x, CityTerrain.Ground(x, z + side * 7.1), z + side * 7.1, .7, top, .7);
                    }

                    Add(x, top - .6, z, 1, top + .1, 7.8);
                }
            }
            else
                foreach (double x in new[] { level.X - 54, level.X + 54 })
                {
                    double z = level.CentreZ(x), floor = level.CentreFloor(x), ground = CityTerrain.Ground(x, z);
                    double centre = (floor + 4.9 + ground) * .5, half = Math.Max(.5, (ground - floor - 4.9) * .5);
                    Add(x, centre - half, z, .65, centre + half, 6.6);

                    for (int side = -1; side <= 1; side += 2)
                    {
                        Add(x, floor - .1, z + side * 6.2, .65, floor + 4.9, .4);
                    }
                }
        }
    }

    public static double VisibleFraction(double x, double y, double z, double dx, double dy, double dz)
    {
        PrepareObstacles();
        double best = 1;

        for (int j = (int)Math.Floor(Math.Min(z, z + dz) / 36); j <= (int)Math.Floor(Math.Max(z, z + dz) / 36); j++)
        {
            for (int i = (int)Math.Floor(Math.Min(x, x + dx) / 36); i <= (int)Math.Floor(Math.Max(x, x + dx) / 36); i++)
            {
                if (!_cameraCells.TryGetValue((i, j), out var boxes)) continue;

                foreach (CameraBox b in boxes)
                {
                    double lo = 0, hi = best;

                    if (Slab(x, dx, b.X0, b.X1, ref lo, ref hi) 
                        && 
                        Slab(y, dy, b.Y0, b.Y1, ref lo, ref hi) 
                        &&
                        Slab(z, dz, b.Z0, b.Z1, ref lo, ref hi))
                    {
                        best = Math.Min(best, Math.Max(0, lo - .005));
                    }
                }
            }
        }

        // Spatial sampling rather than a fixed 64 steps: a long overview boom
        // must not skip a thin bridge slab or tunnel portal. Refine the contact.
        double length = Math.Sqrt(dx * dx + dy * dy + dz * dz);
        int count = Math.Max(1, (int)Math.Ceiling(length * best / .2));

        for (int n = 1; n <= count; n++)
        {
            double t = best * n / count;
            if (!CityRoadLevels.Solid(x + dx * t, y + dy * t, z + dz * t, .4)) continue;
            double low = best * (n - 1) / count, high = t;

            for (int k = 0; k < 7; k++)
            {
                double mid = (low + high) * .5;
                if (CityRoadLevels.Solid(x + dx * mid, y + dy * mid, z + dz * mid, .4)) high = mid;
                else low = mid;
            }

            best = low;
            break;
        }

        return best;
    }

    private static bool Slab(double o, double d, double min, double max, ref double lo, ref double hi)
    {
        if (Math.Abs(d) < 1e-10) return o >= min && o <= max;
        double a = (min - o) / d, b = (max - o) / d;
        if (a > b) (a, b) = (b, a);
        lo = Math.Max(lo, a);
        hi = Math.Min(hi, b);
        return hi >= lo;
    }
}
