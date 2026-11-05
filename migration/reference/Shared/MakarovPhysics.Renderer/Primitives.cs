namespace MakarovPhysics.Renderer;

public static class Primitives
{
    public static Mesh Cube()
    {
        float[] v =
        [
            // +X
             0.5f, -0.5f, -0.5f,  1,  0,  0,
             0.5f,  0.5f, -0.5f,  1,  0,  0,
             0.5f,  0.5f,  0.5f,  1,  0,  0,
             0.5f, -0.5f,  0.5f,  1,  0,  0,

            // -X
            -0.5f, -0.5f,  0.5f, -1,  0,  0,
            -0.5f,  0.5f,  0.5f, -1,  0,  0,
            -0.5f,  0.5f, -0.5f, -1,  0,  0,
            -0.5f, -0.5f, -0.5f, -1,  0,  0,

            // +Y
            -0.5f,  0.5f, -0.5f,  0,  1,  0,
            -0.5f,  0.5f,  0.5f,  0,  1,  0,
             0.5f,  0.5f,  0.5f,  0,  1,  0,
             0.5f,  0.5f, -0.5f,  0,  1,  0,

            // -Y
            -0.5f, -0.5f,  0.5f,  0, -1,  0,
            -0.5f, -0.5f, -0.5f,  0, -1,  0,
             0.5f, -0.5f, -0.5f,  0, -1,  0,
             0.5f, -0.5f,  0.5f,  0, -1,  0,

            // +Z
            -0.5f, -0.5f,  0.5f,  0,  0,  1,
             0.5f, -0.5f,  0.5f,  0,  0,  1,
             0.5f,  0.5f,  0.5f,  0,  0,  1,
            -0.5f,  0.5f,  0.5f,  0,  0,  1,

            // -Z
             0.5f, -0.5f, -0.5f,  0,  0, -1,
            -0.5f, -0.5f, -0.5f,  0,  0, -1,
            -0.5f,  0.5f, -0.5f,  0,  0, -1,
             0.5f,  0.5f, -0.5f,  0,  0, -1
        ];

        List<uint> idx = [];

        for (uint f = 0; f < 6; f++)
        {
            uint b = f * 4;
            idx.Add(b);
            idx.Add(b + 1);
            idx.Add(b + 2);
            idx.Add(b);
            idx.Add(b + 2);
            idx.Add(b + 3);
        }

        return new Mesh(v, [.. idx]);
    }

    /// <summary>Alias of Sphere with (stacks, sectors) order - as PendulumScene calls it.</summary>
    /// <summary>
    /// A unit cube with chambered edges and corners. Perfectly sharp edges are
    /// the loudest tell of computer graphics: nothing manufactured has them,
    /// and a real chamfer catches the key light as a thin bright line along
    /// every edge. With the微 microfacet shading and the environment term that
    /// line is what reads as "machined" rather than "primitive".
    ///
    /// Built by pushing the eight corners of a smaller cube outward: each face
    /// becomes a square, each edge a strip and each corner a triangle, all with
    /// their own flat normals so the chamfer stays crisp.
    /// </summary>
    public static Mesh BeveledCube(float bevel = 0.045f)
    {
        float b = Math.Clamp(bevel, 0.002f, 0.24f);
        float o = 0.5f - b; // the inner cube half extent

        List<float> vertices = [with(1024)];
        List<uint> idx = [with(1024)];

        void Quad(Vec3 a, Vec3 bb, Vec3 c, Vec3 d)
        {
            Vec3 n = Vec3.Cross(bb - a, c - a);
            float len = n.Length();
            if (len < 1e-9f) return;
            n *= 1f / len;
            uint start = (uint)(vertices.Count / 6);

            foreach (Vec3 p in (Span<Vec3>)[a, bb, c, d])
            {
                vertices.Add(p.X);
                vertices.Add(p.Y);
                vertices.Add(p.Z);
                vertices.Add(n.X);
                vertices.Add(n.Y);
                vertices.Add(n.Z);
            }

            idx.Add(start);
            idx.Add(start + 1);
            idx.Add(start + 2);
            idx.Add(start);
            idx.Add(start + 2);
            idx.Add(start + 3);
        }

        void Tri(Vec3 a, Vec3 bb, Vec3 c)
        {
            Vec3 n = Vec3.Cross(bb - a, c - a);
            float len = n.Length();
            if (len < 1e-9f) return;
            n *= 1f / len;
            uint start = (uint)(vertices.Count / 6);

            foreach (Vec3 p in (Span<Vec3>)[a, bb, c])
            {
                vertices.Add(p.X);
                vertices.Add(p.Y);
                vertices.Add(p.Z);
                vertices.Add(n.X);
                vertices.Add(n.Y);
                vertices.Add(n.Z);
            }

            idx.Add(start);
            idx.Add(start + 1);
            idx.Add(start + 2);
        }

        // the six faces, inset by the bevel
        foreach (int axis in (ReadOnlySpan<int>)[0, 1, 2])
        {
            foreach (int sign in (ReadOnlySpan<int>)[-1, 1])
            {
                Vec3 n = axis == 0 ? new Vec3(sign, 0, 0) : axis == 1 ? new Vec3(0, sign, 0) : new Vec3(0, 0, sign);
                Vec3 u = axis == 0 ? new Vec3(0, 1, 0) : axis == 1 ? new Vec3(0, 0, 1) : new Vec3(1, 0, 0);
                Vec3 w = Vec3.Cross(n, u);
                Vec3 c = n * 0.5f;
                Vec3 a0 = c - u * o - w * o, a1 = c + u * o - w * o, a2 = c + u * o + w * o, a3 = c - u * o + w * o;
                // no sign flip here: w = n x u already carries the handedness,
                // so reversing the winding for the negative faces turned them
                // inside out (six inward triangles, exactly the three -X/-Y/-Z faces)
                Quad(a0, a1, a2, a3);
            }
        }

        // twelve edge strips and eight corner triangles, from the signed
        // corner lattice of the inner cube
        foreach (int sx in (ReadOnlySpan<int>)[-1, 1])
        {
            foreach (int sy in (ReadOnlySpan<int>)[-1, 1])
            {
                foreach (int sz in (ReadOnlySpan<int>)[-1, 1])
                {
                    // the three points where this corner meets its faces
                    var px = new Vec3(sx * 0.5f, sy * o, sz * o);
                    var py = new Vec3(sx * o, sy * 0.5f, sz * o);
                    var pz = new Vec3(sx * o, sy * o, sz * 0.5f);

                    // a corner facet, wound outward
                    if (sx * sy * sz > 0)
                    {
                        Tri(px, py, pz);
                    }
                    else
                    {
                        Tri(px, pz, py);
                    }
                }
            }
        }

        // edges: for each axis pair, connect the two corner points along the axis
        foreach (int axis in (ReadOnlySpan<int>)[0, 1, 2])
        {
            foreach (int s1 in (ReadOnlySpan<int>)[-1, 1])
            {
                foreach (int s2 in (ReadOnlySpan<int>)[-1, 1])
                {
                    Vec3 A, B, C, D;

                    if (axis == 0)
                    {
                        A = new Vec3(-o, s1 * 0.5f, s2 * o); B = new Vec3(o, s1 * 0.5f, s2 * o);
                        C = new Vec3(o, s1 * o, s2 * 0.5f); D = new Vec3(-o, s1 * o, s2 * 0.5f);
                    }
                    else if (axis == 1)
                    {
                        A = new Vec3(s2 * o, -o, s1 * 0.5f); B = new Vec3(s2 * o, o, s1 * 0.5f);
                        C = new Vec3(s2 * 0.5f, o, s1 * o); D = new Vec3(s2 * 0.5f, -o, s1 * o);
                    }
                    else
                    {
                        A = new Vec3(s1 * 0.5f, s2 * o, -o); B = new Vec3(s1 * 0.5f, s2 * o, o);
                        C = new Vec3(s1 * o, s2 * 0.5f, o); D = new Vec3(s1 * o, s2 * 0.5f, -o);
                    }

                    Vec3 mid = (A + B + C + D) * 0.25f;
                    Vec3 n = Vec3.Cross(B - A, C - A);

                    if (Vec3.Dot(n, mid) < 0)
                    {
                        Quad(A, D, C, B);
                    }
                    else
                    {
                        Quad(A, B, C, D);
                    }
                }
            }
        }

        return new Mesh([.. vertices], [.. idx]);
    }

    public static Mesh UvSphere(int stacks = 16, int sectors = 32) => Sphere(sectors, stacks);

    public static Mesh Sphere(int sectors = 32, int stacks = 16)
    {
        var (vertices, indices) = SphereData(sectors, stacks);
        return new Mesh(vertices, indices);
    }

    /// <summary>The sphere's vertex (position + normal) and index arrays, for a
    /// caller that builds its own vertex array (instanced rocks).</summary>
    public static (float[] Vertices, uint[] Indices) SphereData(int sectors = 32, int stacks = 16)
    {
        List<float> vertices = [];

        for (int i = 0; i <= stacks; i++)
        {
            float phi = MathF.PI / 2f - i * MathF.PI / stacks;
            float y = MathF.Sin(phi);
            float r = MathF.Cos(phi);

            for (int j = 0; j <= sectors; j++)
            {
                float theta = j * 2f * MathF.PI / sectors;
                float x = r * MathF.Cos(theta);
                float z = r * MathF.Sin(theta);

                vertices.Add(x); vertices.Add(y); vertices.Add(z);
                vertices.Add(x); vertices.Add(y); vertices.Add(z);
            }
        }

        List<uint> idx = [];
        int cols = sectors + 1;

        for (int i = 0; i < stacks; i++)
        {
            for (int j = 0; j < sectors; j++)
            {
                uint a = (uint)(i * cols + j);
                uint b = (uint)((i + 1) * cols + j);
                // CCW when seen from OUTSIDE (row i sits above row i+1, theta
                // grows towards +z): with the old (a, b, a+1) order the
                // geometric normal pointed inward, so back-face culling kept
                // the far inner surface - mirrored lighting and far-hemisphere
                // depth writes ("rods show through balls").
                idx.Add(a); idx.Add(a + 1); idx.Add(b);
                idx.Add(a + 1); idx.Add(b + 1); idx.Add(b);
            }
        }

        return ([.. vertices], [.. idx]);
    }

    /// <summary>
    /// Open-ended cylinder wall: radius 1, height 1 (y ∈ [−0.5, +0.5]), radial
    /// normals. No caps - capsules cover the ends with hemispheres anyway.
    /// The index pattern mirrors Sphere() so the winding matches back-face culling.
    /// </summary>
    /// <param name="capped">End caps with ±Y normals. Default FALSE: every
    /// pre-03.08 caller (including Sandbox's deliberately open tubes) keeps
    /// its exact geometry. Without caps a cylinder seen end-on is a pipe:
    /// the viewer looks INTO it at the far wall's inner surface, whose
    /// radial normal points away - lit as a black hole. Caught live on the
    /// DC motor's battery terminals (Michael, 03.08); the shaft and the
    /// lamp base had the same hole waiting at other camera angles.</param>
    public static Mesh Cylinder(int sectors = 26, bool capped = false)
    {
        List<float> vertices = [];

        for (int i = 0; i <= 1; i++) // two rings: top row, then bottom
        {
            float y = 0.5f - i;

            for (int j = 0; j <= sectors; j++)
            {
                float theta = j * 2f * MathF.PI / sectors;
                float x = MathF.Cos(theta);
                float z = MathF.Sin(theta);

                vertices.Add(x); vertices.Add(y); vertices.Add(z);
                vertices.Add(x); vertices.Add(0f); vertices.Add(z); // radial normal
            }
        }

        List<uint> idx = [];
        int cols = sectors + 1;

        for (int j = 0; j < sectors; j++)
        {
            uint a = (uint)j; // top ring
            uint b = (uint)(cols + j); // bottom ring
            idx.Add(a); idx.Add(a + 1); idx.Add(b); // CCW from outside, as Sphere()
            idx.Add(a + 1); idx.Add(b + 1); idx.Add(b);
        }

        if (capped)
        {
            // two centre fans; cap vertices carry ±Y normals, so the rim gets
            // the hard machined edge a turned part actually has
            foreach (float y in new[] { 0.5f, -0.5f })
            {
                float ny = MathF.Sign(y);
                uint centre = (uint)(vertices.Count / 6);
                vertices.Add(0); vertices.Add(y); vertices.Add(0);
                vertices.Add(0); vertices.Add(ny); vertices.Add(0);
                uint rimStart = (uint)(vertices.Count / 6);

                for (int j = 0; j <= sectors; j++)
                {
                    float theta = j * 2f * MathF.PI / sectors;
                    vertices.Add(MathF.Cos(theta));
                    vertices.Add(y);
                    vertices.Add(MathF.Sin(theta));
                    vertices.Add(0);
                    vertices.Add(ny);
                    vertices.Add(0);
                }

                for (int j = 0; j < sectors; j++)
                {
                    uint r0 = rimStart + (uint)j, r1 = r0 + 1;
                    // top cap CCW from above, bottom cap mirrored
                    if (ny > 0)
                    {
                        idx.Add(centre); 
                        idx.Add(r1); 
                        idx.Add(r0);
                    }
                    else
                    {
                        idx.Add(centre);
                        idx.Add(r0);
                        idx.Add(r1);
                    }
                }
            }
        }

        return new Mesh([.. vertices], [.. idx]);
    }

    public static Mesh Grid(int halfCells = 10, float step = 1f)
    {
        var vertices = new List<float>();
        var idx = new List<uint>();
        uint n = 0;
        float ext = halfCells * step;

        void Add(float x, float z)
        {
            vertices.Add(x);
            vertices.Add(0);
            vertices.Add(z);
            vertices.Add(0);
            vertices.Add(1);
            vertices.Add(0);
        }

        for (int i = -halfCells; i <= halfCells; i++)
        {
            float c = i * step;
            Add(-ext, c); Add(ext, c);
            idx.Add(n++); idx.Add(n++);
            Add(c, -ext); Add(c, ext);
            idx.Add(n++); idx.Add(n++);
        }

        return new Mesh([.. vertices], [.. idx], GL.LINES);
    }

    /// <summary>
    /// A solid cone or frustum, built at its REAL size so it can be drawn with
    /// a plain translation: base radius baseR centred at the origin in the XZ
    /// plane, top radius topR at y = height (topR = 0 gives a sharp apex).
    /// Baking the size in rather than scaling a unit cone is deliberate - a
    /// non-uniform scale would need the inverse-transpose for the normals, and
    /// the lit shader takes uModel straight.
    /// </summary>
    public static Mesh Cone(float baseR, float height, float topR = 0f, int sectors = 48)
    {
        List<float> vertices = [];
        List<uint> idx = [];

        // outward lateral normal: the slant is (topR-baseR, height), so the
        // normal in the (radial, y) half-plane is (height, baseR-topR)
        float dr = baseR - topR;
        float nl = MathF.Sqrt(height * height + dr * dr);
        float nr = nl > 1e-9f ? height / nl : 1f;
        float nyy = nl > 1e-9f ? dr / nl : 0f;
        int cols = sectors + 1;

        for (int ring = 0; ring <= 1; ring++) // 0 = base, 1 = top
        {
            float y = ring == 0 ? 0f : height;
            float rad = ring == 0 ? baseR : topR;

            for (int j = 0; j <= sectors; j++)
            {
                float th = j * 2f * MathF.PI / sectors;
                float cx = MathF.Cos(th), cz = MathF.Sin(th);

                vertices.Add(cx * rad);
                vertices.Add(y);
                vertices.Add(cz * rad);
                vertices.Add(cx * nr);
                vertices.Add(nyy);
                vertices.Add(cz * nr);
            }
        }

        for (int j = 0; j < sectors; j++)
        {
            uint a = (uint)j, b = (uint)(cols + j);

            if (topR > 1e-6f)
            {
                idx.Add(a);
                idx.Add(b); 
                idx.Add(a + 1); 
                idx.Add(a + 1); 
                idx.Add(b); 
                idx.Add(b + 1);
            }
            else
            {
                // degenerate top ring: one triangle
                idx.Add(a); 
                idx.Add(b); 
                idx.Add(a + 1);
            }
        }

        // caps: a fan per end, with its own ±Y normal so the rim is a hard edge
        AddCap(vertices, idx, 0f, baseR, -1f, sectors);

        if (topR > 1e-6f)
        {
            AddCap(vertices, idx, height, topR, 1f, sectors);
        }

        return new Mesh([.. vertices], [.. idx]);
    }

    private static void AddCap(List<float> vertices, List<uint> idx, float y, float radius, float ny, int sectors)
    {
        uint centre = (uint)(vertices.Count / 6);
        vertices.Add(0); vertices.Add(y); vertices.Add(0);
        vertices.Add(0); vertices.Add(ny); vertices.Add(0);
        uint rim = (uint)(vertices.Count / 6);

        for (int j = 0; j <= sectors; j++)
        {
            float th = j * 2f * MathF.PI / sectors;
            vertices.Add(MathF.Cos(th) * radius);
            vertices.Add(y);
            vertices.Add(MathF.Sin(th) * radius);
            vertices.Add(0);
            vertices.Add(ny);
            vertices.Add(0);
        }

        for (int j = 0; j < sectors; j++)
        {
            uint r0 = rim + (uint)j, r1 = r0 + 1;

            if (ny > 0)
            {
                idx.Add(centre); 
                idx.Add(r1); 
                idx.Add(r0);
            }
            else
            {
                idx.Add(centre);
                idx.Add(r0); 
                idx.Add(r1);
            }
        }
    }

    /// <summary>
    /// A solid torus at its REAL size: major radius in the XY plane, axis
    /// along Z - the wall-mounted-ring convention the cloth collider uses, so
    /// the drawn bar and the bar the fabric rests on are the same object.
    /// </summary>
    public static Mesh Torus(float major, float tube, int majorSeg = 64, int minorSeg = 24)
    {
        List<float> vertices = [];
        List<uint> idx = [];
        int cols = minorSeg + 1;

        for (int i = 0; i <= majorSeg; i++)
        {
            float u = i * 2f * MathF.PI / majorSeg;
            float cu = MathF.Cos(u), su = MathF.Sin(u);

            for (int j = 0; j <= minorSeg; j++)
            {
                float v = j * 2f * MathF.PI / minorSeg;
                float cv = MathF.Cos(v), sv = MathF.Sin(v);
                // ring centre (cu, su, 0)·major; the tube sweeps the radial
                // direction and Z
                float nx = cu * cv, nyv = su * cv, nz = sv;
                vertices.Add(cu * major + nx * tube);
                vertices.Add(su * major + nyv * tube);
                vertices.Add(nz * tube);
                vertices.Add(nx);
                vertices.Add(nyv);
                vertices.Add(nz);
            }
        }

        for (int i = 0; i < majorSeg; i++)
        {
            for (int j = 0; j < minorSeg; j++)
            {
                uint a = (uint)(i * cols + j), b = (uint)((i + 1) * cols + j);

                idx.Add(a);
                idx.Add(b);
                idx.Add(a + 1);
                idx.Add(a + 1);
                idx.Add(b);
                idx.Add(b + 1);
            }
        }

        return new Mesh([.. vertices], [.. idx]);
    }

    /// <summary>
    /// A straight prism: a simple polygon in the XY plane extruded along Z
    /// from -0.5 to +0.5, with flat normals on every side and on both caps.
    /// A wedge, a knife edge, a V-groove wall, a dovetail - the machined
    /// profiles a box cannot make and a rotated box only fakes (06.10: the
    /// Stern-Gerlach knife edge was two bricks leaning into a ridge, and the
    /// corners of the bricks poked out of the pole - "the red magnet is
    /// crooked"). The winding may be given either way round: a clockwise
    /// polygon is reversed, so the outward normals are right whichever way
    /// the profile was typed. The polygon must be convex: the caps are fans
    /// from the first vertex.
    /// </summary>
    public static Mesh Prism(ReadOnlySpan<(float X, float Y)> profile)
    {
        if (profile.Length < 3) throw new ArgumentException("A prism needs at least three profile points.", nameof(profile));

        // shoelace sign: counter-clockwise profiles have positive area
        float area = 0f;
        for (int i = 0; i < profile.Length; i++)
        {
            var (x0, y0) = profile[i];
            var (x1, y1) = profile[(i + 1) % profile.Length];
            area += x0 * y1 - x1 * y0;
        }
        var poly = new (float X, float Y)[profile.Length];
        for (int i = 0; i < profile.Length; i++)
            poly[i] = area >= 0f ? profile[i] : profile[profile.Length - 1 - i];

        List<float> v = [];
        List<uint> idx = [];
        int n = poly.Length;

        // the sides: one quad per edge, the outward normal of an edge (dx, dy)
        // of a counter-clockwise polygon is (dy, -dx)
        for (int i = 0; i < n; i++)
        {
            var (x0, y0) = poly[i];
            var (x1, y1) = poly[(i + 1) % n];
            float dx = x1 - x0, dy = y1 - y0;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 1e-7f) continue;   // a degenerate edge (a wedge whose tip is a point)
            float nx = dy / len, ny = -dx / len;
            uint b = (uint)(v.Count / 6);
            v.AddRange([x0, y0, -0.5f, nx, ny, 0f]);
            v.AddRange([x1, y1, -0.5f, nx, ny, 0f]);
            v.AddRange([x1, y1, 0.5f, nx, ny, 0f]);
            v.AddRange([x0, y0, 0.5f, nx, ny, 0f]);
            // counter-clockwise seen from outside: (x0,-z) -> (x1,-z) -> (x1,+z)
            // runs along the edge then towards +z, and with the outward normal
            // that is the front face
            idx.AddRange([b, b + 1, b + 2, b, b + 2, b + 3]);
        }

        // the caps: fans, the +z cap counter-clockwise seen from +z, the -z
        // cap reversed
        uint capFront = (uint)(v.Count / 6);
        foreach (var (x, y) in poly) v.AddRange([x, y, 0.5f, 0f, 0f, 1f]);
        for (uint i = 1; i + 1 < (uint)n; i++) idx.AddRange([capFront, capFront + i, capFront + i + 1]);
        uint capBack = (uint)(v.Count / 6);
        foreach (var (x, y) in poly) v.AddRange([x, y, -0.5f, 0f, 0f, -1f]);
        for (uint i = 1; i + 1 < (uint)n; i++) idx.AddRange([capBack, capBack + i + 1, capBack + i]);

        return new Mesh([.. v], [.. idx]);
    }
}
