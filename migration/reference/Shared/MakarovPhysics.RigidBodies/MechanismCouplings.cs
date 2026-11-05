using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>Tooth force of two ground-supported gears, applied where it really acts: one bilateral
/// row along the tooth normal through a fixed world point (the pitch point). The speed ratio, the
/// bearing loads and - for a tilted normal - the axial thrust all follow from the position of that
/// point and the direction of that normal; nothing is prescribed per gear type.
/// Spur pair: the normal is the common tangent of the pitch circles. Helical pair: the tangent tilted
/// towards the axis by the helix angle. Bevel pair: the tangent at the mean point of the pitch cones.
/// The pressure-angle (separating) component is not part of the row: it follows |Ft|, so it cannot be
/// a linear constraint force; a module that shows it computes it from <see cref="Force"/>.</summary>
public sealed class GearMeshConstraint : MechanismConstraint
{
    private readonly Row _row = new();
    private float _step;
    public HingeConstraint First { get; }
    public HingeConstraint Second { get; }
    public Vector3 PitchPoint { get; }
    public Vector3 Normal { get; }
    /// <summary>Moment arms of the normal about each shaft: the row is ArmA·ωA − ArmB·ωB (+ shaft travel).</summary>
    public float ArmA { get; }
    public float ArmB { get; }
    public double Phase { get; }
    /// <summary>Accumulated slip along the normal at the pitch point, m (zero for a closed mesh).</summary>
    public double Error => ArmA * First.Angle - ArmB * Second.Angle - Phase;
    /// <summary>Tooth force on A along <see cref="Normal"/> in the last substep, N; B receives it reversed.</summary>
    public float Force => _step > 0 ? _row.Impulse / _step : 0;
    internal override IReadOnlyList<MechanismConstraint> Supports => [First, Second];

    public GearMeshConstraint(HingeConstraint first, HingeConstraint second, Vector3 pitchPoint, Vector3 normal)
        : base(first.A, second.A)
    {
        if (first.B is not null || second.B is not null) throw new ArgumentException("The mesh couples two ground-supported shafts.");
        if (!Finite(pitchPoint)) throw new ArgumentException("Finite pitch point required.");
        First = first;
        Second = second;
        PitchPoint = pitchPoint;
        Normal = Axis(normal);
        ArmA = Vector3.Dot(first.AxisWorld, Vector3.Cross(pitchPoint - first.AnchorB, Normal));
        ArmB = Vector3.Dot(second.AxisWorld, Vector3.Cross(pitchPoint - second.AnchorB, Normal));
        if (Math.Abs(ArmA) < 1e-6f || Math.Abs(ArmB) < 1e-6f) throw new ArgumentException("The tooth normal must turn both shafts.");
        Phase = ArmA * first.Angle - ArmB * second.Angle;
    }

    public override void Prepare(float dt)
    {
        _step = dt;
        var rA = PitchPoint - A.Position;
        var rB = PitchPoint - B!.Position;
        _row.Set(A, B, Normal, Vector3.Cross(rA, Normal), -Normal, -Vector3.Cross(rB, Normal), .12f / dt * (float)Error);
    }

    public override void Solve() => _row.Solve();
}

/// <summary>Rack and pinion - and by the same kinematics a rope on a drum or a nut on a screw: the
/// travel x of a ground slider and the angle θ of a ground hinge obey x − Ratio·θ = phase (Ratio in
/// metres per radian, signed by the side of the mesh). With <see cref="Backlash"/> above zero the pair
/// is slack inside ±Backlash/2 and pushes only on whichever flank it reaches, as a real mesh does;
/// closing the gap is a plastic impact, so reversing the drive costs travel and a jolt, not energy.</summary>
public sealed class RackConstraint : MechanismConstraint
{
    private readonly Row _lower = new(), _upper = new();
    private float _step, _backlash;
    public SliderConstraint Rack { get; }
    public HingeConstraint Pinion { get; }
    public float Ratio { get; }
    public double Phase { get; }
    public float Backlash { get => _backlash; set => _backlash = Nonnegative(value, nameof(Backlash)); }
    /// <summary>Rack travel ahead of the pinion's pitch line, m; within ±Backlash/2 the pair is slack.</summary>
    public double Error => Rack.Position - Ratio * Pinion.Angle - Phase;
    /// <summary>Force the pinion put on the rack along the guide in the last substep, N.</summary>
    public float Force => _step > 0 ? (_lower.Impulse - _upper.Impulse) / _step : 0;
    /// <summary>True when a flank carried load in the last substep.</summary>
    public bool Engaged => _backlash <= 0 ? _lower.Impulse != 0 : _lower.Impulse > 0 || _upper.Impulse > 0;
    internal override IReadOnlyList<MechanismConstraint> Supports => [Rack, Pinion];

    public RackConstraint(SliderConstraint rack, HingeConstraint pinion, float ratio) : base(rack.A, pinion.A)
    {
        if (rack.B is not null || pinion.B is not null) throw new ArgumentException("The rack couples a ground guide and a ground shaft.");
        if (!float.IsFinite(ratio) || Math.Abs(ratio) < 1e-6f) throw new ArgumentOutOfRangeException(nameof(ratio));
        Rack = rack;
        Pinion = pinion;
        Ratio = ratio;
        Phase = rack.Position - ratio * pinion.Angle;
    }

    public override void Prepare(float dt)
    {
        _step = dt;
        var axis = Rack.AxisWorld;
        var va = axis;
        var wa = Vector3.Cross(Rack.AnchorA - A.Position, axis);
        var wb = -Ratio * Pinion.AxisWorld;
        float error = (float)Error;

        if (_backlash <= 0)
        {
            // no play: one bilateral row (the lower row carries both signs)
            _lower.Set(A, B, va, wa, Vector3.Zero, wb, .12f / dt * error);
            _upper.Set(A, B, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0);
            return;
        }

        // speculative one-sided rows: a flank still apart may only close the gap within this substep
        float half = _backlash / 2;
        float gapLower = error + half, gapUpper = half - error;
        _lower.Set(A, B, va, wa, Vector3.Zero, wb, Speculative(gapLower, dt), 0, float.PositiveInfinity);
        _upper.Set(A, B, -va, -wa, Vector3.Zero, -wb, Speculative(gapUpper, dt), 0, float.PositiveInfinity);
    }

    private static float Speculative(float gap, float dt) => gap > 0 ? gap / dt : .2f * gap / dt;

    public override void Solve()
    {
        _lower.Solve();
        _upper.Solve();
    }
}

/// <summary>Friction drive between two ground-supported shafts: a belt (Sense +1, both turn the same
/// way), friction wheels (Sense −1) or a plate clutch (equal radii, Sense +1). The faces stick while
/// the transmitted tangential force stays below <see cref="Capacity"/> and slip beyond it; the slip
/// work is counted in <see cref="DissipatedEnergy"/>. Velocity-level: a slip, once made, is not undone.</summary>
public sealed class FrictionCoupling : MechanismConstraint
{
    private readonly Row _row = new();
    private float _step, _before, _capacity;
    public HingeConstraint First { get; }
    public HingeConstraint Second { get; }
    public float RadiusA { get; }
    public float RadiusB { get; }
    public int Sense { get; }
    /// <summary>Largest tangential force the faces can carry, N (μ × clamp force for a clutch).</summary>
    public float Capacity { get => _capacity; set => _capacity = Nonnegative(value, nameof(Capacity)); }
    /// <summary>Surface speed of A minus that of B along the shared tangent, m/s.</summary>
    public float SlipSpeed => RadiusA * First.Speed - Sense * RadiusB * Second.Speed;
    /// <summary>Tangential force on A's face in the last substep, N (it opposes A's slip).</summary>
    public float Force => _step > 0 ? _row.Impulse / _step : 0;
    public bool Slipping => Math.Abs(Force) >= Capacity * .999f && Capacity > 0;
    public double DissipatedEnergy { get; private set; }
    internal override IReadOnlyList<MechanismConstraint> Supports => [First, Second];

    public FrictionCoupling(HingeConstraint first, HingeConstraint second, float radiusA, float radiusB, int sense) : base(first.A, second.A)
    {
        if (first.B is not null || second.B is not null) throw new ArgumentException("The coupling joins two ground-supported shafts.");
        if (!(radiusA > 0) || !(radiusB > 0) || !float.IsFinite(radiusA) || !float.IsFinite(radiusB)) throw new ArgumentOutOfRangeException(nameof(radiusA));
        if (sense is not (1 or -1)) throw new ArgumentOutOfRangeException(nameof(sense));
        First = first;
        Second = second;
        RadiusA = radiusA;
        RadiusB = radiusB;
        Sense = sense;
    }

    public override void Prepare(float dt)
    {
        _step = dt;
        _before = SlipSpeed;
        float cap = Capacity * dt;
        _row.Set(A, B, Vector3.Zero, First.AxisWorld * RadiusA, Vector3.Zero, -Second.AxisWorld * (Sense * RadiusB), 0, -cap, cap);
    }

    public override void Solve() => _row.Solve();
    public override void Finish(float dt) => DissipatedEnergy += Math.Max(0, -_row.Impulse * .5 * (_before + SlipSpeed));
}

/// <summary>A translating follower riding a rotating cam: the follower's slider travel may not fall
/// below the cam's lift, x ≥ Lift(θ). One-sided - the cam pushes and never pulls - so a follower thrown
/// faster than its spring can bring it back leaves the cam, flies, and lands again (a plastic touch).
/// Lift and its slope dLift/dθ are supplied by the owner and must be 2π-periodic in the hinge angle.</summary>
public sealed class CamFollowerContact : MechanismConstraint
{
    private readonly Row _row = new();
    private readonly Func<double, double> _lift, _slope;
    private float _step;
    public SliderConstraint Follower { get; }
    public HingeConstraint Cam { get; }
    /// <summary>Clearance between follower and cam along the guide, m (0 in contact, positive in flight).</summary>
    public double Gap => Follower.Position - _lift(Cam.Angle);
    /// <summary>Contact force the cam put on the follower along the guide in the last substep, N (never negative).</summary>
    public float Force => _step > 0 ? _row.Impulse / _step : 0;
    internal override IReadOnlyList<MechanismConstraint> Supports => [Follower, Cam];

    public CamFollowerContact(SliderConstraint follower, HingeConstraint cam, Func<double, double> lift, Func<double, double> slope)
        : base(follower.A, cam.A)
    {
        if (follower.B is not null || cam.B is not null) throw new ArgumentException("The contact joins a ground guide and a ground shaft.");
        ArgumentNullException.ThrowIfNull(lift);
        ArgumentNullException.ThrowIfNull(slope);
        Follower = follower;
        Cam = cam;
        _lift = lift;
        _slope = slope;
    }

    public override void Prepare(float dt)
    {
        _step = dt;
        var axis = Follower.AxisWorld;
        // Linearise about the middle of the step and take the lift change over it from the profile
        // itself: with the slope at the start alone, a fast and sharply curved program leaves the
        // roller ½·s''·(ω·dt)² inside the cam every step (2.8 mm at 17 rad/s, measured 14.10).
        double angle = Cam.Angle, turn = Cam.Speed * dt;
        double slope = _slope(angle + turn / 2);
        // clearance left at the end of the step if the row's own velocity stays zero
        double ahead = Gap - (_lift(angle + turn) - _lift(angle)) + slope * turn;
        _row.Set(A, B, axis, Vector3.Cross(Follower.AnchorA - A.Position, axis), Vector3.Zero, -(float)slope * Cam.AxisWorld, (float)(ahead / dt), 0, float.PositiveInfinity);
    }

    public override void Solve() => _row.Solve();
}

/// <summary>A pin fixed on A runs in a straight slot fixed on B (a Geneva pin, a block in a yoke): one
/// bilateral row along the slot's in-plane normal at the pin. The owner switches <see cref="Enabled"/>
/// as the pin enters and leaves the slot; disabled, the row carries nothing.</summary>
public sealed class PointOnLineConstraint : MechanismConstraint
{
    private readonly Row _row = new();
    private readonly Vector3 _pinA, _originB, _normalB;
    private float _step;
    public bool Enabled { get; set; } = true;
    public Vector3 Pin => A.Position + Vector3.Transform(_pinA, A.Rotation);
    public Vector3 Origin => B!.Position + Vector3.Transform(_originB, B.Rotation);
    public Vector3 NormalWorld => Vector3.Transform(_normalB, B!.Rotation);
    /// <summary>Distance of the pin from the slot's centre line, m.</summary>
    public float Distance => Vector3.Dot(Pin - Origin, NormalWorld);
    /// <summary>Force of the slot wall on the pin along the slot normal in the last substep, N.</summary>
    public float Force => _step > 0 ? _row.Impulse / _step : 0;

    public PointOnLineConstraint(RigidBody a, RigidBody b, Vector3 pin, Vector3 origin, Vector3 slotDirection, Vector3 planeNormal) : base(a, b)
    {
        if (!Finite(pin) || !Finite(origin)) throw new ArgumentException("Finite pin and slot required.");
        var normal = Vector3.Cross(Axis(planeNormal), Axis(slotDirection));
        if (normal.LengthSquared() < 1e-8f) throw new ArgumentException("The slot must lie in the plane of motion.");
        _pinA = Local(a, pin);
        _originB = Local(b, origin);
        _normalB = Direction(b, Vector3.Normalize(normal));
    }

    public override void Prepare(float dt)
    {
        _step = dt;
        if (!Enabled)
        {
            _row.Set(A, B, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0);
            return;
        }

        var n = NormalWorld;
        var p = Pin;
        _row.Set(A, B, n, Vector3.Cross(p - A.Position, n), -n, -Vector3.Cross(p - B!.Position, n), .2f / dt * Distance);
    }

    public override void Solve() => _row.Solve();
}
