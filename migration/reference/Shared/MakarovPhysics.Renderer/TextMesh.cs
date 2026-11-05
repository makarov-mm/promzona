using System.Drawing.Drawing2D;

namespace MakarovPhysics.Renderer;

/// <summary>
/// Extruded 3-D text, promoted to the library from the electric motor's
/// magnet letters (12.08: "if it is tied to that module, move it out so any
/// module can use it"). GDI+ supplies the outline of any string in the
/// system sans; the outlines are flattened, each OUTER contour is
/// ear-clipped and extruded with proper side-wall normals.
///
/// Honest limitation, inherited and documented: contours that lie INSIDE
/// another contour are letter holes (the counters of O, A, B), and the
/// triangulator here does not cut them - such letters come out filled.
/// Straight-sided capitals (N, S, E, W, T, L, F, H, K, M, V, X, Y, Z,
/// digits 1, 2, 3, 5, 7) extrude exactly. Multi-letter strings work: every
/// standalone outer contour is built, which the motor's original
/// largest-contour-only version could not do.
/// </summary>
public static class TextMesh
{
    /// <summary>Build an extruded mesh of the string: XY plane, centred at
    /// the origin, the given total height, depth along Z.</summary>
    public static Mesh Build(string text, float height, float depth)
    {
        using var path = new GraphicsPath();
        using var fam = new FontFamily(System.Drawing.Text.GenericFontFamilies.SansSerif);
        path.AddString(text, fam, (int)FontStyle.Bold, 96f, new PointF(0, 0), StringFormat.GenericTypographic);
        path.Flatten(null, 0.30f);

        // contours: a subpath starts where PathTypes says Start (0)
        PointF[] pts = path.PathPoints;
        byte[] types = path.PathTypes;
        var contours = new List<List<(float X, float Y)>>();
        List<(float X, float Y)>? current = null;

        for (int i = 0; i < pts.Length; i++)
        {
            if ((types[i] & 0x07) == 0)
            {
                current = new(); 
                contours.Add(current);
            }

            current?.Add((pts[i].X, pts[i].Y));
        }

        // clean each contour: drop consecutive duplicates (Flatten leaves
        // them, and a zero-length edge is a degenerate ear) and the closing
        // repeat of the first point
        var cleaned = new List<List<(float X, float Y)>>();

        foreach (List<(float X, float Y)> c in contours)
        {
            List<(float X, float Y)> poly = [];

            foreach ((float X, float Y) q in c.Where(q => poly.Count == 0 || MathF.Abs(q.X - poly[^1].X) + MathF.Abs(q.Y - poly[^1].Y) > 1e-3f))
            {
                poly.Add(q);
            }

            if (poly.Count > 1 && MathF.Abs(poly[0].X - poly[^1].X) + MathF.Abs(poly[0].Y - poly[^1].Y) < 1e-3f)
            {
                poly.RemoveAt(poly.Count - 1);
            }

            if (poly.Count >= 3 && SignedAreaAbs(poly) > 1e-2f)
            {
                cleaned.Add(poly);
            }
        }

        if (cleaned.Count == 0)
        {
            return new Mesh([], []);
        }

        // outer contours only: a contour whose first vertex sits inside an
        // ODD number of the others is a hole - skip it (see the class doc)
        var outers = new List<List<(float X, float Y)>>();

        for (int i = 0; i < cleaned.Count; i++)
        {
            int inside = cleaned.Where((t, j) => j != i && Contains(t, cleaned[i][0])).Count();

            if ((inside & 1) == 0)
            {
                outers.Add(cleaned[i]);
            }
        }

        // normalise the WHOLE string as one block: centre at the origin,
        // height as asked, GDI's y-down flipped to the scene's y-up
        float minX = float.MaxValue, maxX = float.MinValue;
        float minY = float.MaxValue, maxY = float.MinValue;

        foreach ((float X, float Y) q in outers.SelectMany(poly => poly))
        {
            minX = MathF.Min(minX, q.X); maxX = MathF.Max(maxX, q.X);
            minY = MathF.Min(minY, q.Y); maxY = MathF.Max(maxY, q.Y);
        }

        float scale = height / MathF.Max(1e-3f, maxY - minY);
        float cx = (minX + maxX) * 0.5f, cy = (minY + maxY) * 0.5f;

        var vertices = new List<float>();
        var idx = new List<uint>();
        float hz = depth * 0.5f;

        uint Emit(float x, float y, float z, float nx, float ny, float nz)
        {
            vertices.Add(x);
            vertices.Add(y);
            vertices.Add(z);

            vertices.Add(nx);
            vertices.Add(ny);
            vertices.Add(nz);

            return (uint)(vertices.Count / 6 - 1);
        }

        foreach (List<(float X, float Y)> raw in outers)
        {
            List<(float X, float Y)> poly =
            [
                with(raw.Count),
                .. raw.Select(q => ((q.X - cx) * scale, -(q.Y - cy) * scale))
            ];

            if (SignedArea(poly) < 0) poly.Reverse(); // CCW for the clipper
            List<(int a, int b, int c)> tris = EarClip(poly);

            var front = new uint[poly.Count];
            var back = new uint[poly.Count];

            for (int i = 0; i < poly.Count; i++)
            {
                front[i] = Emit(poly[i].X, poly[i].Y, hz, 0, 0, 1);
                back[i] = Emit(poly[i].X, poly[i].Y, -hz, 0, 0, -1);
            }

            foreach (var (a, b, c) in tris)
            {
                idx.Add(front[a]); idx.Add(front[b]); idx.Add(front[c]);
                idx.Add(back[a]); idx.Add(back[c]); idx.Add(back[b]);
            }

            for (int i = 0; i < poly.Count; i++)
            {
                int j = (i + 1) % poly.Count;
                (float X, float Y) e = (X: poly[j].X - poly[i].X, Y: poly[j].Y - poly[i].Y);
                float el = MathF.Sqrt(e.X * e.X + e.Y * e.Y);
                if (el < 1e-6f) continue;
                float nx = e.Y / el, ny = -e.X / el; // outward normal of a CCW edge
                uint v0 = Emit(poly[i].X, poly[i].Y, hz, nx, ny, 0);
                uint v1 = Emit(poly[j].X, poly[j].Y, hz, nx, ny, 0);
                uint v2 = Emit(poly[j].X, poly[j].Y, -hz, nx, ny, 0);
                uint v3 = Emit(poly[i].X, poly[i].Y, -hz, nx, ny, 0);
                idx.Add(v0); idx.Add(v2); idx.Add(v1);
                idx.Add(v0); idx.Add(v3); idx.Add(v2);
            }
        }

        return new Mesh(vertices.ToArray(), idx.ToArray());
    }

    /// <summary>Even-odd point-in-polygon (ray to +X).</summary>
    private static bool Contains(List<(float X, float Y)> poly, (float X, float Y) p)
    {
        bool inside = false;

        for (int i = 0, j = poly.Count - 1; i < poly.Count; j = i++)
        {
            if (poly[i].Y > p.Y != poly[j].Y > p.Y
                &&
                p.X < (poly[j].X - poly[i].X) * (p.Y - poly[i].Y) / (poly[j].Y - poly[i].Y) + poly[i].X)
            {
                inside = !inside;
            }
        }

        return inside;
    }

    private static float SignedArea(List<(float X, float Y)> p)
    {
        float a = 0;

        for (int i = 0; i < p.Count; i++)
        {
            (float X, float Y) q = p[(i + 1) % p.Count];
            a += p[i].X * q.Y - q.X * p[i].Y;
        }

        return a * 0.5f;
    }

    private static float SignedAreaAbs(List<(float X, float Y)> p) => MathF.Abs(SignedArea(p));

    /// <summary>Ear clipping for a simple CCW polygon. O(n²) on a flattened
    /// glyph outline - dozens of vertices, built once.</summary>
    private static List<(int a, int b, int c)> EarClip(List<(float X, float Y)> poly)
    {
        List<int> ring = [with(poly.Count)];

        for (int i = 0; i < poly.Count; i++)
        {
            ring.Add(i);
        }

        var tris = new List<(int, int, int)>();
        static float Cross((float X, float Y) o, (float X, float Y) a, (float X, float Y) b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        int guard = poly.Count * poly.Count + 16;

        while (ring.Count > 3 && guard-- > 0)
        {
            bool clipped = false;

            for (int i = 0; i < ring.Count; i++)
            {
                int i0 = ring[(i + ring.Count - 1) % ring.Count];
                int i1 = ring[i];
                int i2 = ring[(i + 1) % ring.Count];

                if (Cross(poly[i0], poly[i1], poly[i2]) <= 1e-9f)
                {
                    continue; // reflex
                }

                bool anyInside = false;

                foreach (int k in ring)
                {
                    if (k == i0 || k == i1 || k == i2) continue;

                    if (Cross(poly[i0], poly[i1], poly[k]) >= 0
                        &&
                        Cross(poly[i1], poly[i2], poly[k]) >= 0
                        &&
                        Cross(poly[i2], poly[i0], poly[k]) >= 0)
                    {
                        anyInside = true;
                        break;
                    }
                }

                if (anyInside) continue;
                tris.Add((i0, i1, i2));
                ring.RemoveAt(i);
                clipped = true;
                break;
            }

            if (!clipped) break; // degenerate leftovers: fan them below
        }

        for (int i = 1; i + 1 < ring.Count; i++)
        {
            tris.Add((ring[0], ring[i], ring[i + 1]));
        }

        return tris;
    }
}
