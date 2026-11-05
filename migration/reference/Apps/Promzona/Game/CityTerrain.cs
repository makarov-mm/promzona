namespace Promzona.Game;

/// <summary>Immutable, seeded one-metre height field. Built once with the city,
/// sampled in O(1); no distant rigid bodies or per-frame terrain generation.
/// Buildings have horizontal graded pads, blended into the surrounding landscape.
/// Heights are metres above the original island datum; sea level remains -2.8 m.</summary>
public sealed class CityTerrain
{
    private readonly float[] _heights;
    public int Size { get; }
    public double Origin { get; }
    public float Minimum { get; } = float.MaxValue;
    public float Maximum { get; } = float.MinValue;
    public static CityTerrain Current => CityLayout.Current.Terrain;
    public static double Ground(double x, double z) => Current.Height(x, z);
    public static double Ground(CityPoint p) => Ground(p.X, p.Z);

    public CityTerrain(int seed, IReadOnlyList<CityModel.Obstacle> buildings, Action<double, string>? progress = null, CancellationToken cancel = default, bool industrial = false)
    {
        Origin = -Math.Ceiling(CityTraffic.LandEdge) - 2;
        Size = (int)(-2 * Origin) + 1;
        _heights = new float[Size * Size];
        double phase = (uint)seed % 10007 * .002;

        double Natural(double x, double z)
        {
            double inland = CityTraffic.LandEdge - Math.Max(Math.Abs(x), Math.Abs(z));
            double t = Math.Clamp(inland / 205, 0, 1);
            t = t * t * (3 - 2 * t);
            double h = 20 + 8 * Math.Sin(x * .009 + phase) * Math.Cos(z * .007 - phase * .3)
                          + 7 * Math.Sin(z * .0055 + phase * .7) + 4 * Math.Cos((x + z) * .006 - phase);
            if (CityLayout.Rivers)
            {
                h = 26 + 14 * Math.Sin(x * .0045 + phase) * Math.Cos(z * .004 - phase * .3) +
                    9 * Math.Sin(z * .003 + phase * .7);
                double shore = Math.Clamp((inland - 12) / 210, 0, 1);
                shore = shore * shore * (3 - 2 * shore);
                double beach = -4.8 + Math.Clamp((inland + 2) / 42, 0, 1) * 7.2;
                h = beach * (1 - shore) + h * shore;
                double bank = CityWater.RiverDistance(x);
                if (bank < 150)
                {
                    double blend = Math.Clamp((bank - 22) / 128, 0, 1);
                    blend = blend * blend * (3 - 2 * blend);
                    h = 2.2 * (1 - blend) + h * blend;
                }

                if (bank < CityWater.BankHalf)
                {
                    double v = Math.Clamp((bank - (CityWater.BankHalf - 3.2)) / 3.2, 0, 1);
                    v = v * v * (3 - 2 * v);
                    h = -8 + 10.2 * v;
                }

                return h;
            }

            return h * t;
        }

        double Smooth(double t) { t=Math.Clamp(t,0,1); return t*t*(3-2*t); }
        double Base(double x,double z)
        {
            double h=Natural(x,z);
            if(!industrial)return h;
            double rail=CityIndustrial.RailZ,end=CityIndustrial.RailX1;
            // One continuous railway bed, with a level siding for the parked
            // wagons. A wagon is not a building on a separate block-centre pad.
            double across=1-Smooth((Math.Abs(z-rail)-2.7)/5.3);
            double along=Smooth((x-CityIndustrial.RailX0+6)/6)*(1-Smooth((x-end-3)/10));
            if(across*along<=0)return h;
            double berth=Smooth((x-(end-210))/95);
            double railHeight=Natural(x,rail)*(1-berth)+Natural(end-23,rail)*berth;
            return h+(railHeight-h)*across*along;
        }
        bool TrackObject(CityModel.Obstacle b)=>industrial&&Math.Abs(b.Z-CityIndustrial.RailZ)<.01&&
            (Math.Abs(b.HalfX-CityIndustrial.WagonHalfX)<.01||Math.Abs(b.X-CityIndustrial.RailX1-.4)<.01);

        for (int j = 0; j < Size; j++)
        {
            cancel.ThrowIfCancellationRequested();

            for (int i = 0; i < Size; i++)
            {
                _heights[i + j * Size] = (float)Base(Origin + i, Origin + j);
            }

            if (j % 32 == 0)
            {
                progress?.Invoke(.45 + .025 * j / Size, "Shaping hills and coastline");
            }
        }

        // Raised/level building pads have a 1.5 m collar and a 3 m grading skirt.
        // The collar covers interpolation at footprint corners and the entrance.
        double Pad(CityModel.Obstacle b)
        {
            double low = CityTraffic.Coordinate(0);
            double x = low + (Math.Floor((b.X - low) / 36) + .5) * 36,
                z = low + (Math.Floor((b.Z - low) / 36) + .5) * 36;
            return Base(x, z);
        }

        var grading = new float[_heights.Length];
        foreach (CityModel.Obstacle b in buildings)
        {
            cancel.ThrowIfCancellationRequested();
            if(TrackObject(b))continue;
            double pad = Pad(b);
            int x0 = Math.Max(0, (int)Math.Floor(b.X - b.HalfX - 4.5 - Origin)),
                x1 = Math.Min(Size - 1, (int)Math.Ceiling(b.X + b.HalfX + 4.5 - Origin));
            int z0 = Math.Max(0, (int)Math.Floor(b.Z - b.HalfZ - 4.5 - Origin)),
                z1 = Math.Min(Size - 1, (int)Math.Ceiling(b.Z + b.HalfZ + 4.5 - Origin));
            for (int j = z0; j <= z1; j++)
            for (int i = x0; i <= x1; i++)
            {
                double x = Origin + i, z = Origin + j;
                double d = Math.Max(Math.Abs(x - b.X) - b.HalfX, Math.Abs(z - b.Z) - b.HalfZ);
                double w = Math.Clamp((4.5 - d) / 3, 0, 1);
                w = w * w * (3 - 2 * w);
                // Grading belongs to the plot. Never pull a traffic lane up to
                // its neighbouring foundation: that would form an abrupt ramp.
                double low = CityTraffic.Coordinate(0);
                double dx = Math.Abs(x - low - Math.Round((x - low) / 36) * 36),
                    dz = Math.Abs(z - low - Math.Round((z - low) / 36) * 36);
                double verge = Math.Clamp((Math.Min(dx, dz) - (CityLayout.WideRoads ? 7.3 : 4)) / 2, 0, 1);
                w *= verge * verge * (3 - 2 * verge);
                // Only blend from the base field, avoiding order-dependent compound grading.
                if (w > grading[i + j * Size])
                {
                    grading[i + j * Size] = (float)w;
                    _heights[i + j * Size] = (float)(Base(x, z) * (1 - w) + pad * w);
                }
            }
        }

        // Reapply every protected footprint after skirts: neighbouring skirts must
        // never tilt an already level foundation.
        foreach (var b in buildings)
        {
            if(TrackObject(b))continue;
            double pad = Pad(b);
            int x0 = Math.Max(0, (int)Math.Floor(b.X - b.HalfX - .8 - Origin)),
                x1 = Math.Min(Size - 1, (int)Math.Ceiling(b.X + b.HalfX + .8 - Origin));
            int z0 = Math.Max(0, (int)Math.Floor(b.Z - b.HalfZ - .8 - Origin)),
                z1 = Math.Min(Size - 1, (int)Math.Ceiling(b.Z + b.HalfZ + .8 - Origin));
            for (int j = z0; j <= z1; j++)
            for (int i = x0; i <= x1; i++)
                _heights[i + j * Size] = (float)pad;
        }

        foreach (float h in _heights)
        {
            Minimum = Math.Min(Minimum, h);
            Maximum = Math.Max(Maximum, h);
        }

        progress?.Invoke(.48, "Terrain and foundations ready");
    }

    public double Height(double x, double z) => CityWater.Bridge(x, z) ? CityWater.Deck(x, z) : BedHeight(x, z);

    public double BedHeight(double x, double z)
    {
        double u = Math.Clamp(x - Origin, 0, Size - 1.000001), v = Math.Clamp(z - Origin, 0, Size - 1.000001);
        int i = (int)u, j = (int)v;
        u -= i;
        v -= j;
        int k = i + j * Size;
        // Same diagonal as the terrain mesh: a,d,c and a,c,b.
        double a = _heights[k], b = _heights[k + 1], c = _heights[k + Size + 1], d = _heights[k + Size];
        return u >= v ? a + (b - a) * u + (c - b) * v : a + (c - d) * u + (d - a) * v;
    }

    public CityPoint Gradient(double x, double z, double radius = .5) => new(
        (Height(x + radius, z) - Height(x - radius, z)) / (2 * radius),
        (Height(x, z + radius) - Height(x, z - radius)) / (2 * radius));

    public bool ClearLine(CityPoint a, double ay, CityPoint b, double by, double clearance = .05)
    {
        CityPoint d = b - a;
        int n = Math.Max(1, (int)Math.Ceiling(d.Length / .5));

        for (int i = 1; i < n; i++)
        {
            double t = i / (double)n;
            CityPoint p = a + d * t;

            if (CityRoadLevels.Solid(p.X,ay+(by-ay)*t,p.Z,clearance))
            {
                return false;
            }
        }

        return true;
    }
}
