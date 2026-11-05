// Derived from Shared/MakarovPhysics.RigidBodies2D/World2D.cs (solver 2026-10-05.workshop).
// Game-local addition: vertical contact filtering. Keep impulse equations aligned with upstream.
// Original SHA256: e4e62277ccb8910789fed17486cf84ff701942ac4b28b860cb1fc200c3135451
using MakarovPhysics.RigidBodies2D;
namespace Promzona.Game;

/// <summary>
/// A 2D rigid-body world in the spirit of Box2D-lite: OBB and circle shapes,
/// SAT with reference-face clipping for up to two contact points per pair,
/// sequential impulses with accumulated clamping and warm starting, Coulomb
/// friction bounded by the normal impulse, Baumgarte positional bias with a
/// penetration slop, restitution from the pre-solve approach speed, and a
/// sleep system so a settled stack costs nothing. Broadphase is a uniform
/// spatial hash keyed by body extent.
///
/// The mouse-grab spring carries the lesson learned in the 3D sandbox the
/// hard way: stretch and speed are capped, damping is critical, so dragging
/// a domino never nukes the row it stands in.
///
/// No GL or UI dependencies - runs in SelfTest without a GPU.
/// Twin-measured: see Sandbox2DTests (rest on the floor, bounce height vs
/// restitution, equal-mass exchange, a stack that keeps standing).
/// </summary>
public sealed class CityContactWorld
{
    // Bumped whenever solver BEHAVIOUR changes (not on comment/refactor
    // edits) - Sandbox2DTests prints this next to the DLL path so a stale
    // CityContactWorld.cs can never again hide behind a fresh-looking build. Added
    // 02.08 after the per-point warm-start fix silently failed to reach a
    // working copy and the SAME old bug reappeared bit-for-bit.
    public const string SolverVersion = "2026-10-05.workshop";

    public Func<Body2D,Body2D,bool>? ContactFilter;
    public readonly List<Body2D> Bodies = [];
    public readonly List<CityContactJoint> Joints = [];
    public double GravityY = -9.8;
    private const int SolverIterations = 12; // seven-box towers want the extra two
    private const double Beta = 0.2; // Baumgarte factor
    private const double Slop = 0.005; // allowed penetration, metres
    private const double SleepLinear = 0.012;
    private const double SleepAngular = 0.05;
    private const double SleepDelay = 0.6;

    private struct ContactPoint
    {
        public double Px, Py; // world position
        public double Pen; // penetration depth (> 0 overlapping)
        public double Vn0; // pre-solve approach speed for restitution
        public double AccumN, AccumT; // warm-started impulses
        public double MassN, MassT; // effective masses
        public double Bias;
    }

    private struct Contact
    {
        public int A, B;
        public double Nx, Ny; // from A to B
        public int Count;
        public ContactPoint P0, P1;
    }

    private readonly List<Contact> _contacts = [with(256)];

    // Keyed by (pair, point index) - the POINT is part of the key, so the
    // value is one impulse pair, full stop. The first cut stored a
    // four-slot tuple "for both points" on top of the per-point key and
    // read n1/t1 for k = 1 while writing n0/t0: the second contact point of
    // every box-box manifold warm-started from ZERO, and the asymmetry
    // (one corner warm, one cold) walked the seven-box stack 0.351 sideways
    // in 5 s - the exact SelfTest failure of 02.08, reproduced to the digit
    // by Tools/sandbox2d-mirror.py once the bug was emulated there.
    private readonly Dictionary<long, (double n, double t)> _warm = [with(256)];
    private readonly Dictionary<long, (double n, double t)> _warmNext = [with(256)];

    public int GrabbedIndex = -1;
    public double GrabLocalX, GrabLocalY;
    public double DragTargetX, DragTargetY;
    public Body2D? Grabbed => GrabbedIndex >= 0 && GrabbedIndex < Bodies.Count ? Bodies[GrabbedIndex] : null;

    public void Clear()
    {
        Bodies.Clear();
        Joints.Clear();
        _contacts.Clear();
        _warm.Clear();
        _warmNext.Clear();
        GrabbedIndex = -1;
    }

    /// <summary>Remove a body without leaving stale joint, grab or warm-start indices.</summary>
    public bool RemoveBody(int index)
    {
        if (index < 0 || index >= Bodies.Count) return false;
        Joints.RemoveAll(j => j.A == index || j.B == index);

        foreach (var joint in Joints)
        {
            if (joint.A > index) joint.A--;
            if (joint.B > index) joint.B--;
        }

        if (GrabbedIndex == index)
        {
            Release();
        }
        else if (GrabbedIndex > index)
        {
            GrabbedIndex--;
        }

        Bodies.RemoveAt(index);
        _contacts.Clear();
        _warm.Clear();
        _warmNext.Clear();
        _grid.Clear();
        _pairSeen.Clear();

        foreach (var body in Bodies)
        {
            if (!body.IsStatic)
            {
                body.Wake();
            }
        }

        return true;
    }

    /// <summary>Game-local addition (29.10): remove many bodies in one pass. The
    /// scenery streamer and the physics bubble drop dozens of bodies every quarter
    /// second; RemoveBody per body is O(n) in Bodies, clears every warm start and
    /// wakes the whole world each time, so stacks of bins lost their contact
    /// history four times a second. This compacts the list once, remaps the
    /// warm-start keys and joint indices of the survivors, and wakes only the
    /// bodies that were in contact with a removed one last step - the stack
    /// above a removed support still falls, the far side of the city sleeps on.
    /// Returns the number of bodies removed.</summary>
    public int RemoveBodies(IReadOnlyCollection<Body2D> bodies)
    {
        if (bodies.Count == 0) return 0;
        var gone = new HashSet<Body2D>(bodies);
        var map = new int[Bodies.Count];
        int kept = 0;

        for (int i = 0; i < Bodies.Count; i++)
        {
            if (gone.Contains(Bodies[i])) { map[i] = -1; continue; }
            map[i] = kept;
            Bodies[kept++] = Bodies[i];
        }

        int removed = Bodies.Count - kept;
        if (removed == 0) return 0;

        // Neighbours of a removed body lose their support or their obstacle: wake them.
        foreach (Contact c in _contacts)
        {
            if (map[c.A] < 0 && map[c.B] >= 0) Bodies[map[c.B]].Wake();
            else if (map[c.B] < 0 && map[c.A] >= 0) Bodies[map[c.A]].Wake();
        }

        Bodies.RemoveRange(kept, removed);
        Joints.RemoveAll(j => map[j.A] < 0 || map[j.B] < 0);
        foreach (var joint in Joints) { joint.A = map[joint.A]; joint.B = map[joint.B]; }
        if (GrabbedIndex >= 0) GrabbedIndex = GrabbedIndex < map.Length ? map[GrabbedIndex] : -1;

        _warmNext.Clear();
        foreach (KeyValuePair<long, (double n, double t)> kv in _warm)
        {
            int a = (int)(kv.Key >> 26), b = (int)((kv.Key >> 2) & 0xFFFFFF), point = (int)(kv.Key & 3);
            if (a < map.Length && b < map.Length && map[a] >= 0 && map[b] >= 0) _warmNext[WarmKey(map[a], map[b], point)] = kv.Value;
        }

        _warm.Clear();
        foreach (KeyValuePair<long, (double n, double t)> kv in _warmNext) _warm[kv.Key] = kv.Value;
        _warmNext.Clear();
        _contacts.Clear();
        _grid.Clear();
        _pairSeen.Clear();
        return removed;
    }

    public int AddBox(double x, double y, double halfW, double halfH, double density, double friction, double restitution, int material, double angle = 0)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(angle))
            throw new ArgumentOutOfRangeException(nameof(x), "Body pose must be finite.");
        if (!(halfW > 0) || !(halfH > 0) || !double.IsFinite(halfW) || !double.IsFinite(halfH))
            throw new ArgumentOutOfRangeException(nameof(halfW), "Box half-extents must be finite and positive.");
        if (!(density >= 0) || !double.IsFinite(density))
            throw new ArgumentOutOfRangeException(nameof(density), "Density must be finite and non-negative (0 = static).");
        if (!(friction >= 0) || !double.IsFinite(friction) || !(restitution >= 0) || !double.IsFinite(restitution))
            throw new ArgumentOutOfRangeException(nameof(friction), "Friction and restitution must be finite and non-negative.");

        var b = new Body2D
        {
            Shape = Shape2D.Box, X = x, Y = y, Angle = angle,
            HalfW = halfW, HalfH = halfH,
            Friction = friction, Restitution = restitution, MaterialIndex = material,
        };

        if (density > 0)
        {
            double mass = density * 4 * halfW * halfH;
            b.InvMass = 1.0 / mass;
            double inertia = mass * (4 * halfW * halfW + 4 * halfH * halfH) / 12.0;
            b.InvI = 1.0 / inertia;
        }

        Bodies.Add(b);
        return Bodies.Count - 1;
    }

    public int AddCircle(double x, double y, double radius, double density, double friction, double restitution, int material)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y))
            throw new ArgumentOutOfRangeException(nameof(x), "Body position must be finite.");
        if (!(radius > 0) || !double.IsFinite(radius))
            throw new ArgumentOutOfRangeException(nameof(radius), "Circle radius must be finite and positive.");
        if (!(density >= 0) || !double.IsFinite(density))
            throw new ArgumentOutOfRangeException(nameof(density), "Density must be finite and non-negative (0 = static).");
        if (!(friction >= 0) || !double.IsFinite(friction) || !(restitution >= 0) || !double.IsFinite(restitution))
            throw new ArgumentOutOfRangeException(nameof(friction), "Friction and restitution must be finite and non-negative.");

        var b = new Body2D
        {
            Shape = Shape2D.Circle, X = x, Y = y, Radius = radius,
            Friction = friction, Restitution = restitution, MaterialIndex = material,
        };

        if (density > 0)
        {
            double mass = density * Math.PI * radius * radius;
            b.InvMass = 1.0 / mass;
            b.InvI = 1.0 / (0.5 * mass * radius * radius);
        }

        Bodies.Add(b);
        return Bodies.Count - 1;
    }

    public void AddRevolute(int a, int b, double worldX, double worldY, bool motor = false, double motorSpeed = 0, double maxTorque = 40, bool collideConnected = true)
    {
        if (a < 0 || a >= Bodies.Count || b < 0 || b >= Bodies.Count || a == b)
            throw new ArgumentOutOfRangeException(nameof(a), "Joint body indices must be two distinct existing bodies.");
        if (!double.IsFinite(worldX) || !double.IsFinite(worldY))
            throw new ArgumentOutOfRangeException(nameof(worldX), "Joint anchor must be finite.");
        if (!double.IsFinite(motorSpeed) || !(maxTorque >= 0) || !double.IsFinite(maxTorque))
            throw new ArgumentOutOfRangeException(nameof(motorSpeed), "Motor parameters must be finite; torque non-negative.");

        Body2D ba = Bodies[a], bb = Bodies[b];
        (double lax, double lay) = WorldToLocal(ba, worldX, worldY);
        (double lbx, double lby) = WorldToLocal(bb, worldX, worldY);

        Joints.Add(new CityContactJoint
        {
            A = a, B = b,
            LocalAx = lax, LocalAy = lay, LocalBx = lbx, LocalBy = lby,
            MotorEnabled = motor, MotorSpeed = motorSpeed, MaxMotorTorque = maxTorque, CollideConnected = collideConnected,
        });
    }

    private static (double x, double y) WorldToLocal(Body2D b, double wx, double wy)
    {
        double c = Math.Cos(b.Angle), s = Math.Sin(b.Angle);
        double dx = wx - b.X, dy = wy - b.Y;
        return (c * dx + s * dy, -s * dx + c * dy);
    }

    private static (double x, double y) LocalToWorld(Body2D b, double lx, double ly)
    {
        double c = Math.Cos(b.Angle), s = Math.Sin(b.Angle);
        return (b.X + c * lx - s * ly, b.Y + s * lx + c * ly);
    }

    public void Step(double dt)
    {
        // NaN fails `dt <= 0` and would poison every velocity downstream;
        // the 3D world already guards this, the 2D one now matches (audit 02.08)
        if (!double.IsFinite(dt) || dt <= 0) return;

        ApplyDragSpring(dt);

        foreach (Body2D b in Bodies.Where(b => b is { IsStatic: false, Sleeping: false }))
        {
            b.Vy += GravityY * dt;
        }

        BuildContacts();
        WakeFromContacts();
        PrepareContacts(dt);
        WarmStart();

        foreach(var joint in Joints)joint.MotorImpulse=0;
        for (int i = 0; i < SolverIterations; i++)
        {
            SolveJoints(dt);
            SolveContacts();
        }

        foreach (Body2D b in Bodies.Where(b => b is { IsStatic: false, Sleeping: false }))
        {
            b.X += b.Vx * dt;
            b.Y += b.Vy * dt;
            b.Angle += b.W * dt;
        }

        StoreWarm();
        SleepCheck(dt);
    }

    /// <summary>The 3D sandbox taught this spring its manners: stretch is
    /// capped, damping is critical (zeta = 1 for stiffness 120 / damper 22)
    /// and the resulting speed is limited, so dragging a body through a
    /// standing arrangement moves it instead of detonating the scene. A
    /// deliberate fast fling is still possible at the cap.</summary>
    private void ApplyDragSpring(double dt)
    {
        Body2D? g = Grabbed;
        if (g is null || g.IsStatic) return;
        g.Wake();

        (double ax, double ay) = LocalToWorld(g, GrabLocalX, GrabLocalY);
        double dx = DragTargetX - ax, dy = DragTargetY - ay;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len > 1.0) { dx *= 1.0 / len; dy *= 1.0 / len; }

        (double vx, double vy) = g.VelocityAt(ax, ay);
        double fx = dx * 120 - vx * 22;
        double fy = dy * 120 - vy * 22;

        // impulse at the anchor: linear plus the torque of the offset
        g.Vx += fx * dt;
        g.Vy += fy * dt;
        // fx, fy are accelerations of the centre; the torque needs the real
        // force, so the mass reappears: r x (m*f) * InvI = r x f * InvI/InvMass
        double rx = ax - g.X, ry = ay - g.Y;
        g.W += (rx * fy - ry * fx) * (g.InvI / g.InvMass) * dt;

        double sp = Math.Sqrt(g.Vx * g.Vx + g.Vy * g.Vy);
        if (sp > 8) { g.Vx *= 8 / sp; g.Vy *= 8 / sp; }
        g.W *= 1.0 / (1.0 + 3.0 * dt);
    }

    private readonly Dictionary<long, List<int>> _grid = new(256);
    private const double CellSize = 1.2;
    private static long GridKey(int gx, int gy) => ((long)gx << 32) | (uint)gy;
    private double BodyExtent(Body2D b) => b.Shape == Shape2D.Circle ? b.Radius : Math.Sqrt(b.HalfW * b.HalfW + b.HalfH * b.HalfH);

    private void BuildContacts()
    {
        _contacts.Clear();
        foreach (var list in _grid.Values) list.Clear();

        for (int i = 0; i < Bodies.Count; i++)
        {
            Body2D b = Bodies[i];
            double e = BodyExtent(b);
            int gx0 = (int)Math.Floor((b.X - e) / CellSize), gx1 = (int)Math.Floor((b.X + e) / CellSize);
            int gy0 = (int)Math.Floor((b.Y - e) / CellSize), gy1 = (int)Math.Floor((b.Y + e) / CellSize);

            for (int gx = gx0; gx <= gx1; gx++)
            {
                for (int gy = gy0; gy <= gy1; gy++)
                {
                    long key = GridKey(gx, gy);

                    if (!_grid.TryGetValue(key, out List<int>? list))
                    {
                        _grid[key] = list = [with(8)];
                    }

                    list.Add(i);
                }
            }
        }

        _pairSeen.Clear();
        _jointPairs.Clear();

        foreach (CityContactJoint joint in Joints)
        {
            if (!joint.CollideConnected)
            {
                _jointPairs.Add(((long)Math.Min(joint.A, joint.B) << 24) | (uint)Math.Max(joint.A, joint.B));
            }
        }

        foreach (var list in _grid.Values)
        {
            for (int a = 0; a < list.Count; a++)
            {
                for (int b = a + 1; b < list.Count; b++)
                {
                    int i = list[a], j = list[b];
                    if (i > j) (i, j) = (j, i);
                    Body2D bi = Bodies[i], bj = Bodies[j];
                    if (bi.IsStatic && bj.IsStatic) continue;
                    if (bi.Sleeping && bj.Sleeping) continue;
                    long pair = ((long)i << 24) | (uint)j;
                    if (!_pairSeen.Add(pair)||_jointPairs.Contains(pair)) continue;
                    if(ContactFilter?.Invoke(Bodies[i],Bodies[j]) != false) Collide(i, j);
                }
            }
        }
    }

    private readonly HashSet<long> _pairSeen = [with(512)];
    private readonly HashSet<long> _jointPairs = [];

    private void Collide(int i, int j)
    {
        Body2D a = Bodies[i], b = Bodies[j];

        switch (a.Shape)
        {
            case Shape2D.Circle when b.Shape == Shape2D.Circle:
                CircleCircle(i, j);
                break;

            case Shape2D.Box when b.Shape == Shape2D.Box:
                BoxBox(i, j);
                break;

            case Shape2D.Box:
                BoxCircle(i, j);
                break;

            default:
                BoxCircle(j, i);
                break;
        }
    }

    private void CircleCircle(int i, int j)
    {
        Body2D a = Bodies[i], b = Bodies[j];
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double rsum = a.Radius + b.Radius;
        double d2 = dx * dx + dy * dy;
        if (d2 >= rsum * rsum) return;
        double d, nx, ny;

        if (d2 < 1e-12)
        {
            // coincident centres carry no direction: pick a deterministic
            // fallback so the pair separates instead of ghosting forever
            d = 0; nx = 1; ny = 0;
        }
        else
        {
            d = Math.Sqrt(d2);
            nx = dx / d; ny = dy / d;
        }

        var c = new Contact
        {
            A = i, 
            B = j, 
            Nx = nx, 
            Ny = ny, 
            Count = 1
        };

        c.P0.Px = a.X + nx * a.Radius;
        c.P0.Py = a.Y + ny * a.Radius;
        c.P0.Pen = rsum - d;
        _contacts.Add(c);
    }

    private void BoxCircle(int boxI, int circleI)
    {
        Body2D box = Bodies[boxI], cir = Bodies[circleI];
        (double lx, double ly) = WorldToLocal(box, cir.X, cir.Y);
        double cx = Math.Clamp(lx, -box.HalfW, box.HalfW);
        double cy = Math.Clamp(ly, -box.HalfH, box.HalfH);
        double dx = lx - cx, dy = ly - cy;
        double d2 = dx * dx + dy * dy;

        double nxL, nyL, pen;
        if (d2 > 1e-12)
        {
            // centre outside the box: normal along the closest-point offset
            double d = Math.Sqrt(d2);
            if (d >= cir.Radius) return;
            nxL = dx / d; nyL = dy / d;
            pen = cir.Radius - d;
        }
        else
        {
            // centre inside: push out along the least-penetrated face
            double px = box.HalfW - Math.Abs(lx);
            double py = box.HalfH - Math.Abs(ly);

            if (px < py)
            {
                nxL = lx >= 0 ? 1 : -1; 
                nyL = 0; 
                pen = px + cir.Radius;
            }
            else
            {
                nxL = 0; 
                nyL = ly >= 0 ? 1 : -1; 
                pen = py + cir.Radius;
            }
        }

        double cang = Math.Cos(box.Angle), sang = Math.Sin(box.Angle);
        double nx = cang * nxL - sang * nyL;
        double ny = sang * nxL + cang * nyL;
        var c = new Contact { A = boxI, B = circleI, Nx = nx, Ny = ny, Count = 1 };
        c.P0.Px = cir.X - nx * cir.Radius;
        c.P0.Py = cir.Y - ny * cir.Radius;
        c.P0.Pen = pen;
        _contacts.Add(c);
    }

    private static void BoxCorners(Body2D b, Span<double> xs, Span<double> ys)
    {
        double c = Math.Cos(b.Angle), s = Math.Sin(b.Angle);
        Span<double> lx = [b.HalfW, -b.HalfW, -b.HalfW, b.HalfW];
        Span<double> ly = [b.HalfH, b.HalfH, -b.HalfH, -b.HalfH];


        for (int k = 0; k < 4; k++)
        {
            xs[k] = b.X + c * lx[k] - s * ly[k];
            ys[k] = b.Y + s * lx[k] + c * ly[k];
        }
    }

    /// <summary>Signed clearance of box `other` beyond face `face` (0..3) of
    /// box `refBox`; negative means overlap along that face normal. Also
    /// returns the world face normal.</summary>
    private static double FaceSeparation(Body2D refBox, int face, Body2D other, out double nx, out double ny)
    {
        double c = Math.Cos(refBox.Angle), s = Math.Sin(refBox.Angle);
        (nx, ny) = face switch
        {
            0 => (c, s),   // +X face
            1 => (-s, c),  // +Y face
            2 => (-c, -s), // -X face
            _ => (s, -c),  // -Y face
        };

        double extent = face is 0 or 2 ? refBox.HalfW : refBox.HalfH;
        double faceOffset = nx * refBox.X + ny * refBox.Y + extent;
        Span<double> xs = stackalloc double[4];
        Span<double> ys = stackalloc double[4];
        BoxCorners(other, xs, ys);
        double min = double.MaxValue;

        for (int k = 0; k < 4; k++)
        {
            min = Math.Min(min, nx * xs[k] + ny * ys[k] - faceOffset);
        }

        return min;
    }

    private void BoxBox(int i, int j)
    {
        Body2D a = Bodies[i], b = Bodies[j];

        int bestFace = -1;
        bool refIsA = true;
        double bestSep = double.MinValue;
        double bnx = 0, bny = 0;

        for (int f = 0; f < 4; f++)
        {
            double sep = FaceSeparation(a, f, b, out double nx, out double ny);
            if (sep > 0) return;
            if (sep > bestSep) { bestSep = sep; bestFace = f; refIsA = true; bnx = nx; bny = ny; }
        }

        for (int f = 0; f < 4; f++)
        {
            double sep = FaceSeparation(b, f, a, out double nx, out double ny);
            if (sep > 0) return;
            // the small bias prefers A as the reference for equal axes -
            // stable feature selection frame to frame keeps warm starts warm
            if (sep > bestSep + 1e-4)
            {
                bestSep = sep;
                bestFace = f; 
                refIsA = false; 
                bnx = nx; bny = ny;
            }
        }

        Body2D refBox = refIsA ? a : b;
        Body2D incBox = refIsA ? b : a;

        // incident face: the face of incBox most opposed to the reference normal
        Span<double> xs = stackalloc double[4];
        Span<double> ys = stackalloc double[4];
        BoxCorners(incBox, xs, ys);
        double ic = Math.Cos(incBox.Angle), isn = Math.Sin(incBox.Angle);
        Span<double> fnx = [ic, -isn, -ic, isn];
        Span<double> fny = [isn, ic, -isn, -ic];
        int inc = 0;
        double best = double.MaxValue;

        for (int f = 0; f < 4; f++)
        {
            double d = fnx[f] * bnx + fny[f] * bny;

            if (d < best)
            {
                best = d; 
                inc = f;
            }
        }

        // Incident edge endpoints. Corner order is (+hw,+hh), (-hw,+hh),
        // (-hw,-hh), (+hw,-hh), so face f (+X, +Y, -X, -Y) spans corners
        // (f-1) -> f: the +X face runs from (+hw,-hh) to (+hw,+hh). The
        // first live build shipped (f, f+1) - the edge of the NEXT face,
        // rotated 90 degrees - and every box-box contact of a resting stack
        // was computed at garbage points, pumping energy until the scene
        // detonated within seconds.
        double e0x = xs[(inc + 3) & 3], e0y = ys[(inc + 3) & 3];
        double e1x = xs[inc], e1y = ys[inc];

        // clip the incident edge by the two side planes of the reference face
        double tx = -bny, ty = bnx;   // reference face tangent
        double refExtent = bestFace == 0 || bestFace == 2 ? (refIsA ? a : b).HalfH : (refIsA ? a : b).HalfW;
        double centreT = tx * refBox.X + ty * refBox.Y;

        if (!ClipSegment(ref e0x, ref e0y, ref e1x, ref e1y, tx, ty, centreT + refExtent)) return;
        if (!ClipSegment(ref e0x, ref e0y, ref e1x, ref e1y, -tx, -ty, -(centreT - refExtent))) return;

        double faceExtent = (bestFace == 0 || bestFace == 2) ? refBox.HalfW : refBox.HalfH;
        double faceOffset = bnx * refBox.X + bny * refBox.Y + faceExtent;

        var contact = new Contact
        {
            A = refIsA ? i : j,
            B = refIsA ? j : i,
            Nx = bnx, Ny = bny,
        };

        double p0 = bnx * e0x + bny * e0y - faceOffset;

        if (p0 <= 0)
        {
            contact.P0.Px = e0x; contact.P0.Py = e0y; contact.P0.Pen = -p0;
            contact.Count = 1;
        }

        double p1 = bnx * e1x + bny * e1y - faceOffset;

        if (p1 <= 0)
        {
            ref ContactPoint pt = ref contact.Count == 0 ? ref contact.P0 : ref contact.P1;
            pt.Px = e1x; pt.Py = e1y; pt.Pen = -p1;
            contact.Count++;
        }

        if (contact.Count > 0)
        {
            _contacts.Add(contact);
        }
    }

    /// <summary>Keep the part of the segment with n·p ≤ offset.</summary>
    private static bool ClipSegment(ref double x0, ref double y0, ref double x1, ref double y1, double nx, double ny, double offset)
    {
        double d0 = nx * x0 + ny * y0 - offset;
        double d1 = nx * x1 + ny * y1 - offset;

        if (d0 > 0 && d1 > 0) return false;

        if (d0 > 0)
        {
            double t = d0 / (d0 - d1);
            x0 += (x1 - x0) * t;
            y0 += (y1 - y0) * t;
        }
        else if (d1 > 0)
        {
            double t = d1 / (d1 - d0);
            x1 += (x0 - x1) * t;
            y1 += (y0 - y1) * t;
        }

        return true;
    }

    private void WakeFromContacts()
    {
        foreach (Contact c in _contacts)
        {
            Body2D a = Bodies[c.A], b = Bodies[c.B];
            if (a.Sleeping == b.Sleeping) continue;
            Body2D awake = a.Sleeping ? b : a;
            double sp = Math.Abs(awake.Vx) + Math.Abs(awake.Vy) + Math.Abs(awake.W);

            if (sp > 0.35)
            {
                a.Wake();
                b.Wake();
            }
        }
    }

    private void PrepareContacts(double dt)
    {
        double invDt = 1.0 / dt;

        for (int ci = 0; ci < _contacts.Count; ci++)
        {
            Contact c = _contacts[ci];
            Body2D a = Bodies[c.A], b = Bodies[c.B];

            for (int k = 0; k < c.Count; k++)
            {
                ref ContactPoint p = ref (k == 0 ? ref c.P0 : ref c.P1);
                double rax = p.Px - a.X, ray = p.Py - a.Y;
                double rbx = p.Px - b.X, rby = p.Py - b.Y;

                // a sleeping body is a wall for the solver: ApplyImpulse will
                // skip it, so the effective mass must treat it as static too,
                // or every impulse against it over-corrects tenfold
                double imA = a.Sleeping ? 0 : a.InvMass, iiA = a.Sleeping ? 0 : a.InvI;
                double imB = b.Sleeping ? 0 : b.InvMass, iiB = b.Sleeping ? 0 : b.InvI;

                double rnA = rax * c.Ny - ray * c.Nx;
                double rnB = rbx * c.Ny - rby * c.Nx;
                double kn = imA + imB + iiA * rnA * rnA + iiB * rnB * rnB;
                p.MassN = kn > 0 ? 1.0 / kn : 0;

                double tx = -c.Ny, ty = c.Nx;
                double rtA = rax * ty - ray * tx;
                double rtB = rbx * ty - rby * tx;
                double kt = imA + imB + iiA * rtA * rtA + iiB * rtB * rtB;
                p.MassT = kt > 0 ? 1.0 / kt : 0;

                (double vax, double vay) = a.VelocityAt(p.Px, p.Py);
                (double vbx, double vby) = b.VelocityAt(p.Px, p.Py);
                p.Vn0 = (vbx - vax) * c.Nx + (vby - vay) * c.Ny;

                p.Bias = -Beta * invDt * Math.Max(p.Pen - Slop, 0);

                long key = WarmKey(c.A, c.B, k);

                if (_warm.TryGetValue(key, out var w))
                {
                    p.AccumN = w.n;
                    p.AccumT = w.t;
                }
            }

            _contacts[ci] = c;
        }
    }

    private static long WarmKey(int a, int b, int point) => (((long)a << 24) | (uint)b) << 2 | (uint)point;

    private void WarmStart()
    {
        for (int ci = 0; ci < _contacts.Count; ci++)
        {
            Contact c = _contacts[ci];
            Body2D a = Bodies[c.A], b = Bodies[c.B];

            for (int k = 0; k < c.Count; k++)
            {
                ref ContactPoint p = ref (k == 0 ? ref c.P0 : ref c.P1);
                double ix = c.Nx * p.AccumN - c.Ny * p.AccumT;
                double iy = c.Ny * p.AccumN + c.Nx * p.AccumT;
                ApplyImpulse(a, b, p.Px, p.Py, -ix, -iy, ix, iy);
            }

            _contacts[ci] = c;
        }
    }

    private static void ApplyImpulse(Body2D a, Body2D b, double px, double py, double axI, double ayI, double bxI, double byI)
    {
        if (a is { IsStatic: false, Sleeping: false })
        {
            a.Vx += axI * a.InvMass;
            a.Vy += ayI * a.InvMass;
            a.W += ((px - a.X) * ayI - (py - a.Y) * axI) * a.InvI;
        }

        if (b is { IsStatic: false, Sleeping: false })
        {
            b.Vx += bxI * b.InvMass;
            b.Vy += byI * b.InvMass;
            b.W += ((px - b.X) * byI - (py - b.Y) * bxI) * b.InvI;
        }
    }

    private void SolveContacts()
    {
        for (int ci = 0; ci < _contacts.Count; ci++)
        {
            Contact c = _contacts[ci];
            Body2D a = Bodies[c.A], b = Bodies[c.B];
            if (a.Sleeping && b.Sleeping) continue;
            double friction = Math.Sqrt(a.Friction * b.Friction);
            double restitution = Math.Max(a.Restitution, b.Restitution);

            for (int k = 0; k < c.Count; k++)
            {
                ref ContactPoint p = ref (k == 0 ? ref c.P0 : ref c.P1);

                (double vax, double vay) = a.VelocityAt(p.Px, p.Py);
                (double vbx, double vby) = b.VelocityAt(p.Px, p.Py);
                double vn = (vbx - vax) * c.Nx + (vby - vay) * c.Ny;

                double bounce = p.Vn0 < -1.0 ? -restitution * p.Vn0 : 0;
                // Baumgarte and restitution must not STACK: at a hard impact
                // the one-frame penetration adds ~1 m/s of positional push on
                // top of e·v and the ball leaves faster than it arrived - the
                // self-test caught the ball peaking ABOVE the e² height. The
                // Box2D rule: the target separating speed is the LARGER of
                // the two, never their sum.
                double push = Math.Max(bounce, -p.Bias);
                double lambda = -(vn - push) * p.MassN;
                double newAccumulator = Math.Max(p.AccumN + lambda, 0);
                lambda = newAccumulator - p.AccumN;
                p.AccumN = newAccumulator;
                ApplyImpulse(a, b, p.Px, p.Py, -c.Nx * lambda, -c.Ny * lambda, c.Nx * lambda, c.Ny * lambda);

                (vax, vay) = a.VelocityAt(p.Px, p.Py);
                (vbx, vby) = b.VelocityAt(p.Px, p.Py);
                double tx = -c.Ny, ty = c.Nx;
                double vt = (vbx - vax) * tx + (vby - vay) * ty;
                double lambdaT = -vt * p.MassT;
                double maxT = friction * p.AccumN;
                double newT = Math.Clamp(p.AccumT + lambdaT, -maxT, maxT);
                lambdaT = newT - p.AccumT;
                p.AccumT = newT;
                ApplyImpulse(a, b, p.Px, p.Py, -tx * lambdaT, -ty * lambdaT, tx * lambdaT, ty * lambdaT);
            }

            _contacts[ci] = c;
        }
    }

    private void SolveJoints(double dt)
    {
        foreach (CityContactJoint j in Joints)
        {
            Body2D a = Bodies[j.A], b = Bodies[j.B];
            if (a.Sleeping && b.Sleeping) continue;
            a.Wake();
            b.Wake();

            (double ax, double ay) = LocalToWorld(a, j.LocalAx, j.LocalAy);
            (double bx, double by) = LocalToWorld(b, j.LocalBx, j.LocalBy);
            double rax = ax - a.X, ray = ay - a.Y;
            double rbx = bx - b.X, rby = by - b.Y;

            // 2x2 effective mass of the point constraint
            double k11 = a.InvMass + b.InvMass + a.InvI * ray * ray + b.InvI * rby * rby;
            double k12 = -a.InvI * rax * ray - b.InvI * rbx * rby;
            double k22 = a.InvMass + b.InvMass + a.InvI * rax * rax + b.InvI * rbx * rbx;
            double det = k11 * k22 - k12 * k12;
            if (Math.Abs(det) < 1e-12) continue;

            (double vax, double vay) = a.VelocityAt(ax, ay);
            (double vbx, double vby) = b.VelocityAt(bx, by);
            double cx = (bx - ax) * (Beta / dt);
            double cy = (by - ay) * (Beta / dt);
            double rvx = vbx - vax + cx;
            double rvy = vby - vay + cy;

            double ix = -(k22 * rvx - k12 * rvy) / det;
            double iy = -(k11 * rvy - k12 * rvx) / det;

            if (!a.IsStatic)
            {
                a.Vx -= ix * a.InvMass; a.Vy -= iy * a.InvMass;
                a.W -= (rax * iy - ray * ix) * a.InvI;
            }

            if (!b.IsStatic)
            {
                b.Vx += ix * b.InvMass; b.Vy += iy * b.InvMass;
                b.W += (rbx * iy - rby * ix) * b.InvI;
            }

            if (j.MotorEnabled)
            {
                double relW = b.W - a.W;
                double kMotor = a.InvI + b.InvI;

                if (kMotor > 0)
                {
                    double impulse = -(relW - j.MotorSpeed) / kMotor;
                    double max = j.MaxMotorTorque * dt;
                    double accumulated=Math.Clamp(j.MotorImpulse+impulse,-max,max);
                    impulse=accumulated-j.MotorImpulse;
                    j.MotorImpulse=accumulated;

                    if (!a.IsStatic) a.W -= impulse * a.InvI;
                    if (!b.IsStatic) b.W += impulse * b.InvI;
                }
            }
        }
    }

    private void StoreWarm()
    {
        _warmNext.Clear();

        foreach (Contact c in _contacts)
        {
            _warmNext[WarmKey(c.A, c.B, 0)] = (c.P0.AccumN, c.P0.AccumT);

            if (c.Count > 1)
            {
                _warmNext[WarmKey(c.A, c.B, 1)] = (c.P1.AccumN, c.P1.AccumT);
            }
        }

        _warm.Clear();

        foreach (KeyValuePair<long, (double n, double t)> kv in _warmNext)
        {
            _warm[kv.Key] = kv.Value;
        }
    }

    private void SleepCheck(double dt)
    {
        for (int i = 0; i < Bodies.Count; i++)
        {
            Body2D b = Bodies[i];
            if (b.IsStatic || b.Sleeping) continue;

            if (i == GrabbedIndex)
            {
                b.SleepTimer = 0; 
                continue;
            }

            double lin = b.Vx * b.Vx + b.Vy * b.Vy;

            if (lin < SleepLinear * SleepLinear && Math.Abs(b.W) < SleepAngular)
            {
                b.SleepTimer += dt;

                if (b.SleepTimer > SleepDelay)
                {
                    b.Sleeping = true;
                    b.Vx = b.Vy = b.W = 0;
                }
            }
            else
            {
                b.SleepTimer = 0;
            }
        }
    }

    /// <summary>Body index under the point, topmost added first; -1 if none.</summary>
    public int PickBody(double x, double y)
    {
        for (int i = Bodies.Count - 1; i >= 0; i--)
        {
            Body2D b = Bodies[i];
            if (b.IsStatic) continue;

            if (b.Shape == Shape2D.Circle)
            {
                double dx = x - b.X, dy = y - b.Y;
                if (dx * dx + dy * dy <= b.Radius * b.Radius) return i;
            }
            else
            {
                (double lx, double ly) = WorldToLocal(b, x, y);
                if (Math.Abs(lx) <= b.HalfW && Math.Abs(ly) <= b.HalfH) return i;
            }
        }
        return -1;
    }

    public void Grab(int index, double x, double y)
    {
        GrabbedIndex = index;
        Body2D b = Bodies[index];
        (GrabLocalX, GrabLocalY) = WorldToLocal(b, x, y);
        DragTargetX = x;
        DragTargetY = y;
        b.Wake();
    }

    public void Release() => GrabbedIndex = -1;

    /// <summary>Radial blast: impulse falling off with distance, waking what it hits.</summary>
    public void Explode(double x, double y, double strength, double radius)
    {
        foreach (Body2D b in Bodies.Where(b => !b.IsStatic))
        {
            double dx = b.X - x, dy = b.Y - y;
            double d = Math.Sqrt(dx * dx + dy * dy);
            if (d > radius || d < 1e-6) continue;
            double f = strength * (1 - d / radius) / Math.Max(d, 0.35);
            b.Wake();
            b.Vx += dx * f * b.InvMass;
            b.Vy += dy * f * b.InvMass;
        }
    }

    /// <summary>Total kinetic energy - the self-test's conservation probe.</summary>
    public double KineticEnergy()
    {
        IEnumerable<double> energies = 
            from b in Bodies
            where !b.IsStatic
            let m = b.InvMass > 0 ? 1.0 / b.InvMass : 0
            let inertia = b.InvI > 0 ? 1.0 / b.InvI : 0
            select 0.5 * m * (b.Vx * b.Vx + b.Vy * b.Vy) + 0.5 * inertia * b.W * b.W;

        return energies.Sum();
    }
}
