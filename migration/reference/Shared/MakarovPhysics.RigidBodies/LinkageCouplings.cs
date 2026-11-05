using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>A point fixed on A stays in a plane fixed on B, or in the world when B is null: one bilateral
/// row along the plane's normal. A ball in a straight groove, a ball held in the cage of a constant-velocity
/// joint, a pin on a guide plate.</summary>
public sealed class PointOnPlaneConstraint : MechanismConstraint
{
    private readonly Row _row = new();
    private readonly Vector3 _pointA, _originB, _normalB;
    private float _step;
    public Vector3 Point => A.Position + Vector3.Transform(_pointA, A.Rotation);
    public Vector3 Origin => B is null ? _originB : B.Position + Vector3.Transform(_originB, B.Rotation);
    public Vector3 Normal => WorldDirection(B, _normalB);
    /// <summary>Signed distance of the point from the plane, m.</summary>
    public float Distance => Vector3.Dot(Point - Origin, Normal);
    /// <summary>Force the plane put on A along <see cref="Normal"/> in the last substep, N.</summary>
    public float Force => _step > 0 ? _row.Impulse / _step : 0;
    private float _correction = .2f;
    /// <summary>Share of the distance from the plane put right in each substep, up to 1.</summary>
    public float Correction { get => _correction; set => _correction = value > 0 && value <= 1 ? value : throw new ArgumentOutOfRangeException(nameof(Correction)); }

    public PointOnPlaneConstraint(RigidBody a, RigidBody? b, Vector3 point, Vector3 planePoint, Vector3 planeNormal) : base(a, b)
    {
        if (!Finite(point) || !Finite(planePoint)) throw new ArgumentException("Finite point and plane required.");
        _pointA = Local(a, point);
        _originB = Local(b, planePoint);
        _normalB = Direction(b, Axis(planeNormal));
    }

    public override void Prepare(float dt)
    {
        _carried = _step > 0 ? _row.Impulse * dt / _step : 0;
        _step = dt;
        var n = Normal;
        var p = Point;
        Vector3 wa = Vector3.Cross(p - A.Position, n), wb = B is null ? Vector3.Zero : -Vector3.Cross(p - B.Position, n);
        _row.Set(A, B, n, wa, -n, wb, (_correction * Distance + Curvature(n, wa, wb, dt)) / dt);
    }

    /// <summary>How far one substep at the present velocities takes the point off the plane beyond what
    /// the row's linear speed accounts for. The bodies turn exactly, so a point carried round curves off
    /// its tangent by ½·(ω×(ω×r))·h²: a shaper's lever pin against the ram's sliding slot left it by
    /// ½·L·ω²·h²·sin θ each substep and stood 1 mm out of it at 6 rad/s (measured 14.10). Fed into the
    /// bias, the row aims where the turn really takes the point.</summary>
    private float Curvature(Vector3 n, Vector3 wa, Vector3 wb, float dt)
    {
        static Vector3 Turned(RigidBody body, Vector3 local, float dt)
        {
            var w = body.AngularVelocity;
            float rate = w.Length();
            var rotation = rate * dt > 1e-7f ? Quaternion.Normalize(Quaternion.CreateFromAxisAngle(w / rate, rate * dt) * body.Rotation) : body.Rotation;
            return Vector3.Transform(local, rotation);
        }

        if (A.AngularVelocity == Vector3.Zero && (B is null || B.AngularVelocity == Vector3.Zero)) return 0;
        var point = A.Position + A.Velocity * dt + Turned(A, _pointA, dt);
        Vector3 origin = _originB, normal = _normalB;
        float linear = Vector3.Dot(n, A.Velocity) + Vector3.Dot(wa, A.AngularVelocity);
        if (B is not null)
        {
            origin = B.Position + B.Velocity * dt + Turned(B, _originB, dt);
            normal = Turned(B, _normalB, dt);
            linear += Vector3.Dot(-n, B.Velocity) + Vector3.Dot(wb, B.AngularVelocity);
        }

        return Vector3.Dot(point - origin, normal) - (Distance + linear * dt);
    }

    private float _carried;
    internal override void WarmStart() => _row.Begin(_carried);
    internal override void EqualityRows(List<Row> rows) => rows.Add(_row);
    public override void Solve() => _row.Solve();
}

/// <summary>Holds a body in its plane: no motion of its centre along the plane's normal and no turning
/// about the two directions in the plane; it may slide and turn in the plane. Three rows to the ground -
/// with <see cref="HingeConstraint.InPlane"/> joints, the one place where a planar linkage's bodies are
/// held in their plane.</summary>
public sealed class PlaneLock : MechanismConstraint
{
    private readonly Row[] _rows = [new(), new(), new()];
    private readonly Vector3 _normal, _normalA, _p, _q;
    private readonly float _level;
    private readonly float[] _carried = new float[3];
    private float _step;
    public Vector3 Normal => _normal;
    /// <summary>How far the body's centre has left its plane, m, and how far it has tilted out of it, rad.</summary>
    public float Offset => Vector3.Dot(A.Position, _normal) - _level;
    public float Tilt => Vector3.Cross(_normal, WorldDirection(A, _normalA)).Length();

    public PlaneLock(RigidBody body, Vector3 normal) : base(body, null)
    {
        _normal = Axis(normal);
        _normalA = Direction(body, _normal);
        _p = Perpendicular(_normal);
        _q = Vector3.Cross(_normal, _p);
        _level = Vector3.Dot(body.Position, _normal);
    }

    public override void Prepare(float dt)
    {
        float ratio = _step > 0 ? dt / _step : 0;
        for (int i = 0; i < 3; i++) _carried[i] = _rows[i].Impulse * ratio;
        _step = dt;
        var tilt = Vector3.Cross(WorldDirection(A, _normalA), _normal);
        _rows[0].Set(A, null, _normal, Vector3.Zero, Vector3.Zero, Vector3.Zero, .18f / dt * Offset);
        _rows[1].Set(A, null, Vector3.Zero, _p, Vector3.Zero, Vector3.Zero, -.18f / dt * Vector3.Dot(tilt, _p));
        _rows[2].Set(A, null, Vector3.Zero, _q, Vector3.Zero, Vector3.Zero, -.18f / dt * Vector3.Dot(tilt, _q));
    }

    internal override void WarmStart()
    {
        for (int i = 0; i < 3; i++) _rows[i].Begin(_carried[i]);
    }

    internal override void EqualityRows(List<Row> rows) => rows.AddRange(_rows);

    public override void Solve()
    {
        foreach (var row in _rows) row.Solve();
    }
}

/// <summary>A point of A held at a fixed distance from a point of B, or of the world when B is null: a
/// massless rod, a ball on the sphere of its race. One bilateral row along the line between the points.</summary>
public sealed class DistanceConstraint : MechanismConstraint
{
    private readonly Row _row = new();
    private readonly Vector3 _pointA, _pointB;
    private Vector3 _direction = Vector3.UnitX;
    private float _step;
    public float Length { get; }
    public Vector3 PointA => A.Position + Vector3.Transform(_pointA, A.Rotation);
    public Vector3 PointB => B is null ? _pointB : B.Position + Vector3.Transform(_pointB, B.Rotation);
    /// <summary>Actual distance minus the set length, m.</summary>
    public float Error => Vector3.Distance(PointA, PointB) - Length;
    /// <summary>Tension along the rod in the last substep, N (negative: compression).</summary>
    public float Force => _step > 0 ? -_row.Impulse / _step : 0;

    public DistanceConstraint(RigidBody a, RigidBody? b, Vector3 pointA, Vector3 pointB, float length = float.NaN) : base(a, b)
    {
        if (!Finite(pointA) || !Finite(pointB)) throw new ArgumentException("Finite points required.");
        _pointA = Local(a, pointA);
        _pointB = Local(b, pointB);
        Length = float.IsNaN(length) ? Vector3.Distance(pointA, pointB) : length;
        if (!(Length > 1e-6f) || !float.IsFinite(Length)) throw new ArgumentOutOfRangeException(nameof(length));
        var d = pointA - pointB;
        if (d.LengthSquared() > 1e-12f) _direction = Vector3.Normalize(d);
    }

    public override void Prepare(float dt)
    {
        _carried = _step > 0 ? _row.Impulse * dt / _step : 0;
        _step = dt;
        var pa = PointA;
        var pb = PointB;
        var d = pa - pb;
        float length = d.Length();
        if (length > 1e-9f) _direction = d / length;
        var n = _direction;
        _row.Set(A, B, n, Vector3.Cross(pa - A.Position, n), -n, B is null ? Vector3.Zero : -Vector3.Cross(pb - B.Position, n), .2f / dt * (length - Length));
    }

    private float _carried;
    internal override void WarmStart() => _row.Begin(_carried);
    internal override void EqualityRows(List<Row> rows) => rows.Add(_row);
    public override void Solve() => _row.Solve();
}

/// <summary>Moves a point of a body towards a target in a plane - a stylus led by a servo, a hand on a
/// handle - as an implicit spring of stiffness k with damping c, the target's own velocity fed forward,
/// and the force capped at MaxForce in any direction. With no spring (k = 0) it is a damper towards the
/// target velocity; with no spring and infinite damping at zero target velocity it is dry friction of
/// the point on the plane: it holds while the force needed stays under the cap and drags at the cap.
/// Only the two directions of the plane are driven.</summary>
public sealed class PointDrive : MechanismConstraint
{
    private readonly Vector3 _pointA, _u, _v;
    private Vector3 _r, _ru, _rv;
    private float _massU, _massV, _biasU, _biasV, _gamma, _cap, _lu, _lv, _step, _stiffness, _damping = float.PositiveInfinity, _maxForce = float.PositiveInfinity;
    public Vector3 Target { get; set; }
    public Vector3 TargetVelocity { get; set; }
    public float Stiffness { get => _stiffness; set => _stiffness = Nonnegative(value, nameof(Stiffness)); }
    public float Damping { get => _damping; set => _damping = value >= 0 && !float.IsNaN(value) ? value : throw new ArgumentOutOfRangeException(nameof(Damping)); }
    public float MaxForce { get => _maxForce; set => _maxForce = value >= 0 && !float.IsNaN(value) ? value : throw new ArgumentOutOfRangeException(nameof(MaxForce)); }
    public Vector3 Point => A.Position + Vector3.Transform(_pointA, A.Rotation);
    /// <summary>Force the drive put on the point in the last substep, world frame, N.</summary>
    public Vector3 Force => _step > 0 ? (_u * _lu + _v * _lv) / _step : Vector3.Zero;
    public bool Saturated => float.IsFinite(_maxForce) && _maxForce > 0 && Force.Length() >= _maxForce * .999f;

    public PointDrive(RigidBody body, Vector3 point, Vector3 planeNormal) : base(body, null)
    {
        if (!Finite(point)) throw new ArgumentException("Finite point required.");
        var n = Axis(planeNormal);
        _pointA = Local(body, point);
        _u = Perpendicular(n);
        _v = Vector3.Cross(n, _u);
        Target = point;
    }

    private float _carriedU, _carriedV;

    public override void Prepare(float dt)
    {
        float ratio = _step > 0 ? dt / _step : 0;
        _carriedU = _lu * ratio;
        _carriedV = _lv * ratio;
        _step = dt;
        _lu = _lv = 0;
        _r = Point - A.Position;
        _ru = Vector3.Cross(_r, _u);
        _rv = Vector3.Cross(_r, _v);
        float ku = A.InvMass + Vector3.Dot(_ru, A.InvInertiaWorld * _ru);
        float kv = A.InvMass + Vector3.Dot(_rv, A.InvInertiaWorld * _rv);
        float rate = 0;
        _gamma = 0;
        if (_stiffness > 0 || float.IsFinite(_damping))
        {
            // a damper alone (k = 0) is the limit of the implicit spring-damper: rate 0, softness 1/(h·c)
            var soft = Soft(_stiffness, float.IsFinite(_damping) ? _damping : 1e30f, dt);
            rate = soft.Rate;
            _gamma = soft.Gamma;
        }

        var error = _stiffness > 0 ? Point - Target : Vector3.Zero;
        _biasU = rate * Vector3.Dot(error, _u) - Vector3.Dot(TargetVelocity, _u);
        _biasV = rate * Vector3.Dot(error, _v) - Vector3.Dot(TargetVelocity, _v);
        _massU = ku > 1e-10f ? 1 / (ku + _gamma) : 0;
        _massV = kv > 1e-10f ? 1 / (kv + _gamma) : 0;
        _cap = float.IsFinite(_maxForce) ? _maxForce * dt : float.PositiveInfinity;
    }

    internal override void WarmStart()
    {
        if (A.IsStatic || (_massU == 0 && _massV == 0)) return;
        float nu = _carriedU, nv = _carriedV, length = MathF.Sqrt(nu * nu + nv * nv);
        if (!(length > 0) || !float.IsFinite(length)) return;
        if (length > _cap)
        {
            nu *= _cap / length;
            nv *= _cap / length;
        }

        _lu = nu;
        _lv = nv;
        var impulse = _u * nu + _v * nv;
        A.Velocity += impulse * A.InvMass;
        A.AngularVelocity += A.InvInertiaWorld * Vector3.Cross(_r, impulse);
    }

    public override void Solve()
    {
        if (A.IsStatic || (_massU == 0 && _massV == 0)) return;
        var velocity = A.Velocity + Vector3.Cross(A.AngularVelocity, _r);
        float wu = Vector3.Dot(velocity, _u) + _biasU + _gamma * _lu, wv = Vector3.Dot(velocity, _v) + _biasV + _gamma * _lv;
        float nu = _lu - wu * _massU, nv = _lv - wv * _massV;
        float length = MathF.Sqrt(nu * nu + nv * nv);
        if (length > _cap)
        {
            // At the cap the force must point against the slip. Each direction's own effective mass - a
            // point on a long bar is far lighter across the bar than along it - would settle on a force
            // against M·v instead, up to 35° off the slip of a pantograph's pen (measured 14.10); one
            // mass for both directions settles on −v.
            float mass = Math.Min(_massU, _massV);
            nu = _lu - wu * mass;
            nv = _lv - wv * mass;
            length = MathF.Sqrt(nu * nu + nv * nv);
            if (length > _cap)
            {
                nu *= _cap / length;
                nv *= _cap / length;
            }
        }

        var impulse = _u * (nu - _lu) + _v * (nv - _lv);
        _lu = nu;
        _lv = nv;
        A.Velocity += impulse * A.InvMass;
        A.AngularVelocity += A.InvInertiaWorld * Vector3.Cross(_r, impulse);
    }
}

/// <summary>A one-sided elastic stop on a ground slider: past <see cref="Contact"/> in the direction
/// <see cref="Side"/> the slider meets a spring of stiffness k and damping c - a specimen under a press
/// ram, a rubber buffer. Solved implicitly, so it holds its static deflection F/k exactly at any
/// stiffness, and it pushes but never pulls.</summary>
public sealed class ElasticStop : MechanismConstraint
{
    private readonly Row _row = new();
    private float _step, _stiffness, _damping, _contact;
    public SliderConstraint Slider { get; }
    /// <summary>+1: the spring is met as the slider position grows past Contact; −1: as it falls below.</summary>
    public int Side { get; }
    public float Contact { get => _contact; set => _contact = Valid(value, nameof(Contact)); }
    public float Stiffness { get => _stiffness; set => _stiffness = Valid(value, nameof(Stiffness)) > 0 ? value : throw new ArgumentOutOfRangeException(nameof(Stiffness)); }
    public float Damping { get => _damping; set => _damping = Nonnegative(value, nameof(Damping)); }
    /// <summary>How far the slider has pressed into the spring, m (negative: still clear of it).</summary>
    public float Compression => Side * (Slider.Position - _contact);
    /// <summary>Force of the spring on the slider in the last substep, N (never negative).</summary>
    public float Force => _step > 0 ? _row.Impulse / _step : 0;
    public double ElasticEnergy => .5 * _stiffness * Math.Pow(Math.Max(0, Compression), 2);
    internal override IReadOnlyList<MechanismConstraint> Supports => [Slider];

    public ElasticStop(SliderConstraint slider, float contact, int side, float stiffness, float damping = 0) : base(slider.A, null)
    {
        if (slider.B is not null) throw new ArgumentException("The stop holds a ground slider.");
        if (side is not (1 or -1)) throw new ArgumentOutOfRangeException(nameof(side));
        Slider = slider;
        Side = side;
        Contact = contact;
        Stiffness = stiffness;
        Damping = damping;
    }

    private float _carried;

    public override void Prepare(float dt)
    {
        _carried = _step > 0 ? _row.Impulse * dt / _step : 0;
        _step = dt;
        var axis = Slider.AxisWorld * Side;
        float compression = Compression, approach = Side * Slider.Speed;
        // a spring still out of reach this step carries nothing; one within reach is solved implicitly,
        // F = k·(compression at the end of the step) + c·speed, so a fast ram is caught within the step
        if (compression + Math.Max(0, approach) * dt * 2 < -1e-4f)
        {
            _row.Set(A, null, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0);
            return;
        }

        var r = Slider.AnchorA - A.Position;
        var arm = Vector3.Cross(r, axis);
        float inverseMass = A.InvMass + Vector3.Dot(arm, A.InvInertiaWorld * arm);
        // F = k·(c + β·h·s) + c_d·s with s the speed at the end of the step. β = 1 (backward Euler) is
        // stable at any stiffness but eats the energy of a bounce: a 2 kg ram dropped 5 cm on 2e4 N/m
        // reached 54 % of the compression its energy allows (ω·h = 0.83). β = 0 keeps the energy but
        // overshoots by 11 % there and is unstable past ω·h = 2. So β grows with ω·h: the bounce of a
        // spring with ω·h up to 2.5 stays within 10 % of its energy (91 % in the drop, measured 14.10),
        // a stiffer one is held stable and damped; the static deflection F/k is exact for any β.
        float wh2 = _stiffness * inverseMass * dt * dt;
        float beta = wh2 <= 1 ? .1f * wh2 : wh2 <= 4 ? .1f + .05f * (wh2 - 1) : Math.Max(.25f, .5f * (1 - 4 / wh2) + .05f);
        float d = beta * _stiffness * dt + _damping;
        if (!(inverseMass > 0)) return;
        _row.Set(A, null, -axis, -arm, Vector3.Zero, Vector3.Zero, -_stiffness / d * compression, 0, float.PositiveInfinity, 1 / (dt * d));
    }

    internal override void WarmStart() => _row.Begin(_carried);
    internal override void BoundedRows(List<Row> rows) => rows.Add(_row);
    public override void Solve() => _row.Solve();
}

/// <summary>Points carried by A - pawl tips, the tips of escape-wheel teeth - against a planar profile
/// carried by B - ratchet teeth, the faces of a pallet. Contacts are one-sided rows along the profile's
/// normal, made only for features within reach of a point in this substep and speculative, so a fast
/// point cannot pass through a thin tooth; they push and never pull, carry Coulomb friction along the
/// profile, and close plastically (no bounce). A point in a hollow between two faces gets a row for
/// each face. All motion is in one plane. The profile is a set of polygons, counter-clockwise seen
/// from the plane normal and solid inside; the points are rounded by a tip radius.</summary>
public sealed class ProfileContact : MechanismConstraint
{
    private const int MaxRows = 8;
    private readonly Vector3[] _points;
    private readonly Vector2[][] _polygons;
    private readonly Vector3 _normalB, _e1B, _e2B;
    private readonly Row[] _normal = Enumerable.Range(0, MaxRows).Select(_ => new Row()).ToArray();
    private readonly Row[] _friction = Enumerable.Range(0, MaxRows).Select(_ => new Row()).ToArray();
    private readonly int[] _owner = new int[MaxRows];
    private readonly float[] _slipBefore = new float[MaxRows];
    private readonly Vector3[] _tangent = new Vector3[MaxRows];
    private readonly List<int> _vertices = [];
    private int _rows;
    private float _step, _mu;
    public float TipRadius { get; }
    public float Friction { get => _mu; set => _mu = Nonnegative(value, nameof(Friction)); }
    /// <summary>When false the contact makes no rows: a pawl lifted off its teeth.</summary>
    public bool Enabled { get; set; } = true;
    /// <summary>Rows that carried force in the last substep.</summary>
    public int Touching { get; private set; }
    /// <summary>Total normal force and friction force in the last substep, N.</summary>
    public float NormalForce { get; private set; }
    public float FrictionForce { get; private set; }
    /// <summary>Normal impulse delivered in the last substep, N·s - the blow of an impact.</summary>
    public float NormalImpulse { get; private set; }
    /// <summary>Point that carried the largest normal force in the last substep, or −1.</summary>
    public int PeakPoint { get; private set; } = -1;
    /// <summary>Heat of friction so far, J.</summary>
    public double DissipatedEnergy { get; private set; }
    public int PointCount => _points.Length;

    /// <param name="points">Contact points in world coordinates at construction, carried by A.</param>
    /// <param name="polygons">Solid polygons in world coordinates at construction, carried by B, each
    /// counter-clockwise seen from <paramref name="planeNormal"/>.</param>
    public ProfileContact(RigidBody a, RigidBody b, IReadOnlyList<Vector3> points, IReadOnlyList<IReadOnlyList<Vector3>> polygons, Vector3 planeNormal, float tipRadius) : base(a, b)
    {
        if (points.Count == 0 || polygons.Count == 0) throw new ArgumentException("Points and a profile are required.");
        if (!(tipRadius >= 0) || !float.IsFinite(tipRadius)) throw new ArgumentOutOfRangeException(nameof(tipRadius));
        var n = Axis(planeNormal);
        var e1 = Perpendicular(n);
        var e2 = Vector3.Cross(n, e1);
        _normalB = Direction(b, n);
        _e1B = Direction(b, e1);
        _e2B = Direction(b, e2);
        _points = points.Select(p => Local(a, p)).ToArray();
        _polygons = polygons.Select(poly =>
        {
            if (poly.Count < 3) throw new ArgumentException("A polygon needs three corners.");
            return poly.Select(p => { var l = Local(b, p); return new Vector2(Vector3.Dot(l, _e1B), Vector3.Dot(l, _e2B)); }).ToArray();
        }).ToArray();
        TipRadius = tipRadius;
    }

    /// <summary>World position of contact point i.</summary>
    public Vector3 PointWorld(int i) => A.Position + Vector3.Transform(_points[i], A.Rotation);

    private Vector2 Plane(Vector3 world)
    {
        var l = Vector3.Transform(world - B!.Position, Quaternion.Conjugate(B.Rotation));
        return new Vector2(Vector3.Dot(l, _e1B), Vector3.Dot(l, _e2B));
    }

    private Vector3 World(Vector2 direction) => Vector3.Transform(_e1B * direction.X + _e2B * direction.Y, B!.Rotation);

    private static bool Inside(Vector2 p, Vector2[] poly)
    {
        bool inside = false;
        for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
        {
            Vector2 a = poly[j], b = poly[i];
            if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < a.X + (p.Y - a.Y) * (b.X - a.X) / (b.Y - a.Y)) inside = !inside;
        }

        return inside;
    }

    private static Vector2 Outward(Vector2 a, Vector2 b)
    {
        var ab = b - a;
        float length = ab.Length();
        // a counter-clockwise edge turned clockwise points out of the solid
        return length > 1e-9f ? new Vector2(ab.Y, -ab.X) / length : Vector2.UnitX;
    }

    private Vector3 RelativeVelocity(Vector3 at) =>
        A.Velocity + Vector3.Cross(A.AngularVelocity, at - A.Position) - B!.Velocity - Vector3.Cross(B.AngularVelocity, at - B.Position);

    private void AddRow(int point, Vector3 at, Vector2 normal2, float gap, float dt)
    {
        if (_rows >= MaxRows) return;
        var n = World(normal2);
        var rA = at - A.Position;
        var rB = at - B!.Position;
        int k = _rows++;
        _owner[k] = point;
        _normal[k].Set(A, B, n, Vector3.Cross(rA, n), -n, -Vector3.Cross(rB, n), StopBias(gap, 0, float.PositiveInfinity, dt), 0, float.PositiveInfinity);
        var t = Vector3.Normalize(Vector3.Cross(WorldDirection(B, _normalB), n));
        _friction[k].Set(A, B, t, Vector3.Cross(rA, t), -t, -Vector3.Cross(rB, t), 0, 0, 0);
        _tangent[k] = t;
        _slipBefore[k] = Vector3.Dot(RelativeVelocity(at), t);
    }

    public override void Prepare(float dt)
    {
        _step = dt;
        _rows = 0;
        if (!Enabled) return;
        for (int i = 0; i < _points.Length; i++)
        {
            var pw = PointWorld(i);
            var p = Plane(pw);
            // reach: the tip radius plus how far the point can travel relative to B in two substeps
            float reach = TipRadius + 2 * dt * RelativeVelocity(pw).Length() + 1e-3f;
            foreach (var poly in _polygons)
            {
                if (Inside(p, poly))
                {
                    // already in: push out along the nearest edge
                    float best = float.PositiveInfinity;
                    Vector2 normal = Vector2.UnitX;
                    for (int e = 0, j = poly.Length - 1; e < poly.Length; j = e++)
                    {
                        Vector2 a = poly[j], b = poly[e], ab = b - a;
                        float t = Math.Clamp(Vector2.Dot(p - a, ab) / Math.Max(1e-12f, ab.LengthSquared()), 0, 1);
                        float d = Vector2.Distance(p, a + ab * t);
                        if (d < best)
                        {
                            best = d;
                            normal = Outward(a, b);
                        }
                    }

                    AddRow(i, pw, normal, -best - TipRadius, dt);
                    continue;
                }

                _vertices.Clear();
                for (int e = 0, j = poly.Length - 1; e < poly.Length; j = e++)
                {
                    Vector2 a = poly[j], b = poly[e], ab = b - a;
                    float t = Vector2.Dot(p - a, ab) / Math.Max(1e-12f, ab.LengthSquared());
                    if (t > 0 && t < 1)
                    {
                        // the point faces the inside of this edge
                        var n = Outward(a, b);
                        float d = Vector2.Dot(p - a, n);
                        if (d > 0 && d < reach) AddRow(i, pw, n, d - TipRadius, dt);
                        continue;
                    }

                    // nearest to a corner: one row per corner, along the line from the corner to the point
                    int corner = t <= 0 ? j : e;
                    if (_vertices.Contains(corner)) continue;
                    var c = poly[corner];
                    float dc = Vector2.Distance(p, c);
                    if (dc >= reach || dc < 1e-7f) continue;
                    var prev = poly[(corner + poly.Length - 1) % poly.Length];
                    var next = poly[(corner + 1) % poly.Length];
                    var bisector = Outward(prev, c) + Outward(c, next);
                    var away = (p - c) / dc;
                    // only a corner that sticks out towards the point: a hollow corner is the two faces' business
                    bool convex = Vector2.Dot(Outward(prev, c), next - c) < 0;
                    if (!convex || Vector2.Dot(away, bisector) <= 0) continue;
                    // and only from beyond both its faces: a point still over a face rides that face's line,
                    // which runs through the corner. Seen from there, a corner the point will pass over
                    // within the substep looks, to a row that follows the line from it, like one it will
                    // run into - a tooth resting on a pallet's locking arc, ten chords long, was knocked
                    // back at every joint between them (measured 14.10)
                    if (Vector2.Dot(p - c, c - prev) < 0 || Vector2.Dot(p - c, next - c) > 0) continue;
                    _vertices.Add(corner);
                    AddRow(i, pw, away, dc - TipRadius, dt);
                }
            }
        }
    }

    /// <summary>The normal rows go to the block solve as stops: a tooth that lands on a pallet hung from a
    /// pin a metre above its bob, row by row, met the pendulum's whole swing as if it were free and sank a
    /// third of a millimetre in (measured 14.10). Friction, bounded by a normal impulse still being found,
    /// stays with the sweeps.</summary>
    internal override void BoundedRows(List<Row> rows)
    {
        for (int k = 0; k < _rows; k++) rows.Add(_normal[k]);
    }

    public override void Solve()
    {
        for (int k = 0; k < _rows; k++) _normal[k].Solve();
        if (_mu <= 0) return;
        for (int k = 0; k < _rows; k++)
        {
            float bound = _mu * _normal[k].Impulse;
            _friction[k].Bound(-bound, bound);
            _friction[k].Solve();
        }
    }

    public override void Finish(float dt)
    {
        float total = 0, friction = 0, peak = 0;
        int at = -1, touching = 0;
        for (int k = 0; k < _rows; k++)
        {
            float impulse = _normal[k].Impulse;
            total += impulse;
            friction += Math.Abs(_friction[k].Impulse);
            if (impulse > 0) touching++;
            if (impulse > peak)
            {
                peak = impulse;
                at = _owner[k];
            }

            if (_mu > 0 && _friction[k].Impulse != 0)
            {
                // friction heat: the impulse against the mean sliding speed of the step
                float after = Vector3.Dot(RelativeVelocity(PointWorld(_owner[k])), _tangent[k]);
                DissipatedEnergy += Math.Max(0, -_friction[k].Impulse * .5 * (_slipBefore[k] + after));
            }
        }

        Touching = touching;
        NormalImpulse = total;
        NormalForce = _step > 0 ? total / _step : 0;
        FrictionForce = _step > 0 ? friction / _step : 0;
        PeakPoint = at;
    }
}

/// <summary>A barrel cam: a follower on a ground guide parallel to the axis of a cam on a ground shaft,
/// its roller running in a groove x = s(θ) cut round the cam at radius R. The walls lean to the axis at
/// the pressure angle α, tan α = s'(θ)/R, and push along their normal; with a clearance between roller
/// and groove they are two one-sided rows, and the roller crosses the gap when the load reverses.
/// Coulomb friction acts along the wall, bounded by the load the walls carry. The rows are written in
/// the joints' own coordinates, like the screw thread a groove of constant slope would be. The lift
/// and its slope come from the owner and must be 2π-periodic in the hinge angle.</summary>
public sealed class GrooveConstraint : MechanismConstraint
{
    private readonly Row _minus = new(), _plus = new(), _friction = new();
    private readonly Func<double, double> _lift, _slope;
    private float _step, _mu, _clearance, _norm = 1, _slopeMid, _slipBefore, _slipEnd = float.NaN;
    public SliderConstraint Follower { get; }
    public HingeConstraint Cam { get; }
    public float Radius { get; }
    public float Friction { get => _mu; set => _mu = Nonnegative(value, nameof(Friction)); }
    /// <summary>Gap between roller and groove measured across the groove, m (0: a snug roller).</summary>
    public float Clearance { get => _clearance; set => _clearance = Nonnegative(value, nameof(Clearance)); }
    /// <summary>Follower ahead of the groove's centre line along the axis, m.</summary>
    public double Offset => Follower.Position - _lift(Cam.Angle);
    public double PressureAngle => Math.Atan(_slope(Cam.Angle) / Radius);
    /// <summary>Wall force on the roller along the wall normal in the last substep, N: positive from the
    /// wall behind it (pushing towards +x), negative from the wall ahead.</summary>
    public float NormalForce => _step > 0 ? (_minus.Impulse - _plus.Impulse) / _step : 0;
    public float FrictionForce => _step > 0 ? _friction.Impulse / _step : 0;
    /// <summary>Force of the groove on the follower along the guide and torque on the cam, last substep.</summary>
    public float AxialForce => _step > 0 ? ((_minus.Impulse - _plus.Impulse) + _friction.Impulse * _slopeMid / Radius) / (_norm * _step) : 0;
    public float CamTorque => _step > 0 ? (-(_minus.Impulse - _plus.Impulse) * _slopeMid + _friction.Impulse * Radius) / (_norm * _step) : 0;
    /// <summary>+1 or −1: the wall that carried the roller in the last substep; 0: the roller was free.</summary>
    public int Wall => _minus.Impulse > 0 ? 1 : _plus.Impulse > 0 ? -1 : 0;
    public double DissipatedEnergy { get; private set; }
    internal override IReadOnlyList<MechanismConstraint> Supports => [Follower, Cam];

    public GrooveConstraint(SliderConstraint follower, HingeConstraint cam, float radius, Func<double, double> lift, Func<double, double> slope) : base(follower.A, cam.A)
    {
        if (follower.B is not null || cam.B is not null) throw new ArgumentException("The groove joins a ground guide and a ground shaft.");
        if (!(radius > 0) || !float.IsFinite(radius)) throw new ArgumentOutOfRangeException(nameof(radius));
        ArgumentNullException.ThrowIfNull(lift);
        ArgumentNullException.ThrowIfNull(slope);
        Follower = follower;
        Cam = cam;
        Radius = radius;
        _lift = lift;
        _slope = slope;
    }

    /// <summary>Roller sliding along the wall, m/s: the follower's travel and the cam's surface, both
    /// projected on the groove's direction in the cam's own frame.</summary>
    private float Slip => (_slopeMid / Radius * Follower.Speed + Radius * Cam.Speed) / _norm;

    public override void Prepare(float dt)
    {
        _step = dt;
        var axis = Follower.AxisWorld;
        var turnAxis = Cam.AxisWorld;
        double angle = Cam.Angle, turn = Cam.Speed * dt;
        // the slope at the middle of the step and the lift's own change over it, as for the plate cam
        double slope = _slope(angle + turn / 2);
        double ahead = Offset - (_lift(angle + turn) - _lift(angle)) + slope * turn;
        _slopeMid = (float)slope;
        _norm = (float)Math.Sqrt(1 + slope * slope / (Radius * Radius));
        var r = Follower.AnchorA - A.Position;
        Vector3 va = axis / _norm, wa = Vector3.Cross(r, axis) / _norm, wb = -(float)slope / _norm * turnAxis;
        float across = (float)ahead / _norm;
        if (_clearance <= 0)
        {
            _minus.Set(A, B, va, wa, Vector3.Zero, wb, .2f / dt * across);
            _plus.Set(A, B, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0);
        }
        else
        {
            float gapMinus = _clearance / 2 + across, gapPlus = _clearance / 2 - across;
            _minus.Set(A, B, va, wa, Vector3.Zero, wb, StopBias(gapMinus, 0, float.PositiveInfinity, dt), 0, float.PositiveInfinity);
            _plus.Set(A, B, -va, -wa, Vector3.Zero, -wb, StopBias(gapPlus, 0, float.PositiveInfinity, dt), 0, float.PositiveInfinity);
        }

        // in the cam's frame the roller moves (dx, −R·dθ): along the groove (s'/R, −1), across it (1, s'/R)
        float k = (float)slope / (Radius * _norm);
        _friction.Set(A, B, axis * k, Vector3.Cross(r, axis) * k, Vector3.Zero, turnAxis * (Radius / _norm), 0, 0, 0);
        // the slip the last step ended with, not the one now: a push put on the follower between the steps
        // is not slip the friction worked against - counted in, it moved a sixth of the heat of a barrel
        // cam's rise into its return (measured 14.10)
        _slipBefore = float.IsNaN(_slipEnd) ? Slip : _slipEnd;
    }

    public override void Solve()
    {
        _minus.Solve();
        _plus.Solve();
        if (_mu <= 0) return;
        float bound = _mu * (_clearance <= 0 ? Math.Abs(_minus.Impulse) : _minus.Impulse + _plus.Impulse);
        _friction.Bound(-bound, bound);
        _friction.Solve();
    }

    public override void Finish(float dt)
    {
        float slip = Slip;
        DissipatedEnergy += Math.Max(0, -_friction.Impulse * .5 * (_slipBefore + slip));
        _slipEnd = slip;
    }
}
