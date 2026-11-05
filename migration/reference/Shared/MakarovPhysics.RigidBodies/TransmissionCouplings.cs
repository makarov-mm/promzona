using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>Turning angle of a body about an axis fixed in a frame - the ground, or a moving carrier -
/// followed across whole turns: <see cref="Update"/> adds the wrapped change since the last call.
/// Zero when made.</summary>
internal sealed class TurnAngle
{
    private readonly RigidBody _body;
    private readonly RigidBody? _frame;
    private readonly Vector3 _axis, _base, _reference;
    private float _last;
    public double Value { get; private set; }

    public TurnAngle(RigidBody body, RigidBody? frame, Vector3 axisWorld)
    {
        _body = body;
        _frame = frame;
        _axis = Vector3.Normalize(frame is null ? axisWorld : Vector3.Transform(axisWorld, Quaternion.Conjugate(frame.Rotation)));
        _base = Vector3.Normalize(Vector3.Cross(_axis, Math.Abs(_axis.Y) < .8f ? Vector3.UnitY : Vector3.UnitX));
        var baseWorld = frame is null ? _base : Vector3.Transform(_base, frame.Rotation);
        _reference = Vector3.Transform(baseWorld, Quaternion.Conjugate(body.Rotation));
        _last = Wrapped();
    }

    private float Wrapped()
    {
        var r = Vector3.Transform(_reference, _body.Rotation);
        if (_frame is not null) r = Vector3.Transform(r, Quaternion.Conjugate(_frame.Rotation));
        return MathF.Atan2(Vector3.Dot(_axis, Vector3.Cross(_base, r)), Vector3.Dot(_base, r));
    }

    public void Update()
    {
        float wrapped = Wrapped();
        Value += MathF.IEEERemainder(wrapped - _last, MathF.Tau);
        _last = wrapped;
    }
}

/// <summary>Tooth force of two gears whose shafts are fixed in one frame - the ground or a moving
/// carrier: a planet on its carrier against a sun or a ring coaxial with the carrier, the spider gears
/// of a differential in the turning case, a cycloid disc on its eccentric inside the ring of pins. One
/// bilateral row along the tooth normal through the pitch point, both fixed in the carrier's frame and
/// moving with it. Nothing about the train is prescribed: the speed relations of the whole train
/// (Willis' equation, 2ω₀ = ωL + ωR) follow from where these points sit and how the shafts are held.
/// Error is the slip at the pitch point accumulated from the turns of both gears relative to the
/// carrier, ArmA·φA − ArmB·φB; the row's velocity is its exact derivative. The lever arms run from each
/// shaft's support - the pin on the carrier, or the ground anchor - not from the gear's centre: a gear
/// carried round drifts out of its orbit by the straight position step, and arms from the drifted
/// centre turned that drift into a steady slip the error then kept fighting, a force doing work: 0.8 %
/// of the power of a planetary train at 6 rad/s, 0.02 % with the arms from the supports (measured 14.10).</summary>
public sealed class CarrierMeshConstraint : MechanismConstraint
{
    private readonly Row _row = new();
    private readonly Vector3 _point, _normal;
    private readonly TurnAngle _turnA, _turnB;
    private float _step;
    public HingeConstraint First { get; }
    public HingeConstraint Second { get; }
    public RigidBody? Carrier { get; }
    /// <summary>Moment arms of the normal about each gear's axis (signed): the row is ArmA·φ̇A − ArmB·φ̇B.</summary>
    public float ArmA { get; }
    public float ArmB { get; }
    public Vector3 PitchPoint => Carrier is null ? _point : Carrier.Position + Vector3.Transform(_point, Carrier.Rotation);
    public Vector3 Normal => Carrier is null ? _normal : Vector3.Transform(_normal, Carrier.Rotation);
    /// <summary>Turn of each gear relative to the carrier since the mesh was made, rad.</summary>
    public double TurnA => _turnA.Value;
    public double TurnB => _turnB.Value;
    /// <summary>Accumulated slip along the normal at the pitch point, m (zero for a closed mesh).</summary>
    public double Error => ArmA * _turnA.Value - ArmB * _turnB.Value;
    /// <summary>Tooth force on A along <see cref="Normal"/> in the last substep, N; B receives it reversed.</summary>
    public float Force => _step > 0 ? _row.Impulse / _step : 0;
    internal override IReadOnlyList<MechanismConstraint> Supports => [First, Second];

    public CarrierMeshConstraint(HingeConstraint first, HingeConstraint second, RigidBody? carrier, Vector3 pitchPoint, Vector3 normal)
        : base(first.A, second.A)
    {
        if (!Finite(pitchPoint)) throw new ArgumentException("Finite pitch point required.");
        if (carrier is not null && (carrier == first.A || carrier == second.A)) throw new ArgumentException("The carrier is not one of the gears.");
        var n = Axis(normal);
        First = first;
        Second = second;
        Carrier = carrier;
        _point = Local(carrier, pitchPoint);
        _normal = Direction(carrier, n);
        ArmA = Vector3.Dot(first.AxisWorld, Vector3.Cross(pitchPoint - first.AnchorA, n));
        ArmB = Vector3.Dot(second.AxisWorld, Vector3.Cross(pitchPoint - second.AnchorA, n));
        if (Math.Abs(ArmA) < 1e-6f || Math.Abs(ArmB) < 1e-6f) throw new ArgumentException("The tooth normal must turn both gears.");
        _turnA = new TurnAngle(first.A, carrier, first.AxisWorld);
        _turnB = new TurnAngle(second.A, carrier, second.AxisWorld);
    }

    public override void Prepare(float dt)
    {
        _step = dt;
        var p = PitchPoint;
        var n = Normal;
        _row.Set(A, B, n, Vector3.Cross(p - First.AnchorB, n), -n, -Vector3.Cross(p - Second.AnchorB, n), .12f / dt * (float)Error);
    }

    public override void Solve() => _row.Solve();

    public override void Finish(float dt)
    {
        _turnA.Update();
        _turnB.Update();
    }
}

/// <summary>A screw thread between two ground-supported joints, each a hinge or a slider: a nut on a
/// lead screw, a worm against its wheel. At the contact point the flank pushes along its normal m and the
/// faces slide along the helix h against Coulomb friction, at most μ·|N|. Both rows are written in the
/// joints' own coordinates - the speed of each face along m or h per unit joint speed - so the thread
/// puts an axial force on a slider and a torque on a hinge and nothing else. Whether a load can drive the
/// screw back is not a switch: it follows from the lead angle against μ, through the two rows.
/// The flank is either rigid - with an optional play between the two flanks, closed as a plastic touch
/// - or elastic: two nut halves of stiffness k each, pressed against each other by a preload (no
/// play, twice the stiffness until one half unloads) or apart by a clearance. The halves are never
/// solved against each other: while both are pressed they are one spring of 2k, otherwise the one that
/// is pressed is solved alone, and a half still apart closes its gap as a plastic touch, so a stiff nut
/// neither fights itself in the sweeps nor bounces off its play. Friction is bounded by the load both
/// halves carry, so a preloaded nut drags even when no force passes through it.</summary>
public sealed class ThreadConstraint : MechanismConstraint
{
    private readonly Row _lower = new(), _upper = new(), _friction = new();
    private float _step, _mu, _stiffness = float.PositiveInfinity, _damping, _clearance, _preload, _slipBefore, _elasticLoad;
    public MechanismConstraint First { get; }
    public MechanismConstraint Second { get; }
    public Vector3 ContactPoint { get; }
    /// <summary>Flank normal m; <see cref="NormalForce"/> pushes A's face along it.</summary>
    public Vector3 Normal { get; }
    /// <summary>Direction of the thread h at the contact, perpendicular to m: the faces slide along it.</summary>
    public Vector3 Helix { get; }
    /// <summary>Speed of each face along m per unit speed of its joint (m/rad for a hinge, 1 for a slider).</summary>
    public float ArmA { get; }
    public float ArmB { get; }
    /// <summary>The same along the helix h.</summary>
    public float HelixArmA { get; }
    public float HelixArmB { get; }
    public double Phase { get; }
    public float Friction { get => _mu; set => _mu = Nonnegative(value, nameof(Friction)); }
    /// <summary>Stiffness of each flank (nut half) along m, N/m; +∞ is a rigid flank.</summary>
    public float Stiffness { get => _stiffness; set => _stiffness = float.IsPositiveInfinity(value) || (float.IsFinite(value) && value > 0) ? value : throw new ArgumentOutOfRangeException(nameof(Stiffness)); }
    public float Damping { get => _damping; set => _damping = Nonnegative(value, nameof(Damping)); }
    /// <summary>Total play between the two flanks along m, m.</summary>
    public float Clearance { get => _clearance; set => _clearance = Nonnegative(value, nameof(Clearance)); }
    /// <summary>Force each elastic half carries at rest, N (ignored for a rigid flank).</summary>
    public float Preload { get => _preload; set => _preload = Nonnegative(value, nameof(Preload)); }
    public bool Elastic => !float.IsPositiveInfinity(_stiffness);
    /// <summary>Displacement of A's face along m ahead of the ideal thread, m.</summary>
    public double Error => ArmA * Coordinate(First) - ArmB * Coordinate(Second) - Phase;
    /// <summary>Net flank force on A's face along m in the last substep, N; B receives it reversed.</summary>
    public float NormalForce => _step > 0 ? (_lower.Impulse - _upper.Impulse) / _step : 0;
    /// <summary>Sum of what both flanks carry, N - a preloaded nut carries load even when the net is zero.</summary>
    public float FlankLoad => _step > 0 ? Math.Max(_elasticLoad, (Math.Abs(_lower.Impulse) + Math.Abs(_upper.Impulse)) / _step) : 0;
    /// <summary>Friction force on A's face along h in the last substep, N.</summary>
    public float FrictionForce => _step > 0 ? _friction.Impulse / _step : 0;
    /// <summary>Generalized force of the thread on A's joint: N along a slider, N·m about a hinge.</summary>
    public float LoadA => ArmA * NormalForce + HelixArmA * FrictionForce;
    public float LoadB => -(ArmB * NormalForce + HelixArmB * FrictionForce);
    /// <summary>Sliding speed of A's face over B's along h, m/s.</summary>
    public float SlipSpeed => HelixArmA * Rate(First) - HelixArmB * Rate(Second);
    public double DissipatedEnergy { get; private set; }
    /// <summary>Energy stored in the elastic halves beyond their rest preload, J.</summary>
    public double ElasticEnergy
    {
        get
        {
            if (!Elastic) return 0;
            double offset = _preload / _stiffness - _clearance / 2, e = Error;
            double lower = Math.Max(0, offset - e), upper = Math.Max(0, offset + e), rest = Math.Max(0, offset);
            return .5 * _stiffness * (lower * lower + upper * upper - 2 * rest * rest);
        }
    }
    internal override IReadOnlyList<MechanismConstraint> Supports => [First, Second];

    public ThreadConstraint(MechanismConstraint first, MechanismConstraint second, Vector3 contactPoint, Vector3 normal, Vector3 helix)
        : base(first.A, second.A)
    {
        if (!Finite(contactPoint)) throw new ArgumentException("Finite contact point required.");
        if (first.B is not null || second.B is not null) throw new ArgumentException("The thread joins two ground-supported joints.");
        var m = Axis(normal);
        var h = Axis(helix);
        if (Math.Abs(Vector3.Dot(m, h)) > 1e-4f) throw new ArgumentException("The helix must be perpendicular to the flank normal.");
        First = first;
        Second = second;
        ContactPoint = contactPoint;
        Normal = m;
        Helix = h;
        ArmA = Arm(first, contactPoint, m);
        ArmB = Arm(second, contactPoint, m);
        HelixArmA = Arm(first, contactPoint, h);
        HelixArmB = Arm(second, contactPoint, h);
        if (Math.Abs(ArmA) < 1e-6f || Math.Abs(ArmB) < 1e-6f) throw new ArgumentException("The flank normal must move both joints.");
        Phase = ArmA * Coordinate(first) - ArmB * Coordinate(second);
    }

    private static float Arm(MechanismConstraint joint, Vector3 point, Vector3 direction) => joint switch
    {
        HingeConstraint hinge => Vector3.Dot(hinge.AxisWorld, Vector3.Cross(point - hinge.AnchorB, direction)),
        SliderConstraint slider => Vector3.Dot(slider.AxisWorld, direction),
        _ => throw new ArgumentException("A thread joins hinges and sliders."),
    };

    private static double Coordinate(MechanismConstraint joint) => joint switch
    {
        HingeConstraint hinge => hinge.Angle,
        SliderConstraint slider => slider.Position,
        _ => 0,
    };

    private static float Rate(MechanismConstraint joint) => joint switch
    {
        HingeConstraint hinge => hinge.Speed,
        SliderConstraint slider => slider.Speed,
        _ => 0,
    };

    /// <summary>Jacobian of a joint's rate scaled by an arm: linear along a slider, angular about a hinge.</summary>
    private static (Vector3 Linear, Vector3 Angular) Piece(MechanismConstraint joint, float arm) => joint is SliderConstraint slider
        ? (slider.AxisWorld * arm, Vector3.Zero)
        : (Vector3.Zero, ((HingeConstraint)joint).AxisWorld * arm);

    private static float Speculative(float gap, float dt) => gap > 0 ? gap / dt : .2f * gap / dt;

    public override void Prepare(float dt)
    {
        _step = dt;
        var (va, wa) = Piece(First, ArmA);
        var (vb, wb) = Piece(Second, -ArmB);
        float e = (float)Error;
        _elasticLoad = 0;
        if (!Elastic)
        {
            if (_clearance <= 0)
            {
                _lower.Set(A, B, va, wa, vb, wb, .12f / dt * e);
                _upper.Set(A, B, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0);
            }
            else
            {
                // speculative one-sided flanks: one still apart may only close its gap within this substep
                float half = _clearance / 2;
                _lower.Set(A, B, va, wa, vb, wb, Speculative(e + half, dt), 0, float.PositiveInfinity);
                _upper.Set(A, B, -va, -wa, -vb, -wb, Speculative(half - e, dt), 0, float.PositiveInfinity);
            }
        }
        else
        {
            // the lower half is compressed by offset − e and pushes A along +m, the upper by offset + e and
            // pushes it back; each pushes only while compressed
            float offset = _preload / _stiffness - _clearance / 2, lower = offset - e, upper = offset + e;
            _elasticLoad = _stiffness * (Math.Max(0, lower) + Math.Max(0, upper));
            if (lower > 0 && upper > 0)
            {
                // both pressed: one spring of 2k about e = 0
                var (rate, softness) = Soft(2 * _stiffness, 2 * _damping, dt);
                _lower.Set(A, B, va, wa, vb, wb, rate * e, float.NegativeInfinity, float.PositiveInfinity, softness);
                _upper.Set(A, B, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0);
            }
            else
            {
                // a pressed half alone is a spring; a half still apart may only close its gap in this substep
                var (rate, softness) = Soft(_stiffness, _damping, dt);
                if (lower > 0) _lower.Set(A, B, va, wa, vb, wb, -rate * lower, 0, float.PositiveInfinity, softness);
                else _lower.Set(A, B, va, wa, vb, wb, -lower / dt, 0, float.PositiveInfinity);
                if (upper > 0) _upper.Set(A, B, -va, -wa, -vb, -wb, -rate * upper, 0, float.PositiveInfinity, softness);
                else _upper.Set(A, B, -va, -wa, -vb, -wb, -upper / dt, 0, float.PositiveInfinity);
            }
        }

        var (fa, ga) = Piece(First, HelixArmA);
        var (fb, gb) = Piece(Second, -HelixArmB);
        _friction.Set(A, B, fa, ga, fb, gb, 0, 0, 0);
        _slipBefore = SlipSpeed;
    }

    public override void Solve()
    {
        _lower.Solve();
        _upper.Solve();
        float limit = _mu * Math.Max(_elasticLoad * _step, Math.Abs(_lower.Impulse) + Math.Abs(_upper.Impulse));
        _friction.Bound(-limit, limit);
        _friction.Solve();
    }

    public override void Finish(float dt) => DissipatedEnergy += Math.Max(0, -_friction.Impulse * .5 * (_slipBefore + SlipSpeed));
}

/// <summary>Rotations of two bodies about parallel axes held in a ratio, ψA = Ratio·ψB (+ the angles at
/// which it was made), each angle being the body's absolute turn about the world axis: the pins of a
/// cycloid disc in the holes of the output flange (ratio 1 - the disc's centre orbits, its turn is the
/// output's), the cup of a strain-wave gear (ratio −1/N between wave generator and flexspline). Rigid,
/// rigid with play between two stops, or elastic with stiffness K (N·m/rad on A's angle) and damping.
/// Pure torque: the coupling puts no force on either body.</summary>
public sealed class RotationCoupling : MechanismConstraint
{
    private readonly Row _lower = new(), _upper = new();
    private readonly TurnAngle _turnA, _turnB;
    private float _step, _stiffness = float.PositiveInfinity, _damping, _clearance;
    public Vector3 AxisWorld { get; }
    public float Ratio { get; }
    public float Stiffness { get => _stiffness; set => _stiffness = float.IsPositiveInfinity(value) || (float.IsFinite(value) && value > 0) ? value : throw new ArgumentOutOfRangeException(nameof(Stiffness)); }
    public float Damping { get => _damping; set => _damping = Nonnegative(value, nameof(Damping)); }
    /// <summary>Total angular play of A between the two stops, rad.</summary>
    public float Clearance { get => _clearance; set => _clearance = Nonnegative(value, nameof(Clearance)); }
    public bool Elastic => !float.IsPositiveInfinity(_stiffness);
    public double TurnA => _turnA.Value;
    public double TurnB => _turnB.Value;
    /// <summary>Turn of A ahead of Ratio·ψB, rad: the wind-up of an elastic coupling, the side of the play.</summary>
    public double Error => _turnA.Value - Ratio * _turnB.Value;
    /// <summary>Torque on A about the axis in the last substep, N·m; B receives −Ratio times it.</summary>
    public float Torque => _step > 0 ? (_lower.Impulse - _upper.Impulse) / _step : 0;
    public bool Engaged => _lower.Impulse != 0 || _upper.Impulse != 0;
    /// <summary>Energy in an elastic coupling, J: ½K·(wind-up beyond the play)².</summary>
    public double ElasticEnergy
    {
        get
        {
            if (!Elastic) return 0;
            double over = Math.Max(0, Math.Abs(Error) - _clearance / 2);
            return .5 * _stiffness * over * over;
        }
    }

    public RotationCoupling(RigidBody a, RigidBody b, Vector3 axis, float ratio) : base(a, b)
    {
        if (!float.IsFinite(ratio) || Math.Abs(ratio) < 1e-6f) throw new ArgumentOutOfRangeException(nameof(ratio));
        AxisWorld = Axis(axis);
        Ratio = ratio;
        _turnA = new TurnAngle(a, null, AxisWorld);
        _turnB = new TurnAngle(b, null, AxisWorld);
    }

    private static float Speculative(float gap, float dt) => gap > 0 ? gap / dt : .2f * gap / dt;

    public override void Prepare(float dt)
    {
        _step = dt;
        var n = AxisWorld;
        var wb = -Ratio * n;
        float e = (float)Error, half = _clearance / 2;
        if (!Elastic)
        {
            if (_clearance <= 0)
            {
                _lower.Set(A, B, Vector3.Zero, n, Vector3.Zero, wb, .12f / dt * e);
                _upper.Set(A, B, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0);
            }
            else
            {
                _lower.Set(A, B, Vector3.Zero, n, Vector3.Zero, wb, Speculative(e + half, dt), 0, float.PositiveInfinity);
                _upper.Set(A, B, Vector3.Zero, -n, Vector3.Zero, -wb, Speculative(half - e, dt), 0, float.PositiveInfinity);
            }

            return;
        }

        var (rate, softness) = Soft(_stiffness, _damping, dt);
        if (_clearance <= 0)
        {
            _lower.Set(A, B, Vector3.Zero, n, Vector3.Zero, wb, rate * e, float.NegativeInfinity, float.PositiveInfinity, softness);
            _upper.Set(A, B, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0);
        }
        else
        {
            _lower.Set(A, B, Vector3.Zero, n, Vector3.Zero, wb, rate * (e + half), 0, float.PositiveInfinity, softness);
            _upper.Set(A, B, Vector3.Zero, -n, Vector3.Zero, -wb, rate * (half - e), 0, float.PositiveInfinity, softness);
        }
    }

    public override void Solve()
    {
        _lower.Solve();
        _upper.Solve();
    }

    public override void Finish(float dt)
    {
        _turnA.Update();
        _turnB.Update();
    }
}

/// <summary>A roller pressed on the face of a turning disc - the friction variator. At contact radius r
/// (signed: across the centre the roller turns the other way) the faces roll while the tangential force
/// stays below μ·N and slip beyond it; the slip work is counted. No slip means r·ωdisc = R·ωroller.
/// Velocity-level, like <see cref="FrictionCoupling"/>: a slip, once made, is not undone.</summary>
public sealed class DiscRollerContact : MechanismConstraint
{
    private readonly Row _row = new();
    private float _step, _before, _radius, _mu, _press;
    public HingeConstraint Disc { get; }
    public HingeConstraint Roller { get; }
    public float RollerRadius { get; }
    /// <summary>Distance of the contact from the disc's axis, m (signed).</summary>
    public float ContactRadius { get => _radius; set => _radius = Valid(value, nameof(ContactRadius)); }
    public float Friction { get => _mu; set => _mu = Nonnegative(value, nameof(Friction)); }
    /// <summary>Force pressing the roller on the disc, N.</summary>
    public float PressForce { get => _press; set => _press = Nonnegative(value, nameof(PressForce)); }
    public float Capacity => _mu * _press;
    /// <summary>Surface speed of the disc minus that of the roller at the contact, m/s.</summary>
    public float SlipSpeed => _radius * Disc.Speed - RollerRadius * Roller.Speed;
    /// <summary>Tangential force on the disc's face in the last substep, N (it opposes the disc's slip).</summary>
    public float Force => _step > 0 ? _row.Impulse / _step : 0;
    public bool Slipping => Capacity > 0 && Math.Abs(Force) >= Capacity * .999f;
    public double DissipatedEnergy { get; private set; }
    internal override IReadOnlyList<MechanismConstraint> Supports => [Disc, Roller];

    public DiscRollerContact(HingeConstraint disc, HingeConstraint roller, float rollerRadius) : base(disc.A, roller.A)
    {
        if (disc.B is not null || roller.B is not null) throw new ArgumentException("The contact joins two ground-supported shafts.");
        if (!(rollerRadius > 0) || !float.IsFinite(rollerRadius)) throw new ArgumentOutOfRangeException(nameof(rollerRadius));
        Disc = disc;
        Roller = roller;
        RollerRadius = rollerRadius;
    }

    public override void Prepare(float dt)
    {
        _step = dt;
        _before = SlipSpeed;
        float cap = Capacity * dt;
        _row.Set(A, B, Vector3.Zero, Disc.AxisWorld * _radius, Vector3.Zero, -Roller.AxisWorld * RollerRadius, 0, -cap, cap);
    }

    public override void Solve() => _row.Solve();
    public override void Finish(float dt) => DissipatedEnergy += Math.Max(0, -_row.Impulse * .5 * (_before + SlipSpeed));
}

/// <summary>A roller-chain sprocket: z seats of pitch p on the pitch circle R = p / (2 sin(π/z)) of a
/// body turning in a hinge; seat 0 lies along the given reference direction across the axis.</summary>
public sealed class Sprocket
{
    private readonly Vector3[] _seats;
    public HingeConstraint Shaft { get; }
    public int Teeth { get; }
    public float Pitch { get; }
    public float PitchRadius => Pitch / (2 * MathF.Sin(MathF.PI / Teeth));
    public Vector3 Centre => Shaft.AnchorA;

    public Sprocket(HingeConstraint shaft, int teeth, float pitch, Vector3 reference)
    {
        ArgumentNullException.ThrowIfNull(shaft);
        if (teeth < 6) throw new ArgumentOutOfRangeException(nameof(teeth));
        if (!(pitch > 0) || !float.IsFinite(pitch)) throw new ArgumentOutOfRangeException(nameof(pitch));
        Shaft = shaft;
        Teeth = teeth;
        Pitch = pitch;
        var axis = shaft.AxisWorld;
        var u = reference - axis * Vector3.Dot(reference, axis);
        if (u.LengthSquared() < 1e-8f) throw new ArgumentException("The reference must cross the axis.");
        u = Vector3.Normalize(u);
        var v = Vector3.Cross(axis, u);
        var body = shaft.A;
        var centre = shaft.AnchorA;
        _seats = new Vector3[teeth];
        for (int k = 0; k < teeth; k++)
        {
            float t = k * MathF.Tau / teeth;
            var world = centre + (u * MathF.Cos(t) + v * MathF.Sin(t)) * PitchRadius;
            _seats[k] = Vector3.Transform(world - body.Position, Quaternion.Conjugate(body.Rotation));
        }
    }

    /// <summary>World position of seat k (any integer; seats repeat every z).</summary>
    public Vector3 Seat(int k)
    {
        var body = Shaft.A;
        return body.Position + Vector3.Transform(_seats[((k % Teeth) + Teeth) % Teeth], body.Rotation);
    }
}

/// <summary>One free span of a roller chain: the straight run of n links from the roller seated on one
/// sprocket to the roller seated on the other. It pulls and never pushes (a chain buckles), stretches
/// with stiffness k when pulled beyond n·p, and its ends move from roller to roller as rollers seat and
/// leave: a free roller seats when it reaches the line of the span, a seated one leaves when the link
/// behind it comes into line. The span therefore leaves each sprocket from the corners of its pitch
/// polygon, not from the pitch circle, and the speed it carries rises and falls once per tooth - the
/// chordal effect - with nothing prescribed. The run itself is massless.</summary>
public sealed class ChainSpanConstraint : MechanismConstraint
{
    private readonly Row _row = new();
    private float _step, _stiffness, _damping;
    public Sprocket First { get; }
    public Sprocket Second { get; }
    /// <summary>Seat of the span's end roller on each sprocket.</summary>
    public int SeatFirst { get; private set; }
    public int SeatSecond { get; private set; }
    /// <summary>Seat index step from an end roller towards the span (+1 or −1) on each sprocket.</summary>
    public int TowardFirst { get; }
    public int TowardSecond { get; }
    /// <summary>Links in the span; rollers seated and released since the span was made.</summary>
    public int Links { get; private set; }
    public int Seated { get; private set; }
    public int Released { get; private set; }
    public float Stiffness { get => _stiffness; set => _stiffness = float.IsPositiveInfinity(value) || (float.IsFinite(value) && value > 0) ? value : throw new ArgumentOutOfRangeException(nameof(Stiffness)); }
    public float Damping { get => _damping; set => _damping = Nonnegative(value, nameof(Damping)); }
    public Vector3 EndFirst => First.Seat(SeatFirst);
    public Vector3 EndSecond => Second.Seat(SeatSecond);
    public float Length => Vector3.Distance(EndFirst, EndSecond);
    public float RestLength => Links * First.Pitch;
    /// <summary>Tension of the span in the last substep, N.</summary>
    public float Tension => _step > 0 ? _row.Impulse / _step : 0;
    public double ElasticEnergy
    {
        get
        {
            if (float.IsPositiveInfinity(_stiffness)) return 0;
            double over = Math.Max(0, Length - RestLength);
            return .5 * _stiffness * over * over;
        }
    }
    internal override IReadOnlyList<MechanismConstraint> Supports => [First.Shaft, Second.Shaft];

    public ChainSpanConstraint(Sprocket first, Sprocket second, int seatFirst, int seatSecond, int links)
        : base(first.Shaft.A, second.Shaft.A)
    {
        if (Math.Abs(first.Pitch - second.Pitch) > 1e-6f * first.Pitch) throw new ArgumentException("One chain, one pitch.");
        if (links < 1) throw new ArgumentOutOfRangeException(nameof(links));
        First = first;
        Second = second;
        SeatFirst = seatFirst;
        SeatSecond = seatSecond;
        Links = links;
        _stiffness = float.PositiveInfinity;
        // towards the span is the neighbour seat nearer the other end
        var a = first.Seat(seatFirst);
        var b = second.Seat(seatSecond);
        TowardFirst = Vector3.DistanceSquared(first.Seat(seatFirst + 1), b) < Vector3.DistanceSquared(first.Seat(seatFirst - 1), b) ? 1 : -1;
        TowardSecond = Vector3.DistanceSquared(second.Seat(seatSecond + 1), a) < Vector3.DistanceSquared(second.Seat(seatSecond - 1), a) ? 1 : -1;
    }

    /// <summary>Which side of the line from → to a point is on, about the sprocket axis (sign of the turn).</summary>
    private static float Side(Vector3 axis, Vector3 from, Vector3 to, Vector3 point) => Vector3.Dot(axis, Vector3.Cross(to - from, point - from));

    /// <summary>Moves the end on one sprocket while a roller seats or leaves there; returns true on a change.</summary>
    private bool Advance(Sprocket s, ref int seat, int toward, Vector3 other)
    {
        var axis = s.Shaft.AxisWorld;
        var end = s.Seat(seat);
        float inside = Side(axis, end, other, s.Centre);
        // the free roller next to the end seats once its seat reaches the span's line
        if (Side(axis, end, other, s.Seat(seat + toward)) * inside <= 0)
        {
            seat += toward;
            Links--;
            Seated++;
            return true;
        }

        // the end roller leaves once the seated roller behind it, the end and the other end are in line
        var behind = s.Seat(seat - toward);
        if (Side(axis, behind, other, end) * Side(axis, behind, other, s.Centre) > 0)
        {
            seat -= toward;
            Links++;
            Released++;
            return true;
        }

        return false;
    }

    public override void Prepare(float dt)
    {
        _step = dt;
        for (int guard = 0; guard < 4; guard++)
        {
            int first = SeatFirst, second = SeatSecond;
            bool moved = Advance(First, ref first, TowardFirst, EndSecond);
            SeatFirst = first;
            moved |= Advance(Second, ref second, TowardSecond, EndFirst);
            SeatSecond = second;
            if (!moved || Links < 1) break;
        }

        Links = Math.Max(1, Links);
        var pa = EndFirst;
        var pb = EndSecond;
        var d = pa - pb;
        float length = d.Length();
        if (length < 1e-6f)
        {
            _row.Set(A, B, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, 0, 0, 0);
            return;
        }

        var e = d / length;
        // the span pulls A towards B: the row's velocity is the rate of shortening
        var va = -e;
        var wa = -Vector3.Cross(pa - A.Position, e);
        var vb = e;
        var wb = Vector3.Cross(pb - B!.Position, e);
        float slack = RestLength - length;
        if (float.IsPositiveInfinity(_stiffness))
            _row.Set(A, B, va, wa, vb, wb, slack > 0 ? slack / dt : .2f * slack / dt, 0, float.PositiveInfinity);
        else
        {
            var (rate, softness) = Soft(_stiffness, _damping, dt);
            _row.Set(A, B, va, wa, vb, wb, rate * slack, 0, float.PositiveInfinity, softness);
        }
    }

    public override void Solve() => _row.Solve();
}
