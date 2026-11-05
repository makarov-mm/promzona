using System.Numerics;
using MakarovPhysics.RigidBodies.Materials;

namespace MakarovPhysics.RigidBodies;

public sealed class PhysicsWorld
{
    public const float FixedStep = 1f / 120f;

    /// <summary>
    /// Gauss-Seidel sweeps per step, velocity then position.
    ///
    /// A property rather than a const since 29.08, so a test can ASK whether a
    /// stack that drifts is a convergence problem or a modelling one. The
    /// default is unchanged and nothing in the app writes it; the mixed-material
    /// tower measurement in SandboxTests does, and finds out in one run what
    /// three tails of reasoning did not.
    /// </summary>
    public int SolverIterations { get; set; } = 10;

    /// <summary>Sweeps actually used on the last step - the adaptive count, not
    /// the base. Read by SelfTest; the app never writes it.</summary>
    public int EffectiveIterations { get; private set; } = 10;

    /// <summary>The largest normal impulse in the warm-start cache after the
    /// last substep. An instrument, like EffectiveIterations: a resting stack
    /// should cache about one substep's weight per contact and no more. The
    /// tepid start of tail247 cached thirty (02.09), because a seed that is
    /// never applied is never paid back; SandboxTests pins the class shut.</summary>
    public float PeakCachedImpulse { get; private set; }

    /// <summary>Ceiling on the adaptive count. Four times the base is where the
    /// measured return stops being worth the frame: 20 sweeps take the
    /// mixed-material tower from 4194.8 mm of drift to 81.9, and 80 only reach
    /// 27.2 for four times the cost again.</summary>
    public int MaxIterations { get; set; } = 40;

    /// <summary>Turn each body by exactly |ω|·h about ω in a step (the exponential map) instead of the
    /// first-order quaternion update, which turns it by 2·atan(|ω|·h/2): 0.08 % short at 12 rad/s and
    /// 0.59 % at 32 rad/s with the 1/120 s step. The shortfall is harmless for a tumbling body, but a
    /// coupling that ties a shaft's angle to another body's position sees it as a growing error every
    /// step - 17 µm a step on a ball screw at 32 rad/s, far more than the nut's real deflection
    /// (measured 14.10). Off by default, so existing scenes keep their measured behaviour.</summary>
    public bool ExactRotation { get; set; }

    /// <summary>
    /// Worst dynamic-against-dynamic mass ratio among this step's contacts.
    ///
    /// Static bodies are excluded, and that is not a detail: a floor has
    /// infinite mass, so counting it would make every scene in the suite
    /// report an infinite ratio and pay the maximum sweep count forever.
    /// </summary>
    private double WorstMassRatio()
    {
        double worst = 1.0;

        for (int ci = 0; ci < _contactCount; ci++)
        {
            Contact c = _contacts[ci];
            if (c.A.InvMass == 0f || c.B.InvMass == 0f) continue;

            double r = c.A.InvMass > c.B.InvMass
                ? c.A.InvMass / c.B.InvMass
                : c.B.InvMass / c.A.InvMass;

            if (r > worst) worst = r;
        }

        return worst;
    }

    /// <summary>
    /// How many sweeps a given mass step needs.
    ///
    /// Gauss-Seidel converges more slowly the bigger the mass ratio across a
    /// contact, and on 29.08 the suite finally measured how much more slowly: a
    /// nine-block tower of alternating densities 4:1 drifted 4194.8 mm in ten
    /// seconds at ten sweeps, 81.9 at twenty, 57.7 at forty and 27.2 at eighty.
    /// A factor of fifty-one for twice the work, then a long flat tail.
    ///
    /// So the count doubles per octave of mass ratio, capped. A ratio of one -
    /// which is every uniform scene in the suite, and therefore almost every
    /// pinned number in SelfTest - returns EXACTLY the base and nothing about
    /// those scenes moves. The cost is paid only where it was measured to buy
    /// something.
    /// </summary>
    public int IterationsFor(double massRatio)
    {
        // The cap applies to the uniform case too. It used to return the base
        // straight out, so a world with SolverIterations = 80 and
        // MaxIterations = 40 got eighty sweeps on a uniform stack - a property
        // called Max that was not one. Shipping defaults never showed it,
        // which is exactly why the contract has to hold without them (30.08).
        if (!(massRatio > 1.0)) return Math.Min(MaxIterations, SolverIterations);
        int octaves = (int)Math.Floor(Math.Log2(massRatio));
        return Math.Min(MaxIterations, SolverIterations * (1 + octaves));
    }
    private const float Beta = 0.2f; // Baumgarte factor
    /// <summary>
    /// Penetration slop: Baumgarte only pushes out what is deeper than this,
    /// so EVERY resting contact settles at roughly this depth, and in a stack
    /// those depths add up. Eight millimetres is 1.6 % of the sandbox crate
    /// (0.5 m a side), and a stack of twelve sagged by 41 mm - exactly what
    /// reads as "the boxes are sinking into each other".
    ///
    /// Measured (audit 13.09, a three-mode probe, 15 s at rest plus 2 s of
    /// watching):
    ///
    ///     slop     stack of 12    heavy x50      2.5 m drop      jitter
    ///     8 mm     41.0 mm        10.7 mm        20.1 mm         0.00 mm/s
    ///     0.5 mm    7.5 mm         2.8 mm        19.7 mm         0.00 mm/s
    ///
    /// IMPACT penetration does not depend on the slop at all - speculative
    /// contacts handle that - while rest depends on it entirely, and the
    /// jitter the slop exists to prevent never appeared once.
    ///
    /// So the slop is RELATIVE now: a fraction of the smaller of the two
    /// bodies in contact, with the old eight millimetres as the cap. Large
    /// bodies behave as before, small ones stop sinking.
    /// </summary>
    private const float SlopCap = 0.008f;
    private const float SlopFraction = 0.001f;

    /// <summary>Slop for one pair: 0.1 % of the smaller extent
    /// (SlopFraction - the comment used to say 0.2 % while the constant was
    /// always 0.001; external audit 14.09), capped by the old ceiling and
    /// never below a tenth of a millimetre.</summary>
    private static float SlopFor(in Contact c)
    {
        float size = MathF.Min(Extent(c.A), Extent(c.B));
        return Math.Clamp(SlopFraction * size, 0.0001f, SlopCap);
    }

    /// <summary>Smallest extent of a body: for a compound one, the smallest
    /// among its children; for an empty one, a metre, so old scenes behave
    /// exactly as before.</summary>
    private static float Extent(RigidBody? body)
    {
        if (body is null || body.Children.Length == 0) return 1f;
        float smallest = float.MaxValue;
        foreach (ChildShape child in body.Children)
        {
            float size = child.Shape switch
            {
                ShapeType.Box => 2f * MathF.Min(MathF.Min(child.HalfExtents.X, child.HalfExtents.Y), child.HalfExtents.Z),
                ShapeType.Capsule => 2f * child.Radius,
                _ => 2f * child.Radius,
            };
            if (size > 1e-4f && size < smallest) smallest = size;
        }
        return smallest < float.MaxValue ? smallest : 1f;
    }
    private const float RestitutionThreshold = 1.0f;

    // rolling resistance for balls in contact (per second); tuned so a ball coasts to rest
    // in a couple of seconds instead of rolling indefinitely
    private const float RollAngularDamp = 1.6f;
    private const float RollLinearDamp = 0.9f;

    // sleeping: a body below both velocity thresholds for SleepDelay seconds nods off
    private const float SleepLinVelSq = 0.08f * 0.08f;
    private const float SleepAngVelSq = 0.30f * 0.30f;
    private const float SleepDelay = 0.6f;
    private const float WakeImpactSpeed = 0.30f;

    private const float WarmStartMatchDistSq = 0.04f * 0.04f;

    /// <summary>
    /// SPECULATIVE CONTACTS (27.08). A sphere is allowed to find a contact
    /// BEFORE it touches - up to this far away - and an approaching pair is
    /// then braked so it ends the substep touching instead of buried.
    ///
    /// Why: at 120 Hz a ball thrown at 12 m/s moves 10 cm a substep, a
    /// quarter of its own diameter, so the contact is first seen when it is
    /// already deep inside. Baumgarte then pushes it out over several
    /// substeps and the result reads as rubber - "the bricks look more like
    /// sponges", DTF, 27.08, and he was right: our own log had a ball 16 cm
    /// inside another one. Tools/twins/penetration_twin.py measures the
    /// candidates: raising the position-bias clamp or Baumgarte changes
    /// impact penetration by NOTHING (53.1 mm either way); speculative
    /// contacts take it to 0.0 mm.
    ///
    /// The margin a pair needs is exactly what it can close in one substep,
    /// so it is computed from the pair's own speed and is zero for anything
    /// resting - a settled stack pays no phantom contacts. This is the cap.
    /// </summary>
    private const float SpeculativeCap = 0.12f;

    private static float PairMargin(RigidBody a, RigidBody b)
    {
        float speed = a.Velocity.Length() + b.Velocity.Length();
        return MathF.Min(SpeculativeCap, speed * FixedStep);
    }

    public readonly List<RigidBody> Bodies = [];
    public readonly List<Joint> Joints = [];
    /// <summary>Begin the mechanism rows that keep their meaning between substeps - hinge and slider
    /// rows, rods, points on planes, point drives, elastic stops - from last substep's impulses. Without it
    /// every substep solves a held load from zero: a seven-bar inversor under 50 N needed 1000 sweeps to
    /// stand within a micron, with it 80 (measured 14.10). Opt-in; contacts warm-start regardless.</summary>
    public bool WarmStartMechanisms { get; set; }
    /// <summary>Solve the mechanisms' plain equality rows - hinge and slider rows, rods, points on planes,
    /// plane locks - all at once in every sweep, by a factorisation of their J·M⁻¹·Jᵀ made once per
    /// substep; drives, stops and contacts are swept row by row around it as before. For linkages whose
    /// levers multiply forces - a scissor lift, a toggle press - which row-by-row sweeps settle only after
    /// thousands of passes. The cost grows with the cube of the number of rows: meant for a few dozen
    /// bodies. Opt-in.</summary>
    public bool DirectMechanisms { get; set; }
    private readonly MechanismBlock _block = new();
    private readonly List<MechanismConstraint> _mechanisms = [];
    public IReadOnlyList<MechanismConstraint> Mechanisms => _mechanisms;
    public void AddMechanism(MechanismConstraint constraint)
    {
        ArgumentNullException.ThrowIfNull(constraint);
        if (!Bodies.Contains(constraint.A) || (constraint.B is not null && !Bodies.Contains(constraint.B)))
            throw new ArgumentException("Constraint bodies must belong to this world.");
        if (_mechanisms.Contains(constraint)) throw new ArgumentException("Duplicate constraint.");
        if (constraint is GearConstraint gear && (!_mechanisms.Contains(gear.First) || !_mechanisms.Contains(gear.Second)))
            throw new ArgumentException("Register the supporting hinges first.");
        if (constraint is ShaftBrake brake && !_mechanisms.Contains(brake.Hinge))
            throw new ArgumentException("Register the supporting hinge first.");
        foreach (var support in constraint.Supports)
            if (!_mechanisms.Contains(support))
                throw new ArgumentException("Register the supporting joints first.");
        _mechanisms.Add(constraint);
    }
    public void RemoveMechanism(MechanismConstraint constraint)
    {
        _mechanisms.Remove(constraint);
        if (constraint is HingeConstraint hinge)
            _mechanisms.RemoveAll(c => (c is GearConstraint g && (g.First == hinge || g.Second == hinge)) || (c is ShaftBrake brake && brake.Hinge == hinge));
        // a coupling cannot outlive the shaft or guide it reads
        _mechanisms.RemoveAll(c => c.Supports.Contains(constraint));
    }

    /// <summary>Validated joint registration. The raw list
    /// stays public for iteration and removal, but additions should go
    /// through here: a malformed constraint (missing body, foreign body,
    /// static-static pair, non-finite anchors or parameters) is rejected with
    /// a clear exception instead of blowing up mid-Step deep inside the
    /// solver. Joint.Presolve keeps its own defensive guard for anything
    /// that mutates after registration.</summary>
    public void AddJoint(Joint joint)
    {
        ArgumentNullException.ThrowIfNull(joint);

        if (joint.A is null)
            throw new ArgumentException("Joint.A must be set.", nameof(joint));

        if (!Bodies.Contains(joint.A))
            throw new ArgumentException("Joint.A does not belong to this world.", nameof(joint));

        if (joint.B is not null && !Bodies.Contains(joint.B))
            throw new ArgumentException("Joint.B does not belong to this world.", nameof(joint));

        if (joint.A.IsStatic && (joint.B is null || joint.B.IsStatic))
            throw new ArgumentException("At least one joint body must be dynamic.", nameof(joint));

        if (!IsFinite(joint.LocalA) || !IsFinite(joint.LocalB))
            throw new ArgumentException("Joint anchors must be finite.", nameof(joint));

        if (!float.IsFinite(joint.Length) || joint.Length < 0f)
            throw new ArgumentException("Joint.Length must be finite and non-negative.", nameof(joint));

        if (!float.IsFinite(joint.Stiffness) 
            || 
            joint.Stiffness < 0f
            ||
            !float.IsFinite(joint.Damping) || joint.Damping < 0f)
        {
            throw new ArgumentException("Joint stiffness and damping must be finite and non-negative.", nameof(joint));
        }

        Joints.Add(joint);
    }

    private static bool IsFinite(Vector3 v) => float.IsFinite(v.X) && float.IsFinite(v.Y) && float.IsFinite(v.Z);
    public readonly List<ForceField> Fields = [];
    public readonly List<WaterVolume> Waters = [];

    // strong contacts this frame, drained by the renderer to spawn sparks
    public readonly List<(Vector3 point, Vector3 normal, float speed)> Impacts = [];

    /// <summary>Bodies that took a contact this frame, with the closing speed
    /// at first touch. Consumers that must react to a real COLLISION (the
    /// 7.7: an explosive detonating on impact rather than on mere airspeed)
    /// read this instead of guessing from velocities.</summary>
    public readonly List<(RigidBody body, float speed)> BodyImpacts = [];

    // one-frame fracture events, drained by the renderer to spawn material-specific debris VFX
    public readonly List<BreakEvent> BreakEvents = [];
    public readonly RigidBody Ground = new()
    {
        InvMass = 0f,
        Restitution = 0.2f,
        Friction = 0.7f,
    };

    public Vector3 Gravity = new(0, -9.81f, 0);
    // Spacecraft experiments need force-free coasting. Defaults preserve the
    // sandbox's settling behaviour; zero damping and no sleep conserve momentum.
    public float LinearDamping = .02f, AngularDamping = .08f;
    public bool EnableSleeping = true;
    public bool EnableGroundPlane = true;

    // Contacts are POOLED: the list keeps last substep's Contact objects and
    // only the first _contactCount entries are alive this substep. At 120
    // substeps/s a busy pile used to allocate hundreds of short-lived Contact
    // objects per frame, which was the single largest GC source in this loop.
    private readonly List<Contact> _contacts = [];

    // top-down contact solve order (see the comment at the sort site). The
    // comparer that used to live here was a field, so it looked free, and it
    // was not: List.Sort with an IComparer wraps it on every call. See
    // SortContactsTopDown, which sorts on a materialised float key instead.
    // INSTANCE, not static (27.08). It was static, and it is written every
    // substep from THIS world's gravity and read by the contact sort and the
    // support wake. Two worlds with different gravity stepping on two threads
    // would have handed each other their sort axis mid-step: wrong top-down
    // order, wrong vertical-velocity test, wrong wake, and nondeterministic
    // to boot. One machine with one scene never saw it; a shared engine is
    // still wrong. The sort key is built inside the instance now.
    private Vector3 _solveOrderUp = Vector3.UnitY;
    private int _contactCount;
    private float _accumulator;
    private Random _breakRng = new(24680);
    public RigidBody? Grabbed;
    public Vector3 GrabLocalAnchor;
    public Vector3 DragTarget;

    public int AwakeCount
    {
        get
        {
            int n = 0;
            foreach (var b in Bodies) if (!b.Inactive) n++;
            return n;
        }
    }

    // ---- warm starting cache ----
    // Impulses solved on the previous substep, keyed by body pair and re-anchored
    // in A's local frame. Feeding them back as the starting guess is what lets a
    // stack of boxes actually settle instead of buzzing forever: the solver only
    // has to correct the small per-frame change, not rediscover the whole load path
    // from zero in 10 iterations.
    private struct CachedImpulse
    {
        /// <summary>Consumed by a contact this substep. Without this flag two
        /// clipped manifold points could MATCH THE SAME cache entry and each
        /// apply its impulse - a doubled warm-start with two different lever
        /// arms, i.e. a parasitic torque injected every substep. That torque
        /// span every box of the default tower slowly and threw the stack
        /// (21.08: towers over 7 boxes collapsed; headless A/B pinned it).</summary>
        public bool Used;

        public Vector3 LocalA;
        public float Pn, Pt1, Pt2;
    }
    private Dictionary<(RigidBody, RigidBody), List<CachedImpulse>> _warmCache = new();
    private Dictionary<(RigidBody, RigidBody), List<CachedImpulse>> _warmCacheBack = new();

    /// <summary>
    /// Full teardown for a scene rebuild. Clearing only the public lists is
    /// not enough: the fixed-step accumulator would smuggle a fraction of a
    /// step into the new scene, and the warm-start impulse caches keep entries
    /// keyed by the OLD RigidBody instances - stale references that both leak
    /// and could warm-start a fresh contact with impulses from a dead scene.
    /// </summary>
    public void Clear()
    {
        Bodies.Clear();
        Joints.Clear();
        _mechanisms.Clear();
        Fields.Clear();
        Waters.Clear();
        Grabbed = null;
        Impacts.Clear();
        BodyImpacts.Clear();
        BreakEvents.Clear();
        _contacts.Clear();   // drop the pool too: pooled contacts reference old bodies
        _contactCount = 0;
        _warmCache.Clear();
        _warmCacheBack.Clear();
        PeakCachedImpulse = 0f;
        _accumulator = 0f;
        // determinism: the same impact after a Clear() shatters into the same
        // debris - the fracture RNG restarts with the world
        _breakRng = new Random(24680);
    }

    public void Step(float dt)
    {
        // dt validation: a negative dt would silently DRAIN the
        // accumulator, and MathF.Min(NaN, 0.05) = NaN would poison it forever
        if (!float.IsFinite(dt) || dt <= 0f) return;
        long t0 = GC.GetAllocatedBytesForCurrentThread();
        Impacts.Clear();
        BodyImpacts.Clear();
        BreakEvents.Clear();
        _accumulator += MathF.Min(dt, 0.05f);
        int steps = 0;
        long t1 = GC.GetAllocatedBytesForCurrentThread();
        while (_accumulator >= FixedStep && steps < 8)
        {
            SubStep(FixedStep);
            _accumulator -= FixedStep;
            steps++;
        }
        long t2 = GC.GetAllocatedBytesForCurrentThread();
        // drop time ONLY when there is a real backlog: a frame that needed
        // exactly 8 substeps used to lose its fractional remainder too
        if (steps == 8 && _accumulator >= FixedStep) _accumulator = 0f;

        // Age out fracture debris so it doesn't accumulate and keep jittering/clattering forever.
        List<RigidBody>? expired = null;
        foreach (var b in Bodies)
        {
            if (b.DebrisLife < 0f) continue;
            b.DebrisLife -= dt;
            if (b.DebrisLife <= 0f) (expired ??= new List<RigidBody>()).Add(b);
        }
        if (expired is not null)
            foreach (var b in expired) RemoveBody(b);
        long t3 = GC.GetAllocatedBytesForCurrentThread();
        // the 04.09 log's "outside SubStep 9 568 B" (32 B a step, one small
        // object) has no name yet: these two say whether it is Step's own
        // body or an unmeasured stretch inside SubStep (see AllocBytesPre)
        AllocBytesSubSteps += t2 - t1;
        AllocBytesStepBody += (t1 - t0) + (t3 - t2);
    }

    private void SubStep(float h)
    {
        long p0 = GC.GetAllocatedBytesForCurrentThread();
        foreach (var w in Waters) w.Step(h);
        foreach (var b in Bodies)
        {
            if (b.Inactive) continue;
            b.Velocity += Gravity * h;
        }

        ApplyEnvironment(h);
        ApplyDragSpring(h);

        _contactCount = 0;   // pooled: last substep's Contact objects get reused
        foreach (var b in Bodies) b.Touching = false;
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        AllocBytesPre += a0 - p0;
        GenerateContacts();
        long aw = GC.GetAllocatedBytesForCurrentThread();
        WakeTheUnsupported();
        long a1 = GC.GetAllocatedBytesForCurrentThread();
        AllocBytesWake += a1 - aw;
        AllocBytesContacts += a1 - a0;

        // Solve order matters, and bottom-up is the WRONG order (17.08). The
        // broadphase emits pairs by body index, which for a stack means
        // floor->crate0, crate0->crate1, ... - bottom-up. Sequential impulses
        // solved in that order pump a systematic sideways drift through the
        // friction clamp: a lower contact is solved while its normal impulse
        // does not yet carry the column above, so mu*Pn clamps friction
        // against a fictitiously light load, every iteration, every substep.
        // Measured on the twin (Tools/twins/sandbox_stack_twin.py, 5-crate
        // column, 12 s at 120 Hz): bottom-up drifts the top crate 295 mm and
        // the default scene's stairs demolish themselves; TOP-DOWN drifts
        // 8 mm and plateaus flat through 24 s. So: sort contacts from the
        // top of the gravity axis downward - the lowest contacts are solved
        // last, when the impulses above them already carry the true weight.
        // With no gravity there is no "down" and no stack, so keep the
        // natural order. (Twin note, open question: at 30 velocity
        // iterations even top-down destabilises in the twin; the engine
        // runs 10, where top-down is measured rock-solid.)
        if (Gravity.LengthSquared() > 1e-8f)
        {
            _solveOrderUp = -Vector3.Normalize(Gravity);
            long s0 = GC.GetAllocatedBytesForCurrentThread();
            SortContactsTopDown();
            AllocSort += GC.GetAllocatedBytesForCurrentThread() - s0;
        }

        for (int ci = 0; ci < _contactCount; ci++)
        {
            Contact c = _contacts[ci];
            if (c.Penetration <= 0f) continue;   // speculative: not touching yet
            c.A.Touching = true;
            c.B.Touching = true;
        }

        // joints share the solver with contacts: wake the pairs first so a tug on one
        // end of a chain travels down it, then interleave the iterations
        foreach (var j in Joints) j.WakePair();
        foreach (var j in Joints) j.Presolve(h);
        foreach (var c in _mechanisms) { c.Wake(); c.Prepare(h); }

        long s1 = GC.GetAllocatedBytesForCurrentThread();
        for (int ci = 0; ci < _contactCount; ci++) Presolve(_contacts[ci], h);
        WarmStart();
        if (WarmStartMechanisms) foreach (var c in _mechanisms) c.WarmStart();
        if (DirectMechanisms) _block.Build(_mechanisms);
        int sweeps = EffectiveIterations = IterationsFor(WorstMassRatio());
        for (int i = 0; i < sweeps; i++)
        {
            foreach (var j in Joints) j.Solve(h);
            if (DirectMechanisms) _block.Solve();
            foreach (var c in _mechanisms) c.Solve();
            for (int ci = 0; ci < _contactCount; ci++) SolveContact(_contacts[ci]);
        }
        for (int i = 0; i < sweeps; i++)
            for (int ci = 0; ci < _contactCount; ci++) SolvePosition(_contacts[ci]);
        long s2 = GC.GetAllocatedBytesForCurrentThread();
        AllocIterations += s2 - s1;

        StoreWarmCache();
        long s3 = GC.GetAllocatedBytesForCurrentThread();
        AllocWarmCache += s3 - s2;
        CollectImpacts();
        BreakBodiesFromImpacts();
        AllocImpacts += GC.GetAllocatedBytesForCurrentThread() - s3;
        long a2 = GC.GetAllocatedBytesForCurrentThread();
        AllocBytesSolver += a2 - a1;

        foreach (var b in Bodies)
        {
            if (b.Inactive) continue;

            b.Position += (b.Velocity + b.BiasVelocity) * h;

            var w = b.AngularVelocity + b.BiasAngularVelocity;
            if (ExactRotation && w.LengthSquared() > 1e-10f)
            {
                float rate = w.Length();
                b.Rotation = Quaternion.Normalize(Quaternion.CreateFromAxisAngle(w / rate, rate * h) * b.Rotation);
            }
            else if (w.LengthSquared() > 1e-10f)
            {
                var dq = new Quaternion(w.X, w.Y, w.Z, 0f) * b.Rotation;

                b.Rotation = Quaternion.Normalize(new Quaternion(
                    b.Rotation.X + dq.X * 0.5f * h,
                    b.Rotation.Y + dq.Y * 0.5f * h,
                    b.Rotation.Z + dq.Z * 0.5f * h,
                    b.Rotation.W + dq.W * 0.5f * h));
            }

            b.BiasVelocity = Vector3.Zero;
            b.BiasAngularVelocity = Vector3.Zero;
            if (b.RippleCooldown > 0f) b.RippleCooldown -= h;
            if (b.Wetness > 0f) b.Wetness = MathF.Max(0f, b.Wetness - h * 0.18f);
            if (b.Charge > 0f) b.Charge = MathF.Max(0f, b.Charge - h * 0.85f);

            // mild damping keeps stacks from buzzing
            b.Velocity *= 1f / (1f + Math.Max(0, LinearDamping) * h);
            b.AngularVelocity *= 1f / (1f + Math.Max(0, AngularDamping) * h);

            // rolling resistance: a ball resting on a surface should coast to a stop, not
            // roll forever (wind-pushed balls, the domino striker, thrown bowling balls).
            // Only applied to balls that are actually touching something, so airborne or
            // floating spheres are unaffected.
            if (b.IsBall && b.Touching)
            {
                b.AngularVelocity *= 1f / (1f + RollAngularDamp * h);
                // shave the horizontal glide too; gravity-aligned (fall) component is left alone
                var v = b.Velocity;
                var up = Vector3.UnitY;
                var vUp = Vector3.Dot(v, up) * up;
                var vFlat = v - vUp;
                vFlat *= 1f / (1f + RollLinearDamp * h);
                b.Velocity = vFlat + vUp;
            }

            // sleep bookkeeping
            if (EnableSleeping && b.Velocity.LengthSquared() < SleepLinVelSq
                && b.AngularVelocity.LengthSquared() < SleepAngVelSq)
            {
                b.SleepTimer += h;
                if (b.SleepTimer >= SleepDelay && b != Grabbed)
                {
                    b.Sleeping = true;
                    b.Velocity = Vector3.Zero;
                    b.AngularVelocity = Vector3.Zero;
                }
            }
            else
            {
                b.SleepTimer = 0f;
            }

            b.UpdateDerived();
        }
        foreach (var c in _mechanisms) c.Finish(h);
        AllocBytesIntegrate += GC.GetAllocatedBytesForCurrentThread() - a2;
    }

    private void ApplyEnvironment(float h)
    {
        if (Fields.Count == 0 && Waters.Count == 0) return;
        foreach (var b in Bodies)
        {
            if (b.IsStatic) continue;

            // `affected` is true only when a field or a water volume
            // actually acted on THIS body. Previously any field in the scene
            // woke every body (killing the sleeping system), while water never
            // woke anything (a sleeping body could carry buoyancy velocity and
            // stay frozen).
            bool affected = false;
            foreach (var f in Fields) affected |= f.Apply(b, h);
            foreach (var w in Waters) affected |= w.Apply(b, Gravity, h);
            if (affected && b.Sleeping) b.Wake();
        }
    }

    private const float ImpactSparkSpeed = 2.5f;

    private void CollectImpacts()
    {
        for (int ci = 0; ci < _contactCount; ci++)
        {
            Contact c = _contacts[ci];
            if (c.ImpactSpeed > ImpactSparkSpeed)
            {
                Impacts.Add((c.Point, c.Normal, c.ImpactSpeed));
                if (!c.A.IsStatic) BodyImpacts.Add((c.A, c.ImpactSpeed));
                if (!c.B.IsStatic) BodyImpacts.Add((c.B, c.ImpactSpeed));
            }
        }
    }

    // reused every substep - allocating it fresh was pure GC churn
    private readonly Dictionary<RigidBody, (float speed, Vector3 point, Vector3 normal)> _toBreak = new();

    private void BreakBodiesFromImpacts()
    {
        if (_contactCount == 0 || Bodies.Count > 520) return;

        _toBreak.Clear();
        for (int ci = 0; ci < _contactCount; ci++)
        {
            Contact c = _contacts[ci];
            if (c.ImpactSpeed < 1f) continue;
            // the body in the user's hand does not shatter there: dragging a
            // glass crate along the floor is a move, not an impact - what it
            // is flung INTO still breaks honestly
            if (c.A != Grabbed) TryMarkBreakable(c.A, c.ImpactSpeed, c.Point, c.Normal, _toBreak);
            if (c.B != Grabbed) TryMarkBreakable(c.B, c.ImpactSpeed, c.Point, -c.Normal, _toBreak);
        }

        foreach (var (b, hit) in _toBreak)
            BreakBody(b, hit.point, hit.normal, hit.speed);
        _toBreak.Clear();   // do not hold references to (possibly removed) bodies
    }

    private static void TryMarkBreakable(RigidBody b, float impactSpeed, Vector3 point, Vector3 normal, Dictionary<RigidBody, (float speed, Vector3 point, Vector3 normal)> toBreak)
    {
        if (b.IsStatic || !b.UserObject || !b.Breakable) return;
        if (b.BoundingRadius < 0.16f) return;
        if (impactSpeed < MathF.Max(0.5f, b.BreakThreshold)) return;

        if (!toBreak.TryGetValue(b, out var existing) || impactSpeed > existing.speed)
            toBreak[b] = (impactSpeed, point, normal);
    }

    // Force a breakable body to fracture now (used by explosions, which otherwise only shove bodies).
    public void FractureBody(RigidBody b, Vector3 point, Vector3 normal, float impactSpeed)
    {
        if (b.Breakable && b.BoundingRadius >= 0.16f) BreakBody(b, point, normal, impactSpeed);
    }

    private void BreakBody(RigidBody b, Vector3 hitPoint, Vector3 hitNormal, float impactSpeed)
    {
        if (!Bodies.Contains(b)) return;

        var pieces = new List<RigidBody>(Math.Clamp(b.BreakPieces, 3, 18));
        foreach (var child in b.Children)
            CreateBreakPiecesForChild(b, child, pieces);

        if (pieces.Count == 0) return;

        BreakEvents.Add(new BreakEvent(
            hitPoint,
            hitNormal.LengthSquared() > 1e-6f ? Vector3.Normalize(hitNormal) : Vector3.UnitY,
            impactSpeed,
            b.MaterialId,
            b.Color,
            pieces.Count));

        var originalVel = b.Velocity;
        var originalAng = b.AngularVelocity;
        var color = b.Color;

        RemoveBody(b);

        foreach (var p in pieces)
        {
            p.Color = Vector3.Clamp(color * (0.75f + (float)_breakRng.NextDouble() * 0.35f), Vector3.Zero, Vector3.One);
            p.MaterialId = b.MaterialId;
            p.Friction = b.Friction;
            p.Restitution = MathF.Max(b.Restitution, 0.18f);
            p.Flammability = b.Flammability;
            p.Conductivity = b.Conductivity;
            p.ExplosivePower = 0f; // debris does not chain-detonate by default
            p.Breakable = false; // one-level fracture keeps runaway debris under control
            p.BreakThreshold = b.BreakThreshold;
            p.Velocity = originalVel + RandomUnit() * (1.0f + (float)_breakRng.NextDouble() * 2.5f);
            p.AngularVelocity = originalAng + RandomUnit() * (2.0f + (float)_breakRng.NextDouble() * 5.0f);
            p.UserObject = true;
            p.DebrisLife = 8f + (float)_breakRng.NextDouble() * 4f;   // clear after 8-12s so debris can't pile up forever
            p.Wake();
            Bodies.Add(p);
        }
    }

    private void CreateBreakPiecesForChild(RigidBody source, ChildShape child, List<RigidBody> pieces)
    {
        Vector3 childPos = source.Position + Vector3.Transform(child.LocalPos, source.Rotation);
        Quaternion childRot = source.Rotation * child.LocalRot;
        int maxPieces = Math.Clamp(source.BreakPieces, 3, 18);

        if (child.Shape == ShapeType.Box)
        {
            var he = child.HalfExtents;
            if (he.LengthSquared() < 0.03f) return;

            switch (source.MaterialId)
            {
                case MaterialId.Wood:
                    CreateWoodSplinters(source, childPos, childRot, he, pieces, maxPieces);
                    return;

                case MaterialId.Glass:
                case MaterialId.Ice:
                    CreateGlassShards(source, childPos, childRot, he, pieces, maxPieces);
                    return;

                case MaterialId.Stone:
                    CreateStoneChunks(source, childPos, childRot, he, pieces, maxPieces);
                    return;

                case MaterialId.Plastic:
                case MaterialId.Synthetic:
                    CreatePlasticFragments(source, childPos, childRot, he, pieces, maxPieces);
                    return;
            }

            Vector3 smallHe = Vector3.Max(he * 0.46f, new Vector3(0.06f));
            int made = 0;

            for (int sx = -1; sx <= 1 && made < maxPieces; sx += 2)
            {
                for (int sy = -1; sy <= 1 && made < maxPieces; sy += 2)
                {
                    for (int sz = -1; sz <= 1 && made < maxPieces; sz += 2)
                    {
                        var local = new Vector3(sx * he.X * 0.48f, sy * he.Y * 0.48f, sz * he.Z * 0.48f);
                        RigidBody body = RigidBody.CreateBox(childPos + Vector3.Transform(local, childRot), smallHe, MathF.Max(source.Density, 0.001f));
                        body.Rotation = childRot;
                        body.UpdateDerived();
                        pieces.Add(body);
                        made++;
                    }
                }
            }

            return;
        }

        if (child.Shape == ShapeType.Sphere)
        {
            float r = MathF.Max(child.Radius * 0.42f, 0.055f);
            int count = Math.Min(maxPieces, 7);

            for (int i = 0; i < count; i++)
            {
                Vector3 dir = RandomUnit();
                var p = RigidBody.CreateSphere(childPos + dir * child.Radius * 0.38f, r, MathF.Max(source.Density, 0.001f));
                pieces.Add(p);
            }
            return;
        }

        // Capsules are approximated by bead-like fragments.
        if (child.Shape == ShapeType.Capsule)
        {
            float r = MathF.Max(child.Radius * 0.38f, 0.05f);
            int count = Math.Min(maxPieces, 6);

            for (int i = 0; i < count; i++)
            {
                float u = count == 1 ? 0f : (i / (float)(count - 1) - 0.5f) * child.HalfHeight * 2f;
                var local = new Vector3(0, u, 0) + RandomUnit() * child.Radius * 0.2f;
                var p = RigidBody.CreateSphere(childPos + Vector3.Transform(local, childRot), r, MathF.Max(source.Density, 0.001f));
                pieces.Add(p);
            }
        }
    }


    private void CreateWoodSplinters(RigidBody source, Vector3 childPos, Quaternion childRot, Vector3 he, List<RigidBody> pieces, int maxPieces)
    {
        int count = Math.Clamp(maxPieces, 4, 14);
        int axis = he.X >= he.Y && he.X >= he.Z ? 0 : (he.Y >= he.Z ? 1 : 2);
        for (int i = 0; i < count; i++)
        {
            var local = new Vector3(Rand(-he.X * 0.65f, he.X * 0.65f), Rand(-he.Y * 0.65f, he.Y * 0.65f), Rand(-he.Z * 0.65f, he.Z * 0.65f));
            var splinter = new Vector3(
                axis == 0 ? MathF.Max(he.X * Rand(0.28f, 0.55f), 0.05f) : MathF.Max(he.X * Rand(0.08f, 0.18f), 0.035f),
                axis == 1 ? MathF.Max(he.Y * Rand(0.28f, 0.55f), 0.05f) : MathF.Max(he.Y * Rand(0.08f, 0.18f), 0.035f),
                axis == 2 ? MathF.Max(he.Z * Rand(0.28f, 0.55f), 0.05f) : MathF.Max(he.Z * Rand(0.08f, 0.18f), 0.035f));
            var p = RigidBody.CreateBox(childPos + Vector3.Transform(local, childRot), splinter, MathF.Max(source.Density, 0.001f));
            p.Rotation = childRot;
            p.UpdateDerived();
            pieces.Add(p);
        }
    }

    private void CreateGlassShards(RigidBody source, Vector3 childPos, Quaternion childRot, Vector3 he, List<RigidBody> pieces, int maxPieces)
    {
        int count = Math.Clamp(maxPieces + 4, 8, 18);
        for (int i = 0; i < count; i++)
        {
            var local = new Vector3(Rand(-he.X * 0.75f, he.X * 0.75f), Rand(-he.Y * 0.75f, he.Y * 0.75f), Rand(-he.Z * 0.75f, he.Z * 0.75f));
            var shard = new Vector3(
                MathF.Max(he.X * Rand(0.10f, 0.26f), 0.025f),
                MathF.Max(he.Y * Rand(0.035f, 0.09f), 0.012f),
                MathF.Max(he.Z * Rand(0.10f, 0.26f), 0.025f));
            var p = RigidBody.CreateBox(childPos + Vector3.Transform(local, childRot), shard, MathF.Max(source.Density, 0.001f));
            p.Rotation = childRot;
            p.Restitution = MathF.Max(p.Restitution, 0.28f);
            p.UpdateDerived();
            pieces.Add(p);
        }
    }

    private void CreateStoneChunks(RigidBody source, Vector3 childPos, Quaternion childRot, Vector3 he, List<RigidBody> pieces, int maxPieces)
    {
        int count = Math.Clamp(maxPieces, 5, 12);
        for (int i = 0; i < count; i++)
        {
            var local = new Vector3(Rand(-he.X * 0.60f, he.X * 0.60f), Rand(-he.Y * 0.60f, he.Y * 0.60f), Rand(-he.Z * 0.60f, he.Z * 0.60f));
            var chunk = new Vector3(
                MathF.Max(he.X * Rand(0.22f, 0.42f), 0.045f),
                MathF.Max(he.Y * Rand(0.18f, 0.42f), 0.045f),
                MathF.Max(he.Z * Rand(0.22f, 0.42f), 0.045f));
            var p = RigidBody.CreateBox(childPos + Vector3.Transform(local, childRot), chunk, MathF.Max(source.Density, 0.001f));
            p.Rotation = childRot;
            p.Restitution = 0.08f;
            p.UpdateDerived();
            pieces.Add(p);
        }
    }

    private void CreatePlasticFragments(RigidBody source, Vector3 childPos, Quaternion childRot, Vector3 he, List<RigidBody> pieces, int maxPieces)
    {
        int count = Math.Clamp(maxPieces, 5, 12);
        for (int i = 0; i < count; i++)
        {
            var local = new Vector3(Rand(-he.X * 0.65f, he.X * 0.65f), Rand(-he.Y * 0.65f, he.Y * 0.65f), Rand(-he.Z * 0.65f, he.Z * 0.65f));
            var frag = new Vector3(
                MathF.Max(he.X * Rand(0.18f, 0.38f), 0.035f),
                MathF.Max(he.Y * Rand(0.12f, 0.32f), 0.030f),
                MathF.Max(he.Z * Rand(0.18f, 0.38f), 0.035f));
            var p = RigidBody.CreateBox(childPos + Vector3.Transform(local, childRot), frag, MathF.Max(source.Density, 0.001f));
            p.Rotation = childRot;
            p.Restitution = MathF.Max(source.Restitution, 0.22f);
            p.UpdateDerived();
            pieces.Add(p);
        }
    }

    private float Rand(float min, float max) => min + (float)_breakRng.NextDouble() * (max - min);
    private Vector3 RandomUnit()
    {
        var v = new Vector3(
            (float)_breakRng.NextDouble() * 2f - 1f,
            (float)_breakRng.NextDouble() * 2f - 1f,
            (float)_breakRng.NextDouble() * 2f - 1f);
        return v.LengthSquared() > 1e-6f ? Vector3.Normalize(v) : Vector3.UnitY;
    }

    /// <summary>Drops a body and any joints that referenced it. Keeps the world consistent.</summary>
    public void RemoveBody(RigidBody b)
    {
        Bodies.Remove(b);
        Joints.RemoveAll(j => j.Involves(b));
        _mechanisms.RemoveAll(c => c.Involves(b));
        if (Grabbed == b) Grabbed = null;
        // A removed body must read as inert: callers may still hold the
        // reference (a fractured ball is replaced by shards), and stale
        // pre-removal velocity looks like a body that "never comes to rest".
        b.Velocity = Vector3.Zero;
        b.AngularVelocity = Vector3.Zero;
        b.Sleeping = true;
    }

    private void ApplyDragSpring(float h)
    {
        if (Grabbed is null || Grabbed.IsStatic) return;
        Grabbed.Wake();

        var anchor = Grabbed.Position + Vector3.Transform(GrabLocalAnchor, Grabbed.Rotation);
        var delta = DragTarget - anchor;
        float len = delta.Length();
        // The old cap of 4 m at stiffness 180 allowed accelerations of
        // 720 m/s^2 - 73 g. A grabbed domino yanked toward a far cursor
        // rocketed through its neighbours and scattered the whole stack,
        // and breakable crates shattered in the user's hand. 1.2 m at 120
        // still feels snappy (144 m/s^2 peak) while a casual nudge stays
        // orders of magnitude below any break threshold.
        if (len > 1.2f) delta *= 1.2f / len;

        var velAtAnchor = Grabbed.VelocityAt(anchor);
        // damping 22 at stiffness 120 is critical (zeta = 1): the body
        // arrives at the cursor without the overshoot slam that used to
        // knock stacks over on release
        var force = Grabbed.Mass * (delta * 120f - velAtAnchor * 22f);
        Grabbed.ApplyImpulse(force * h, anchor);

        // a deliberate fling is still possible - 9 m/s clears the break
        // threshold of glass - but the speed no longer runs away while the
        // cursor merely leads the body across the table
        float speed = Grabbed.Velocity.Length();
        if (speed > 9f) Grabbed.Velocity *= 9f / speed;

        // extra angular damping while held, otherwise boxes spin like propellers
        Grabbed.AngularVelocity *= 1f / (1f + 4f * h);
    }

    // ================= contact generation =================

    // ---- narrow phase, in three passes (27.08) ----
    //
    // The SOLVER is sequential by design (Gauss-Seidel, top-down order), and
    // stays so. The narrow phase is not a solver: for a given pair of bodies
    // BoxBox is a pure function of two poses that writes nothing the other
    // pairs read. It is also where a big collapse spends its frame - on the
    // 2 476-brick fortress with 1 100 bricks awake, ~20 000 box-box SAT
    // tests a substep, each 1-2 us, twice a frame: that was the "really
    // chugging now" of the 27.08 screenshot, not the solver (7 000 contacts x 20
    // sweeps is a fraction of it).
    //
    // So the pass is split. COLLECT (sequential) walks the spatial hash and
    // lists the candidate pairs in the exact order the old loop tested them;
    // PROCESS (parallel over ranges of that list) runs the shape tests into
    // per-thread scratch, tagging every contact with its pair index and a
    // sequence number; MERGE (sequential) sorts the results back into pair
    // order and commits them exactly as AddContact always did - pooled
    // Contact objects, wake-on-impact, debris one-way rules. The contact
    // list the solver receives is therefore in the SAME ORDER as before, and
    // with the same numbers, so the top-down sort and the warm cache see
    // nothing new; a run with one thread and a run with sixteen are identical.
    //
    // One honest difference from the old single loop: a pair of two SLEEPING
    // bodies is skipped at collection time. In the old loop, a sleeper woken
    // by an earlier pair in the same pass could still meet a second sleeper
    // later in that pass; now that meeting waits for the next substep,
    // 1/120 s later. The contact-impact wake rule was written to propagate
    // through contact anyway, and one substep of latency at the wave front
    // is below anything a pin has ever measured.
    private readonly struct PairRef(int i, int j)
    {
        public readonly int I = i, J = j;
    }

    private PairRef[] _pairs = new PairRef[1024];
    private int _pairCount;

    /// <summary>Per-thread narrow-phase scratch: the SAT axes and clip
    /// polygons BoxBox used to keep on the world, and the contacts this
    /// thread found, tagged for the merge.</summary>
    private sealed class NarrowScratch
    {
        public readonly Vector3[] AxesA = new Vector3[3];
        public readonly Vector3[] AxesB = new Vector3[3];
        public readonly List<Vector3> Poly = new(16);
        public readonly List<Vector3> Clip = new(16);
        public readonly List<PendingContact> Pending = new(256);
        public int Pair;     // index of the pair being tested (negative = ground plane pass)
        public int Seq;      // contacts emitted for this pair so far
        public float Margin; // speculative reach for this pair, metres
    }

    private struct PendingContact
    {
        public int Pair, Seq;
        public RigidBody A, B;
        public Vector3 Point, Normal;
        public float Penetration;
    }

    // a Comparison, not a Comparer: Array.Sort(array, comparer) builds a
    // fresh comparer.Compare delegate on every call, Span.Sort(comparison)
    // does not
    private static readonly Comparison<PendingContact> PendingOrder = static (x, y) =>
    {
        int c = x.Pair.CompareTo(y.Pair);
        return c != 0 ? c : x.Seq.CompareTo(y.Seq);
    };

    /// <summary>Diagnostics for the allocation pins: candidate pairs the last
    /// substep collected, and whether it took the parallel path.</summary>
    public int LastPairCount { get; private set; }
    public bool LastNarrowPhaseParallel { get; private set; }

    /// <summary>Managed bytes the calling thread allocated inside each
    /// phase of SubStep since <see cref="ResetAllocationDiagnostics"/>:
    /// contact generation (collect/process/merge), the solver with its
    /// warm cache, and the rest (integration, sleep). Read by the
    /// allocation pins so a regression names its phase instead of a
    /// number (27.08: 200 KB per 300 substeps, three logs, no suspect).
    /// GC.GetAllocatedBytesForCurrentThread is a cheap counter read.</summary>
    public long AllocBytesContacts { get; private set; }
    public long AllocBytesSolver { get; private set; }
    public long AllocBytesIntegrate { get; private set; }

    /// <summary>The contact phase split five ways (fourth log, 27.08: 640 B
    /// a substep in the contact phase, every construct in it allocation-free
    /// on paper): ground plane, broad-phase collect, shape tests, merge into
    /// the contact pool, support wake.</summary>
    public long AllocBytesGround { get; private set; }
    public long AllocBytesCollect { get; private set; }
    public long AllocBytesProcess { get; private set; }
    public long AllocBytesMerge { get; private set; }
    public long AllocBytesWake { get; private set; }

    /// <summary>Fifth log (27.08): all of it in the process pass, ground
    /// pass 0 - but a settled pit may have no ground contacts at all, so 0
    /// there says nothing about Emit. Counted separately: bytes inside Emit
    /// itself and how many contacts each pass emitted.</summary>
    public long AllocBytesEmit { get; private set; }
    public long EmittedGround { get; private set; }
    public long EmittedProcess { get; private set; }

    /// <summary>Seventh log (27.08): the process pass allocates 637 B a
    /// substep and Emit itself allocates NOTHING, so it is one of the shape
    /// tests. Binned by pair kind, plus what the NarrowPhase loop spends
    /// outside the dispatches, so the next log names the function.</summary>
    public long AllocSphereSphere { get; private set; }
    public long AllocSphereBox { get; private set; }
    public long AllocBoxBox { get; private set; }
    public long AllocCapsuleAny { get; private set; }
    public long AllocNarrowOverhead { get; private set; }

    /// <summary>Ninth log (27.08): every shape test 0 B, NarrowPhase's own
    /// overhead 0 B, Emit 0 B - and the process pass still 641 B a substep.
    /// So it happens either in the loop that calls NarrowPhase, or in
    /// NarrowPhase BEFORE its own counter starts (the two early-outs).
    /// Measured per call, with the first offending call described.</summary>
    public long AllocProcessLoop { get; private set; }

    /// <summary>The same region measured from OUTSIDE the call and from
    /// INSIDE it. Eleven logs in, every counter inside the process pass reads
    /// zero while the pass reads 633 B a substep, and the figure barely moves
    /// when the scene goes from 410 candidate pairs to 52 - so it is not per
    /// pair, and the only code between the counters is a method call. Either
    /// the call itself allocates, which it cannot, or
    /// GC.GetAllocatedBytesForCurrentThread does not measure what I have been
    /// assuming over short windows. These two numbers tell the difference:
    /// if outer is 633 and inner is 0, the counter is the liar, not the code.</summary>
    public long AllocProcessOuter { get; private set; }
    public long AllocProcessInner { get; private set; }

    /// <summary>Substeps that took the PARALLEL narrow phase, and what they
    /// allocated on the calling thread. Twelve logs of hunting ended here:
    /// outside the call 192 784 B, inside the sequential method 0 B, counter
    /// calibration 0 B. The pass was not allocating - it was not always the
    /// pass being called. A settling ball pit crosses 4096 candidate pairs
    /// while it is still dense, takes the parallel branch for those substeps,
    /// and Parallel.ForEach allocates its own machinery on the caller. The
    /// printed "narrow phase sequential" was the LAST substep, which is what
    /// made it look impossible.</summary>
    public long ParallelSubsteps { get; private set; }
    public long AllocBytesParallelPath { get; private set; }

    /// <summary>The solver region split four ways. Removing the sort's
    /// comparer took it from 16 944 B to 4 120 B per 300 substeps
    /// (fourteenth log); the rest is 13.7 B a substep, which is small enough
    /// to be a pool growing now and then rather than work done every step -
    /// but "probably" is how the last thirteen logs started.</summary>
    /// <summary>The stretch of SubStep before contact generation (water,
    /// gravity, fields, drag), the sum of the substep loop as Step sees it,
    /// and Step's own body outside the loop (05.09): named so the 04.09 log's
    /// unexplained 32 B a step can be placed.</summary>
    public long AllocBytesPre { get; private set; }
    public long AllocBytesSubSteps { get; private set; }
    public long AllocBytesStepBody { get; private set; }
    public long AllocSort { get; private set; }
    public long AllocIterations { get; private set; }
    public long AllocWarmCache { get; private set; }
    public long AllocImpacts { get; private set; }
    public int FirstOffenderPair { get; private set; } = -1;
    public long FirstOffenderBytes { get; private set; }
    public string FirstOffenderWhat { get; private set; } = "none";
    public void ResetAllocationDiagnostics()
    {
        AllocBytesContacts = AllocBytesSolver = AllocBytesIntegrate
            = AllocBytesGround = AllocBytesCollect = AllocBytesProcess = AllocBytesMerge = AllocBytesWake
            = AllocBytesEmit = EmittedGround = EmittedProcess
            = AllocSphereSphere = AllocSphereBox = AllocBoxBox = AllocCapsuleAny = AllocNarrowOverhead
            = AllocProcessLoop = FirstOffenderBytes = AllocProcessOuter = AllocProcessInner
         = ParallelSubsteps = AllocBytesParallelPath
         = AllocSort = AllocIterations = AllocWarmCache = AllocImpacts
         = AllocBytesPre = AllocBytesSubSteps = AllocBytesStepBody = 0;
        FirstOffenderPair = -1;
        FirstOffenderWhat = "none";
    }

    [ThreadStatic] private static NarrowScratch? t_scratch;
    private static NarrowScratch Scratch => t_scratch ??= new NarrowScratch();

    private readonly NarrowScratch _scratch0 = new();
    // the pool and the used-list are plain lists under a lock: a
    // ConcurrentQueue allocates a snapshot on every enumeration, and the
    // sequential path enumerated it every substep - 868 B a substep, which
    // Sandbox's allocation-free pin caught on the machine (27.08 log)
    private readonly List<NarrowScratch> _scratchPool = [];
    private readonly List<NarrowScratch> _scratchUsed = [];
    private readonly object _scratchLock = new();
    private PendingContact[] _merge = new PendingContact[1024];

    /// <summary>Below this many candidate pairs the parallel pass costs more
    /// than it saves; the work goes through the same collect / process /
    /// merge path on the calling thread, so the result is the same.</summary>
    private const int ParallelPairThreshold = 4096;

    private void GenerateContacts()
    {
        // ground plane, sequential, first - the order the old pass produced.
        // Pair indices below zero keep these ahead of every body pair after
        // the merge sort, in body order.
        long g0 = GC.GetAllocatedBytesForCurrentThread();
        long EmittedGroundBefore = EmittedGround;
        _scratch0.Pending.Clear();
        t_scratch = _scratch0;
        int bodyCount = Bodies.Count;
        for (int bi = 0; EnableGroundPlane && bi < bodyCount; bi++)
        {
            var body = Bodies[bi];
            if (body.Inactive) continue;
            _scratch0.Pair = bi - bodyCount;
            _scratch0.Seq = 0;
            _scratch0.Margin = MathF.Min(SpeculativeCap, body.Velocity.Length() * FixedStep);
            foreach (ref var p in body.Proxies.AsSpan())
            {
                switch (p.Shape)
                {
                    case ShapeType.Sphere: SpherePlane(in p); break;
                    case ShapeType.Box: BoxPlane(in p); break;
                    case ShapeType.Capsule: CapsulePlane(in p); break;
                }
            }
        }
        _pairCount = 0;
        long g1 = GC.GetAllocatedBytesForCurrentThread();
        AllocBytesGround += g1 - g0;
        EmittedGround += _scratch0.Pending.Count;

        // --- broad phase ---
        // Small bodies go into a uniform spatial hash so we only test nearby pairs instead
        // of every pair (the old O(n^2) loop is what made big collapses crawl). A few "huge"
        // bodies (the arena walls) would blow up the cell size, so they're tested separately
        // against everything - there are only a handful of them.
        int count = Bodies.Count;

        float maxR = 0f;
        for (int i = 0; i < count; i++)
        {
            float r = Bodies[i].BoundingRadius;
            if (r <= HugeRadius && r > maxR) maxR = r;
        }
        _cellSize = Math.Clamp(2f * maxR, 0.6f, 2f * HugeRadius);

        if (_next.Length < count) _next = new int[Math.Max(count, 64)];
        _cellHead.Clear();

        for (int i = 0; i < count; i++)
        {
            var b = Bodies[i];
            if (b.BoundingRadius > HugeRadius) continue; // huge bodies handled below
            long key = CellKey(b.Position);
            _next[i] = _cellHead.TryGetValue(key, out int head) ? head : -1;
            _cellHead[key] = i;
        }

        // COLLECT. small vs small: scan each body's 3x3x3 cell neighbourhood;
        // j>i keeps each pair once. A pair of two inactive bodies is dropped
        // here (see the note above); the bounding-sphere test stays in the
        // process pass so the list is cheap to build.
        for (int i = 0; i < count; i++)
        {
            var a = Bodies[i];
            if (a.BoundingRadius > HugeRadius) continue;
            (int cx, int cy, int cz) = CellCoords(a.Position);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!_cellHead.TryGetValue(PackKey(cx + dx, cy + dy, cz + dz), out int j)) continue;
                        while (j != -1)
                        {
                            if (j > i && !(a.Inactive && Bodies[j].Inactive)) PushPair(i, j);
                            j = _next[j];
                        }
                    }
        }

        // huge bodies (walls) vs everything; keep A = lower index so warm-start keys are stable
        for (int p = 0; p < count; p++)
        {
            if (Bodies[p].BoundingRadius <= HugeRadius) continue;
            for (int q = 0; q < count; q++)
            {
                if (q == p) continue;
                bool qHuge = Bodies[q].BoundingRadius > HugeRadius;
                if (qHuge && q < p) continue; // huge-huge pair handled once
                int lo = Math.Min(p, q), hi = Math.Max(p, q);
                if (!(Bodies[lo].Inactive && Bodies[hi].Inactive)) PushPair(lo, hi);
            }
        }

        // PROCESS
        int pairs = _pairCount;
        long g2 = GC.GetAllocatedBytesForCurrentThread();
        AllocBytesCollect += g2 - g1;
        LastPairCount = pairs;
        LastNarrowPhaseParallel = pairs >= ParallelPairThreshold && Environment.ProcessorCount > 1;
        long p0 = GC.GetAllocatedBytesForCurrentThread();
        if (LastNarrowPhaseParallel)
        {
            ProcessPairsInParallel(pairs);
            ParallelSubsteps++;
            AllocBytesParallelPath += GC.GetAllocatedBytesForCurrentThread() - p0;
        }
        else
        {
            ProcessPairsSequentially(pairs);
        }
        AllocProcessOuter += GC.GetAllocatedBytesForCurrentThread() - p0;

        // MERGE, in pair order
        long g3 = GC.GetAllocatedBytesForCurrentThread();
        AllocBytesProcess += g3 - g2;
        int total = _scratch0.Pending.Count;
        EmittedProcess += total - (EmittedGround - EmittedGroundBefore);
        for (int u = 0; u < _scratchUsed.Count; u++) total += _scratchUsed[u].Pending.Count;
        if (_merge.Length < total) _merge = new PendingContact[Math.Max(total, _merge.Length * 2)];
        int m = 0;
        for (int k = 0; k < _scratch0.Pending.Count; k++) _merge[m++] = _scratch0.Pending[k];
        for (int u = 0; u < _scratchUsed.Count; u++)
        {
            var used = _scratchUsed[u];
            for (int k = 0; k < used.Pending.Count; k++) _merge[m++] = used.Pending[k];
            used.Pending.Clear();
            _scratchPool.Add(used);
        }
        // one scratch means the list is already in pair order: the sequential
        // path does not sort at all
        bool sort = _scratchUsed.Count > 0;
        _scratchUsed.Clear();
        _scratch0.Pending.Clear();
        if (sort) _merge.AsSpan(0, m).Sort(PendingOrder);
        for (int k = 0; k < m; k++)
        {
            ref var pc = ref _merge[k];
            AddContact(pc.A, pc.B, pc.Point, pc.Normal, pc.Penetration);
        }
        AllocBytesMerge += GC.GetAllocatedBytesForCurrentThread() - g3;
    }

    /// <summary>
    /// The parallel path lives in its own method, and that is not tidiness.
    /// Its lambdas are the only closures in the contact phase, and a closure
    /// costs its display class wherever the compiler decides to put it -
    /// which is the enclosing scope, not the branch. Nine logs of hunting a
    /// 640 B/substep allocation ended here: every counter inside
    /// GenerateContacts read zero while the pass as a whole did not, and the
    /// only code left between the counters was the branch that was never
    /// taken. Kept split so the sequential path can never pay for it again.
    /// </summary>
    // The three delegates are built ONCE. Written inline they were rebuilt on
    // every parallel substep - each captures `this`, so the compiler cannot
    // cache them itself - and that was most of the 642 bytes a substep the
    // allocation pin has been reporting since tail457. Parallel.ForEach still
    // allocates its own bookkeeping, which is the price of using it at all.
    private Func<NarrowScratch>? _scratchRent;
    private Func<Tuple<int, int>, System.Threading.Tasks.ParallelLoopState, NarrowScratch, NarrowScratch>? _scratchWork;
    private Action<NarrowScratch>? _scratchReturn;

    private void ProcessPairsInParallel(int pairs)
    {
        _scratchRent ??= () =>
        {
            lock (_scratchLock)
            {
                if (_scratchPool.Count == 0) return new NarrowScratch();
                var sc = _scratchPool[^1];
                _scratchPool.RemoveAt(_scratchPool.Count - 1);
                return sc;
            }
        };
        _scratchWork ??= (range, _, sc) =>
        {
            t_scratch = sc;   // the shape tests reach their scratch through the thread
            for (int k = range.Item1; k < range.Item2; k++)
            {
                sc.Pair = k;
                sc.Seq = 0;
                NarrowPhase(Bodies[_pairs[k].I], Bodies[_pairs[k].J]);
            }
            return sc;
        };
        _scratchReturn ??= sc => { lock (_scratchLock) _scratchUsed.Add(sc); };

        System.Threading.Tasks.Parallel.ForEach(
            System.Collections.Concurrent.Partitioner.Create(0, pairs, Math.Max(256, pairs / (4 * Environment.ProcessorCount))),
            _scratchRent, _scratchWork, _scratchReturn);
    }

    private void ProcessPairsSequentially(int pairs)
    {
        long q0 = GC.GetAllocatedBytesForCurrentThread();
        t_scratch = _scratch0;
        for (int k = 0; k < pairs; k++)
        {
            _scratch0.Pair = k;
            _scratch0.Seq = 0;
            long c0 = GC.GetAllocatedBytesForCurrentThread();
            RigidBody ba = Bodies[_pairs[k].I], bb = Bodies[_pairs[k].J];
            NarrowPhase(ba, bb);
            long spent = GC.GetAllocatedBytesForCurrentThread() - c0;
            AllocProcessLoop += spent;
            if (spent > 0 && FirstOffenderPair < 0)
            {
                FirstOffenderPair = k;
                FirstOffenderBytes = spent;
                FirstOffenderWhat = $"bodies {_pairs[k].I}/{_pairs[k].J}, "
                    + $"{(ba.Children.Length > 0 ? ba.Children[0].Shape.ToString() : "?")}-"
                    + $"{(bb.Children.Length > 0 ? bb.Children[0].Shape.ToString() : "?")}, "
                    + $"inactive {ba.Inactive}/{bb.Inactive}";
            }
        }
        AllocProcessInner += GC.GetAllocatedBytesForCurrentThread() - q0;
    }

    /// <summary>
    /// The contact order the solver depends on (see the note at the call
    /// site), sorted WITHOUT a comparer. List.Sort with an IComparer wraps it
    /// per call and cost 56 B a substep - the last managed allocation in a
    /// settled scene, and the one that kept the allocation pin red after the
    /// narrow phase was cleared (27.08, thirteenth log).
    ///
    /// Instead the key is materialised: height along the up axis, negated so
    /// the top sorts first, with X and Z folded in as tie-breakers at a scale
    /// far below any real height difference. Array.Sort(float[], T[]) on a
    /// primitive key takes the comparer-free path.
    ///
    /// The tie-breakers keep the four points of one manifold in a
    /// deterministic order across substeps, which is what the original
    /// comparer was doing with its second and third comparisons; folding them
    /// into the key is exact enough for that, because points within a
    /// manifold differ by centimetres in X and Z and by nothing at all in
    /// height.
    ///
    /// WHICH order the four corners take is not cosmetic (02.09). The old
    /// tie-breaker (X, then Z) walked a face row by row: corner (-,-),
    /// (-,+), (+,-), (+,+). Each corner solved alone kicks the box about the
    /// axis through the others, and a row-major walk stacks two kicks the
    /// same way before the far corners answer them - a perfectly symmetric
    /// column of nine boxes had ωx and vz on its SECOND substep, and that
    /// rocking walks the stack sideways through friction. The key below
    /// pairs OPPOSITE corners (the sign of X·Z): (+,+) with (-,-), then
    /// (-,+) with (+,-), so every second corner cancels the torque of the
    /// one before it. Measured on the shipped tower (Tools/twins/
    /// sandbox_stack_twin.py, SandboxTests): with the warm start below in
    /// place, all-wood drift 34.0 -> 4.4 mm, all-stone 6.3 -> 14.0 (the one
    /// row it does not improve), shipped mixed 4.4 -> 3.5. For a two-point
    /// edge manifold or a yawed box the key is just a different arbitrary
    /// but deterministic tie-breaker.
    /// </summary>
    private void SortContactsTopDown()
    {
        int n = _contactCount;
        if (n < 2) return;
        if (_sortKeys.Length < n)
        {
            _sortKeys = new float[Math.Max(n, _sortKeys.Length * 2)];
            _sortItems = new Contact[Math.Max(n, _sortItems.Length * 2)];
        }
        for (int i = 0; i < n; i++)
        {
            Contact c = _contacts[i];
            _sortKeys[i] = -Vector3.Dot(c.Point, _solveOrderUp)
                           - c.Point.X * c.Point.Z * 1e-4f + c.Point.X * 1e-8f;
            _sortItems[i] = c;
        }
        Array.Sort(_sortKeys, _sortItems, 0, n);
        for (int i = 0; i < n; i++) _contacts[i] = _sortItems[i];
    }

    private float[] _sortKeys = new float[256];
    private Contact[] _sortItems = new Contact[256];

    private void PushPair(int i, int j)
    {
        if (Bodies[i].CollisionGroup > 0 && Bodies[i].CollisionGroup == Bodies[j].CollisionGroup) return;
        if (_pairCount == _pairs.Length) Array.Resize(ref _pairs, _pairs.Length * 2);
        _pairs[_pairCount++] = new PairRef(i, j);
    }

    /// <summary>What every shape test calls: records the contact for the
    /// merge. Pure - touches only this thread's scratch.</summary>
    private void Emit(RigidBody a, RigidBody b, Vector3 point, Vector3 normalAtoB, float penetration)
    {
        long e0 = GC.GetAllocatedBytesForCurrentThread();
        var sc = Scratch;
        sc.Pending.Add(new PendingContact
        {
            Pair = sc.Pair, Seq = sc.Seq++,
            A = a, B = b, Point = point, Normal = normalAtoB, Penetration = penetration,
        });
        AllocBytesEmit += GC.GetAllocatedBytesForCurrentThread() - e0;
    }

    /// <summary>
    /// A sleeper wakes on IMPACT only (AddContact) - a resting contact must
    /// not wake it, or stacks would keep each other awake forever. The gap
    /// that leaves (25.08, Demolition screenshot): a brick whose supporter
    /// is knocked out from under it is never touched again, so it sleeps
    /// in mid-air. Sleeping is a claim - "nothing under me has moved" - and
    /// this pass checks the claim: every AWAKE, MOVING body wakes any
    /// sleeper resting within contact range ABOVE it. The woken body then
    /// either finds another supporter and sleeps again in SleepDelay, or
    /// falls - and falling, it wakes what rested on it in turn, so a wall
    /// unzips from the hole upward instead of hanging. Uses the spatial
    /// hash GenerateContacts just built; only awake bodies scan, so a
    /// sleeping wall costs nothing.
    /// </summary>
    private void WakeTheUnsupported()
    {
        int count = Bodies.Count;
        if (_next.Length < count) return;
        for (int i = 0; i < count; i++)
        {
            var b = Bodies[i];
            if (b.Inactive || b.BoundingRadius > HugeRadius) continue;
            // "support leaving" means the mover is FALLING (or flung): a brick
            // jostled at the sleep threshold must not wake its neighbours -
            // tail387's first cut did, and one impact woke all 414 bricks at
            // once; a tall stack awake in an iterative solver bows like cloth
            // (screenshot 26.08). Down at 0.25 m/s, or 1 m/s any direction.
            float vUp = Vector3.Dot(b.Velocity, _solveOrderUp);
            if (vUp > -SupportLeaveSpeed && b.Velocity.LengthSquared() < FlungSpeed * FlungSpeed) continue;
            (int cx, int cy, int cz) = CellCoords(b.Position);
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    for (int dz = -1; dz <= 1; dz++)
                    {
                        if (!_cellHead.TryGetValue(PackKey(cx + dx, cy + dy, cz + dz), out int j)) continue;
                        while (j != -1)
                        {
                            var s = Bodies[j];
                            if (s.Sleeping && !s.IsStatic)
                            {
                                Vector3 d = s.Position - b.Position;
                                float rr = s.BoundingRadius + b.BoundingRadius;
                                if (Vector3.Dot(d, _solveOrderUp) > 0 && d.LengthSquared() < rr * rr)
                                    s.Wake();
                            }
                            j = _next[j];
                        }
                    }
        }
    }

    private const float SupportLeaveSpeed = 0.25f;
    private const float FlungSpeed = 1.0f;
    private const float HugeRadius = 4f;
    private readonly Dictionary<long, int> _cellHead = new();
    private int[] _next = [];
    private float _cellSize = 1f;

    private (int, int, int) CellCoords(Vector3 p)
        => ((int)MathF.Floor(p.X / _cellSize), (int)MathF.Floor(p.Y / _cellSize), (int)MathF.Floor(p.Z / _cellSize));

    private long CellKey(Vector3 p)
    {
        var (cx, cy, cz) = CellCoords(p);
        return PackKey(cx, cy, cz);
    }

    // exact (collision-free) packing of cell coords in +/-2^20 cells into one long
    private static long PackKey(int x, int y, int z)
        => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (long)(z & 0x1FFFFF);

    private void NarrowPhase(RigidBody a, RigidBody b)
    {
        if (a.Inactive && b.Inactive) return;

        var sc = Scratch;
        sc.Margin = PairMargin(a, b);
        float rr = a.BoundingRadius + b.BoundingRadius + sc.Margin;
        if ((b.Position - a.Position).LengthSquared() > rr * rr) return;

        long n0 = GC.GetAllocatedBytesForCurrentThread();
        long dispatched = 0;
        foreach (ref var pa in a.Proxies.AsSpan())
            foreach (ref var pb in b.Proxies.AsSpan())
            {
                float prr = pa.BoundingRadius + pb.BoundingRadius + sc.Margin;
                if ((pb.Position - pa.Position).LengthSquared() > prr * prr) continue;
                long d0 = GC.GetAllocatedBytesForCurrentThread();
                var (ka, kb) = (pa.Shape, pb.Shape);
                Dispatch(in pa, in pb);
                long spent = GC.GetAllocatedBytesForCurrentThread() - d0;
                dispatched += spent;
                if (ka == ShapeType.Capsule || kb == ShapeType.Capsule) AllocCapsuleAny += spent;
                else if (ka == ShapeType.Sphere && kb == ShapeType.Sphere) AllocSphereSphere += spent;
                else if (ka == ShapeType.Box && kb == ShapeType.Box) AllocBoxBox += spent;
                else AllocSphereBox += spent;
            }
        AllocNarrowOverhead += GC.GetAllocatedBytesForCurrentThread() - n0 - dispatched;
    }

    private void Dispatch(in ShapeProxy a, in ShapeProxy b)
    {
        var (sa, sb) = (a.Shape, b.Shape);
        if (sa == ShapeType.Sphere && sb == ShapeType.Sphere) SphereSphere(in a, in b);
        else if (sa == ShapeType.Sphere && sb == ShapeType.Box) SphereBox(in a, in b, flip: false);
        else if (sa == ShapeType.Box && sb == ShapeType.Sphere) SphereBox(in b, in a, flip: true);
        else if (sa == ShapeType.Box && sb == ShapeType.Box) BoxBox(in a, in b);
        else if (sa == ShapeType.Capsule && sb == ShapeType.Capsule) CapsuleCapsule(in a, in b);
        else if (sa == ShapeType.Capsule && sb == ShapeType.Sphere) CapsuleSphere(in a, in b, flip: false);
        else if (sa == ShapeType.Sphere && sb == ShapeType.Capsule) CapsuleSphere(in b, in a, flip: true);
        else if (sa == ShapeType.Capsule && sb == ShapeType.Box) CapsuleBox(in a, in b, flip: false);
        else CapsuleBox(in b, in a, flip: true);
    }

    private void AddContact(RigidBody a, RigidBody b, Vector3 point, Vector3 normalAtoB, float penetration)
    {
        // One-way debris: fracture debris (DebrisLife >= 0) is pushed around by the world and by big bodies,
        // but it never pushes a non-debris body, and debris does not collide with other debris. This stops
        // big piles of shards from turning into a churning mush and shoving the real objects around.
        bool aDebris = a.DebrisLife >= 0f, bDebris = b.DebrisLife >= 0f;
        if (aDebris && bDebris) return;   // debris ignores debris entirely

        // a sleeping body only wakes if the thing touching it actually approaches with
        // some speed; a resting contact must NOT wake it, or stacks would keep each
        // other awake forever and sleeping would be useless
        if (a.Sleeping != b.Sleeping)
        {
            var sleeper = a.Sleeping ? a : b;
            var awake = a.Sleeping ? b : a;
            float approach = MathF.Abs(Vector3.Dot(
                awake.VelocityAt(point) - sleeper.VelocityAt(point), normalAtoB));

            // A speculative contact (penetration < 0) is a body that has NOT
            // touched anything, so the plain approach test is not enough: a
            // ball flying PAST a wall is inside the speculative reach for a
            // whole stretch of its flight and would wake the wall on the way
            // by. It wakes the sleeper only when it is about to LAND on it -
            // when the approach closes the remaining gap within this substep.
            //
            // Getting this wrong is not cosmetic (27.08, tail468 shipped it):
            // a sleeping body is infinite mass to the solver, so the ball was
            // braked against a brick that never woke, crept to the wall and
            // stopped without hitting anything. The balls demolished nothing.
            bool aboutToLand = penetration > 0f || approach * FixedStep >= -penetration;
            if (aboutToLand && (approach > WakeImpactSpeed || awake == Grabbed))
                sleeper.Wake();
        }

        // If exactly one side is debris, the other (the solid body) is immovable for this contact,
        // so only the debris responds.
        // acquire from the pool; every field is (re)assigned here or in Presolve
        Contact c;
        if (_contactCount < _contacts.Count)
        {
            c = _contacts[_contactCount];
        }
        else
        {
            c = new Contact();
            _contacts.Add(c);
        }
        _contactCount++;
        c.A = a; c.B = b;
        c.Point = point;
        c.Normal = normalAtoB;
        c.Penetration = penetration;
        c.SkipA = bDebris;
        c.SkipB = aDebris;
    }

    // ---- vs ground plane (y = 0) ----

    private void SpherePlane(in ShapeProxy s)
    {
        float dist = s.Position.Y - s.Radius;
        if (dist < Scratch.Margin)   // negative -dist = a speculative gap
            Emit(Ground, s.Owner, s.Position - new Vector3(0, s.Radius, 0), Vector3.UnitY, -dist);
    }

    private void BoxPlane(in ShapeProxy b)
    {
        for (int sx = -1; sx <= 1; sx += 2)
            for (int sy = -1; sy <= 1; sy += 2)
                for (int sz = -1; sz <= 1; sz += 2)
                {
                    var local = new Vector3(sx * b.HalfExtents.X, sy * b.HalfExtents.Y, sz * b.HalfExtents.Z);
                    var corner = b.Position + Vector3.Transform(local, b.Rotation);
                    if (corner.Y < 0f)
                        Emit(Ground, b.Owner, corner, Vector3.UnitY, -corner.Y);
                }
    }

    private void CapsulePlane(in ShapeProxy c)
    {
        c.CapsuleSegment(out var p0, out var p1);
        foreach (var p in stackalloc[] { p0, p1 })
        {
            float dist = p.Y - c.Radius;
            if (dist < 0f)
                Emit(Ground, c.Owner, p - new Vector3(0, c.Radius, 0), Vector3.UnitY, -dist);
        }
    }

    // ---- sphere pairs ----

    private void SphereSphere(in ShapeProxy a, in ShapeProxy b)
    {
        if (SphereContacts.TryContact(a.Position, a.Radius, b.Position, b.Radius, Scratch.Margin,
                out var n, out var point, out float penetration))
            Emit(a.Owner, b.Owner, point, n, penetration); // negative = speculative gap
    }

    private void SphereBox(in ShapeProxy s, in ShapeProxy box, bool flip)
    {
        if (!SphereObbCore(s.Position, s.Radius, in box,
                out var point, out var normalBoxToSphere, out float penetration, Scratch.Margin))
            return;

        if (flip) Emit(box.Owner, s.Owner, point, normalBoxToSphere, penetration);
        else Emit(s.Owner, box.Owner, point, -normalBoxToSphere, penetration);
    }

    /// <summary>Sphere (center, radius) vs OBB. Handles the deep case (center inside the box).</summary>
    /// <param name="margin">Speculative reach: a sphere this far OUTSIDE the
    /// box still reports a contact, with a negative penetration equal to the
    /// gap. Zero keeps the historic touch-only behaviour, which is what the
    /// capsule path asks for.</param>
    private static bool SphereObbCore(Vector3 center, float radius, in ShapeProxy box,
        out Vector3 point, out Vector3 normalBoxToSphere, out float penetration, float margin = 0f)
    {
        var invRot = Quaternion.Conjugate(box.Rotation);
        var local = Vector3.Transform(center - box.Position, invRot);
        var h = box.HalfExtents;
        var clamped = Vector3.Clamp(local, -h, h);

        if (clamped == local)
        {
            // center is inside the box: push out through the nearest face
            float dx = h.X - MathF.Abs(local.X);
            float dy = h.Y - MathF.Abs(local.Y);
            float dz = h.Z - MathF.Abs(local.Z);

            Vector3 nLocal;
            float depth;
            if (dx <= dy && dx <= dz) { nLocal = new Vector3(MathF.Sign(local.X), 0, 0); depth = dx; }
            else if (dy <= dz) { nLocal = new Vector3(0, MathF.Sign(local.Y), 0); depth = dy; }
            else { nLocal = new Vector3(0, 0, MathF.Sign(local.Z)); depth = dz; }
            if (nLocal == Vector3.Zero) nLocal = Vector3.UnitY;

            normalBoxToSphere = Vector3.Transform(nLocal, box.Rotation);
            point = center;
            penetration = radius + depth;
            return true;
        }

        var delta = local - clamped;
        float dist = delta.Length();
        if (dist >= radius + margin)
        {
            point = default; normalBoxToSphere = default; penetration = 0f;
            return false;
        }

        normalBoxToSphere = Vector3.Transform(delta / dist, box.Rotation);
        point = box.Position + Vector3.Transform(clamped, box.Rotation);
        penetration = radius - dist;
        return true;
    }

    // ---- capsule pairs ----

    private void CapsuleSphere(in ShapeProxy cap, in ShapeProxy s, bool flip)
    {
        cap.CapsuleSegment(out var p0, out var p1);
        var closest = ClosestPtSegmentPoint(p0, p1, s.Position);

        var d = s.Position - closest;
        float dist = d.Length();
        float sum = cap.Radius + s.Radius;
        if (dist >= sum) return;

        var n = dist > 1e-6f ? d / dist : Vector3.UnitY;
        var point = closest + n * cap.Radius;

        if (flip) Emit(s.Owner, cap.Owner, point, -n, sum - dist);
        else Emit(cap.Owner, s.Owner, point, n, sum - dist);
    }

    private void CapsuleCapsule(in ShapeProxy a, in ShapeProxy b)
    {
        a.CapsuleSegment(out var a0, out var a1);
        b.CapsuleSegment(out var b0, out var b1);
        ClosestPtSegmentSegment(a0, a1, b0, b1, out var pA, out var pB);

        var d = pB - pA;
        float dist = d.Length();
        float sum = a.Radius + b.Radius;
        if (dist >= sum) return;

        var n = dist > 1e-6f ? d / dist : Vector3.UnitY;
        Emit(a.Owner, b.Owner, (pA + n * a.Radius + pB - n * b.Radius) * 0.5f, n, sum - dist);
    }

    private void CapsuleBox(in ShapeProxy cap, in ShapeProxy box, bool flip)
    {
        cap.CapsuleSegment(out var p0, out var p1);

        var invRot = Quaternion.Conjugate(box.Rotation);
        var l0 = Vector3.Transform(p0 - box.Position, invRot);
        var l1 = Vector3.Transform(p1 - box.Position, invRot);
        var h = box.HalfExtents;

        // squared distance from a point of the segment to the box is convex in t,
        // so a plain ternary search finds the deepest spot - no special cases needed
        float DistSq(float t)
        {
            var p = Vector3.Lerp(l0, l1, t);
            return (p - Vector3.Clamp(p, -h, h)).LengthSquared();
        }

        float lo = 0f, hi = 1f;
        for (int i = 0; i < 48; i++)
        {
            float m1 = lo + (hi - lo) / 3f;
            float m2 = hi - (hi - lo) / 3f;
            if (DistSq(m1) <= DistSq(m2)) hi = m2; else lo = m1;
        }
        float tBest = (lo + hi) * 0.5f;

        // also probe both ends: a capsule lying flat on a box needs more than one
        // contact point or it would wobble like a seesaw
        float lastT = float.MinValue;
        foreach (float t in stackalloc[] { tBest, 0f, 1f })
        {
            if (MathF.Abs(t - lastT) < 0.05f) continue;

            var center = Vector3.Lerp(p0, p1, t);
            if (!SphereObbCore(center, cap.Radius, in box,
                    out var point, out var normalBoxToCap, out float pen))
                continue;

            lastT = t;
            if (flip) Emit(box.Owner, cap.Owner, point, normalBoxToCap, pen);
            else Emit(cap.Owner, box.Owner, point, -normalBoxToCap, pen);
        }
    }

    private static Vector3 ClosestPtSegmentPoint(Vector3 a, Vector3 b, Vector3 p)
    {
        var ab = b - a;
        float t = Vector3.Dot(p - a, ab) / MathF.Max(ab.LengthSquared(), 1e-9f);
        return a + ab * Math.Clamp(t, 0f, 1f);
    }

    /// <summary>Closest points between two segments (Ericson, Real-Time Collision Detection 5.1.9).</summary>
    private static void ClosestPtSegmentSegment(
        Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2, out Vector3 c1, out Vector3 c2)
    {
        var d1 = q1 - p1;
        var d2 = q2 - p2;
        var r = p1 - p2;
        float a = d1.LengthSquared();
        float e = d2.LengthSquared();
        float f = Vector3.Dot(d2, r);
        float s, t;

        if (a <= 1e-9f && e <= 1e-9f) { c1 = p1; c2 = p2; return; }

        if (a <= 1e-9f) { s = 0f; t = Math.Clamp(f / e, 0f, 1f); }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-9f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
            else
            {
                float b = Vector3.Dot(d1, d2);
                float denom = a * e - b * b;
                s = denom > 1e-9f ? Math.Clamp((b * f - c * e) / denom, 0f, 1f) : 0f;
                t = (b * s + f) / e;
                if (t < 0f) { t = 0f; s = Math.Clamp(-c / a, 0f, 1f); }
                else if (t > 1f) { t = 1f; s = Math.Clamp((b - c) / a, 0f, 1f); }
            }
        }

        c1 = p1 + d1 * s;
        c2 = p2 + d2 * t;
    }

    // ---- OBB vs OBB: SAT over all 15 axes, then face clipping or edge-edge ----

    // scratch axes for BoxBox live on the THREAD's NarrowScratch (27.08):
    // the narrow phase runs in parallel over pairs, and two arrays shared by
    // the world would be two arrays shared by every thread
    private void BoxBox(in ShapeProxy a, in ShapeProxy b)
    {
        var sc = Scratch;
        Vector3[] axesA = sc.AxesA;
        axesA[0] = a.Axis(0); axesA[1] = a.Axis(1); axesA[2] = a.Axis(2);
        Vector3[] axesB = sc.AxesB;
        axesB[0] = b.Axis(0); axesB[1] = b.Axis(1); axesB[2] = b.Axis(2);
        var d = b.Position - a.Position;
        var ea = a.HalfExtents;
        var eb = b.HalfExtents;

        float bestPen = float.MaxValue;
        Vector3 bestAxis = Vector3.UnitY;
        int bestType = -1;                 // 0 = face of A, 1 = face of B, 2 = edge-edge
        int bestI = 0, bestJ = 0;

        bool Test(Vector3 axis, int type, int i, int j)
        {
            float lenSq = axis.LengthSquared();
            if (lenSq < 1e-8f) return true; // near-parallel edges, the cross product is junk
            axis /= MathF.Sqrt(lenSq);

            float ra = ea.X * MathF.Abs(Vector3.Dot(axesA[0], axis))
                     + ea.Y * MathF.Abs(Vector3.Dot(axesA[1], axis))
                     + ea.Z * MathF.Abs(Vector3.Dot(axesA[2], axis));
            float rb = eb.X * MathF.Abs(Vector3.Dot(axesB[0], axis))
                     + eb.Y * MathF.Abs(Vector3.Dot(axesB[1], axis))
                     + eb.Z * MathF.Abs(Vector3.Dot(axesB[2], axis));

            float proj = Vector3.Dot(d, axis);
            float pen = ra + rb - MathF.Abs(proj);
            if (pen < 0f) return false;    // found a separating axis, no collision

            // edge axes carry a 5% penalty: an edge-edge pair gives a single contact
            // point, a face gives a whole clipped manifold, so when penetrations are
            // close we'd much rather pick the face. (Getting the sign of this bias
            // wrong selects edges for plain stacked boxes - ask me how I know.)
            float effective = type == 2 ? pen * 1.05f + 1e-4f : pen;
            if (effective < bestPen)
            {
                bestPen = effective;
                bestAxis = proj < 0f ? -axis : axis; // orient from A toward B
                bestType = type;
                bestI = i; bestJ = j;
            }
            return true;
        }

        for (int i = 0; i < 3; i++) if (!Test(axesA[i], 0, i, 0)) return;
        for (int j = 0; j < 3; j++) if (!Test(axesB[j], 1, 0, j)) return;
        for (int i = 0; i < 3; i++)
            for (int j = 0; j < 3; j++)
                if (!Test(Vector3.Cross(axesA[i], axesB[j]), 2, i, j)) return;

        if (bestType == 2)
        {
            EdgeEdgeContact(in a, in b, axesA, axesB, bestAxis, bestI, bestJ);
            return;
        }

        bool refIsA = bestType == 0;
        ref readonly var refBox = ref refIsA ? ref a : ref b;
        ref readonly var incBox = ref refIsA ? ref b : ref a;
        var refNormal = refIsA ? bestAxis : -bestAxis; // points from refBox toward incBox
        int refAxisIdx = refIsA ? bestI : bestJ;

        // incident face = the face of the other box looking most directly back at us
        int incAxisIdx = 0;
        float minDot = float.MaxValue;
        float incSign = 1f;
        for (int i = 0; i < 3; i++)
        {
            float dot = Vector3.Dot(incBox.Axis(i), refNormal);
            if (dot < minDot) { minDot = dot; incAxisIdx = i; incSign = 1f; }
            if (-dot < minDot) { minDot = -dot; incAxisIdx = i; incSign = -1f; }
        }

        var incNormal = incBox.Axis(incAxisIdx) * incSign;
        int iu = (incAxisIdx + 1) % 3, iv = (incAxisIdx + 2) % 3;
        var incU = incBox.Axis(iu) * Comp(incBox.HalfExtents, iu);
        var incV = incBox.Axis(iv) * Comp(incBox.HalfExtents, iv);
        var incCenter = incBox.Position + incNormal * Comp(incBox.HalfExtents, incAxisIdx);

        List<Vector3> poly = sc.Poly;
        poly.Clear();
        poly.Add(incCenter + incU + incV);
        poly.Add(incCenter + incU - incV);
        poly.Add(incCenter - incU - incV);
        poly.Add(incCenter - incU + incV);

        // Sutherland-Hodgman: shave the incident face down by the 4 side planes
        // of the reference face; whatever survives below the face is the manifold
        int ru = (refAxisIdx + 1) % 3, rv = (refAxisIdx + 2) % 3;
        var axisU = refBox.Axis(ru);
        var axisV = refBox.Axis(rv);
        ClipPolygon(poly, axisU, Vector3.Dot(refBox.Position, axisU) + Comp(refBox.HalfExtents, ru));
        ClipPolygon(poly, -axisU, -Vector3.Dot(refBox.Position, axisU) + Comp(refBox.HalfExtents, ru));
        ClipPolygon(poly, axisV, Vector3.Dot(refBox.Position, axisV) + Comp(refBox.HalfExtents, rv));
        ClipPolygon(poly, -axisV, -Vector3.Dot(refBox.Position, axisV) + Comp(refBox.HalfExtents, rv));

        float faceOffset = Vector3.Dot(refBox.Position, refNormal) + Comp(refBox.HalfExtents, refAxisIdx);
        var normalAtoB = refIsA ? refNormal : -refNormal;

        foreach (var p in poly)
        {
            float sep = Vector3.Dot(p, refNormal) - faceOffset;
            if (sep < 0f)
                Emit(a.Owner, b.Owner, p - refNormal * (sep * 0.5f), normalAtoB, -sep);
        }
    }

    private void EdgeEdgeContact(in ShapeProxy a, in ShapeProxy b,
        Vector3[] axesA, Vector3[] axesB, Vector3 normalAtoB, int edgeAxisA, int edgeAxisB)
    {
        // pick the edge of each box closest to the other one: start at the center
        // and walk to the corner along the two non-edge axes, choosing signs that
        // move toward the contact
        var pA = a.Position;
        for (int k = 0; k < 3; k++)
        {
            if (k == edgeAxisA) continue;
            float s = Vector3.Dot(axesA[k], normalAtoB) > 0f ? 1f : -1f;
            pA += axesA[k] * (s * Comp(a.HalfExtents, k));
        }

        var pB = b.Position;
        for (int k = 0; k < 3; k++)
        {
            if (k == edgeAxisB) continue;
            float s = Vector3.Dot(axesB[k], -normalAtoB) > 0f ? 1f : -1f;
            pB += axesB[k] * (s * Comp(b.HalfExtents, k));
        }

        var d1 = axesA[edgeAxisA];
        var d2 = axesB[edgeAxisB];
        var r = pA - pB;
        float bDot = Vector3.Dot(d1, d2);
        float c = Vector3.Dot(d1, r);
        float f = Vector3.Dot(d2, r);
        float denom = 1f - bDot * bDot;
        if (MathF.Abs(denom) < 1e-6f) return; // parallel edges - the face case will pick this up

        float s1 = Math.Clamp((bDot * f - c) / denom, -Comp(a.HalfExtents, edgeAxisA), Comp(a.HalfExtents, edgeAxisA));
        float t1 = Math.Clamp((f - bDot * c) / denom, -Comp(b.HalfExtents, edgeAxisB), Comp(b.HalfExtents, edgeAxisB));

        var cA = pA + d1 * s1;
        var cB = pB + d2 * t1;

        float depth = Vector3.Dot(cA - cB, normalAtoB);
        if (depth < 0f) depth = 0.001f;

        Emit(a.Owner, b.Owner, (cA + cB) * 0.5f, normalAtoB, depth);
    }

    private static float Comp(Vector3 v, int i) => i == 0 ? v.X : i == 1 ? v.Y : v.Z;

    // scratch buffers for the Sutherland-Hodgman clipping: a face manifold used
    // to allocate one polygon list plus one output list per clip plane (about
    // five lists per touching box pair, every substep) - now zero
    private static void ClipPolygon(List<Vector3> poly, Vector3 n, float offset)
    {
        if (poly.Count == 0) return;
        List<Vector3> output = Scratch.Clip;
        output.Clear();

        for (int i = 0; i < poly.Count; i++)
        {
            var cur = poly[i];
            var next = poly[(i + 1) % poly.Count];
            float dCur = Vector3.Dot(cur, n) - offset;
            float dNext = Vector3.Dot(next, n) - offset;

            if (dCur <= 0f) output.Add(cur);
            if (dCur * dNext < 0f)
            {
                float t = dCur / (dCur - dNext);
                output.Add(cur + (next - cur) * t);
            }
        }

        poly.Clear();
        for (int i = 0; i < output.Count; i++) poly.Add(output[i]);
    }

    // ================= solver =================

    private void Presolve(Contact c, float h)
    {
        c.RA = c.Point - c.A.Position;
        c.RB = c.Point - c.B.Position;
        var n = c.Normal;

        c.MassNormal = 1f / EffectiveMass(c, n);

        var t1 = Vector3.Normalize(Vector3.Cross(n, MathF.Abs(n.X) > 0.7f ? Vector3.UnitY : Vector3.UnitX));
        var t2 = Vector3.Cross(n, t1);
        c.Tangent1 = t1;
        c.Tangent2 = t2;
        c.MassTangent1 = 1f / EffectiveMass(c, t1);
        c.MassTangent2 = 1f / EffectiveMass(c, t2);

        float vn = Vector3.Dot(RelativeVelocity(c), n);
        float restitution = MathF.Max(c.A.Restitution, c.B.Restitution);
        c.VelocityBias = vn < -RestitutionThreshold ? -restitution * vn : 0f;
        c.ImpactSpeed = vn < 0f ? -vn : 0f;

        if (c.Penetration < 0f)
        {
            // SPECULATIVE: the pair is -c.Penetration metres APART.
            //
            // Two rules, and tail468 got both wrong in the same line.
            //
            // While the pair is still going to be apart at the end of this
            // substep, the constraint only stops the OVERSHOOT: the target
            // normal velocity is exactly the gap per substep, so nothing is
            // slowed that would not have touched anyway. Putting restitution
            // here (tail468) made a pair separate before it ever touched -
            // the thrown ball rebounded ten centimetres short of the wall and
            // demolished nothing.
            //
            // When the pair LANDS this substep, the impact is happening now
            // and gets its bounce. That is at most one substep early, which
            // at 12 m/s is one frame's worth of position and invisible, and
            // it keeps the energy: without it the twin measured the target
            // leaving at 11.07 m/s instead of 15.07, and the dropped ball
            // rebounding 474 mm instead of 1742.
            float gap = -c.Penetration;
            bool lands = -vn * h >= gap;
            c.VelocityBias = lands
                ? MathF.Max(c.VelocityBias, c.Penetration / h)
                : c.Penetration / h;
            c.PositionBias = 0f;
        }
        else
        {
            c.PositionBias = MathF.Min(Beta / h * MathF.Max(0f, c.Penetration - SlopFor(c)), 4f);
        }
        c.Pn = c.Pt1 = c.Pt2 = c.Pnb = 0f;

        // WARM START, seed half. The accumulators take last substep's
        // impulses here; WarmStart() applies them to the velocities in a
        // pass of its own once EVERY contact has been through Presolve.
        //
        // History, because the wrong lesson was written here for twelve days.
        // 21.08 (tail247) found that applying the cached impulse collapsed
        // every stack deeper than seven boxes and kept the seed WITHOUT the
        // application - a "tepid" start. That variant is not a cache but an
        // integrator: nothing ever pays the seeded impulse back, so every
        // substep adds the full weight again and the cached Pn grows without
        // bound - measured 02.09 on the shipped tower: 0.8 after 0.1 s, 11.0
        // after 1.2 s, against a true per-substep weight impulse of 0.38.
        // The friction clamp mu*Pn was therefore meaningless at rest, and the
        // "poison" the A/B saw in the applied form was that runaway seed
        // being applied for real.
        //
        // The second half of the story: the 21.08 A/B applied the impulse
        // HERE, inside Presolve, contact by contact, so every contact later
        // in the top-down list measured its vn (restitution, the speculative
        // landing test, ImpactSpeed) on velocities the earlier contacts had
        // already warmed. That form buzzes - a nine-box column bounced
        // vertically at +-0.5 m/s and never slept. Applied as a separate
        // pass after all Presolves, the canonical order (Box2D's), the same
        // column stands: 5873.7 mm of drift became 14.5 on the twin
        // geometry, and the shipped tower 85.3 -> 3.5 mm with the corner
        // order above. The accumulated impulse is now what its name says.
        if (_warmCache.TryGetValue((c.A, c.B), out var cached))
        {
            var localA = Vector3.Transform(c.Point - c.A.Position, Quaternion.Conjugate(c.A.Rotation));
            var span = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(cached);
            int best = -1;
            float bestD = WarmStartMatchDistSq;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Used) continue;    // one cache entry feeds ONE point
                float d = (span[i].LocalA - localA).LengthSquared();
                if (d < bestD) { bestD = d; best = i; }
            }
            if (best >= 0)
            {
                span[best].Used = true;
                c.Pn = span[best].Pn;
                c.Pt1 = span[best].Pt1;
                c.Pt2 = span[best].Pt2;
            }
        }
    }

    /// <summary>Warm start, apply half: the seeded accumulators become
    /// velocity, in one pass AFTER every Presolve (see the note there). A
    /// contact that matched nothing carries zero and applies nothing; the
    /// solver's clamped accumulation can take the whole seed back within the
    /// same substep if the pair has since separated.</summary>
    private void WarmStart()
    {
        for (int ci = 0; ci < _contactCount; ci++)
        {
            Contact c = _contacts[ci];
            if (c is { Pn: 0f, Pt1: 0f, Pt2: 0f }) continue;
            ApplyPair(c, c.Normal * c.Pn + c.Tangent1 * c.Pt1 + c.Tangent2 * c.Pt2);
        }
    }

    // per-pair impulse lists are pooled: allocating a fresh List for every
    // touching pair on every substep was a steady GC stream
    private readonly Stack<List<CachedImpulse>> _cacheListPool = new();

    private void StoreWarmCache()
    {
        (_warmCache, _warmCacheBack) = (_warmCacheBack, _warmCache);
        foreach (var recycled in _warmCache.Values)
        {
            recycled.Clear();
            _cacheListPool.Push(recycled);
        }
        _warmCache.Clear();
        PeakCachedImpulse = 0f;
        for (int ci = 0; ci < _contactCount; ci++)
        {
            Contact c = _contacts[ci];
            if (c is { Pn: 0f, Pt1: 0f, Pt2: 0f }) continue;
            if (c.Pn > PeakCachedImpulse) PeakCachedImpulse = c.Pn;
            var key = (c.A, c.B);
            if (!_warmCache.TryGetValue(key, out var list))
                _warmCache[key] = list = _cacheListPool.Count > 0 ? _cacheListPool.Pop() : new List<CachedImpulse>(4);
            list.Add(new CachedImpulse
            {
                LocalA = Vector3.Transform(c.Point - c.A.Position, Quaternion.Conjugate(c.A.Rotation)),
                Pn = c.Pn,
                Pt1 = c.Pt1,
                Pt2 = c.Pt2,
            });
        }
    }

    private static float EffectiveMass(Contact c, Vector3 dir)
    {
        float k = 1e-6f;
        if (!c.A.Inactive && !c.SkipA)
        {
            var raCrossD = Vector3.Cross(c.RA, dir);
            k += c.A.InvMass + Vector3.Dot(Vector3.Cross(c.A.InvInertiaWorld * raCrossD, c.RA), dir);
        }
        if (!c.B.Inactive && !c.SkipB)
        {
            var rbCrossD = Vector3.Cross(c.RB, dir);
            k += c.B.InvMass + Vector3.Dot(Vector3.Cross(c.B.InvInertiaWorld * rbCrossD, c.RB), dir);
        }
        return k;
    }

    private static Vector3 RelativeVelocity(Contact c)
        => (c.B.Velocity + Vector3.Cross(c.B.AngularVelocity, c.RB))
         - (c.A.Velocity + Vector3.Cross(c.A.AngularVelocity, c.RA));

    private static void SolveContact(Contact c)
    {
        var n = c.Normal;

        // normal impulse with accumulated clamping: individual iterations may go
        // negative, only the running total has to stay >= 0
        float vn = Vector3.Dot(RelativeVelocity(c), n);
        float dPn = c.MassNormal * (c.VelocityBias - vn);
        float oldPn = c.Pn;
        c.Pn = MathF.Max(oldPn + dPn, 0f);
        ApplyPair(c, n * (c.Pn - oldPn));

        // friction: Coulomb cone approximated by a box on two tangents
        float friction = MathF.Sqrt(c.A.Friction * c.B.Friction);
        float maxPt = friction * c.Pn;

        float vt1 = Vector3.Dot(RelativeVelocity(c), c.Tangent1);
        float oldPt1 = c.Pt1;
        c.Pt1 = Math.Clamp(oldPt1 + c.MassTangent1 * -vt1, -maxPt, maxPt);
        ApplyPair(c, c.Tangent1 * (c.Pt1 - oldPt1));

        float vt2 = Vector3.Dot(RelativeVelocity(c), c.Tangent2);
        float oldPt2 = c.Pt2;
        c.Pt2 = Math.Clamp(oldPt2 + c.MassTangent2 * -vt2, -maxPt, maxPt);
        ApplyPair(c, c.Tangent2 * (c.Pt2 - oldPt2));
    }

    private static void SolvePosition(Contact c)
    {
        if (c.PositionBias <= 0f) return;
        var n = c.Normal;

        var relBias = (c.B.BiasVelocity + Vector3.Cross(c.B.BiasAngularVelocity, c.RB))
                    - (c.A.BiasVelocity + Vector3.Cross(c.A.BiasAngularVelocity, c.RA));
        float vbn = Vector3.Dot(relBias, n);
        float dP = c.MassNormal * (c.PositionBias - vbn);
        float old = c.Pnb;
        c.Pnb = MathF.Max(old + dP, 0f);
        dP = c.Pnb - old;

        var impulse = n * dP;
        if (!c.A.Inactive && !c.SkipA)
        {
            c.A.BiasVelocity -= impulse * c.A.InvMass;
            c.A.BiasAngularVelocity -= c.A.InvInertiaWorld * Vector3.Cross(c.RA, impulse);
        }
        if (!c.B.Inactive && !c.SkipB)
        {
            c.B.BiasVelocity += impulse * c.B.InvMass;
            c.B.BiasAngularVelocity += c.B.InvInertiaWorld * Vector3.Cross(c.RB, impulse);
        }
    }

    private static void ApplyPair(Contact c, Vector3 impulse)
    {
        // sleeping bodies are treated as static here; if the hit was hard enough
        // to matter, AddContact has already woken them up
        if (!c.A.Inactive && !c.SkipA)
        {
            c.A.Velocity -= impulse * c.A.InvMass;
            c.A.AngularVelocity -= c.A.InvInertiaWorld * Vector3.Cross(c.RA, impulse);
        }
        if (!c.B.Inactive && !c.SkipB)
        {
            c.B.Velocity += impulse * c.B.InvMass;
            c.B.AngularVelocity += c.B.InvInertiaWorld * Vector3.Cross(c.RB, impulse);
        }
    }

    // ================= picking =================

    /// <summary>
    /// Radial impulse from a point - the sandbox "explosion". Strength falls off with
    /// distance (linear), and a small upward bias makes the debris hop instead of just
    /// sliding along the floor. Everything in range gets woken first, otherwise sleeping
    /// bodies would just ignore the blast.
    /// </summary>
    public void ApplyExplosion(Vector3 center, float radius, float strength)
    {
        // Radius == 0 with a body at the centre produced
        // falloff = 1 - 0/0 = NaN which then infected the body's velocity
        if (!(radius > 0f) || !float.IsFinite(radius) || !float.IsFinite(strength)
            || !float.IsFinite(center.X) || !float.IsFinite(center.Y) || !float.IsFinite(center.Z))
            return;

        foreach (var b in Bodies)
        {
            if (b.IsStatic) continue;
            var d = b.Position - center;
            float dist = d.Length();
            if (dist > radius) continue;

            b.Wake();
            var dir = dist > 1e-4f ? d / dist : Vector3.UnitY;
            dir = Vector3.Normalize(dir + new Vector3(0, 0.6f, 0)); // lift bias
            float falloff = 1f - dist / radius;
            b.ApplyImpulse(dir * (strength * falloff * b.Mass), b.Position);
        }
    }

    /// <summary>Closest dynamic body hit by the ray. The direction is
    /// normalized internally, so tHit is always a distance in world units
    /// (RaySphere assumed a unit direction while box/capsule did
    /// not, making the closest-hit sort shape-dependent for scaled rays).</summary>
    public RigidBody? RayCast(Vector3 origin, Vector3 dir, out float tHit, out Vector3 hitPoint)
    {
        tHit = float.MaxValue;
        hitPoint = default;

        float lenSq = dir.LengthSquared();
        if (!float.IsFinite(lenSq) || lenSq < 1e-12f) return null;
        dir /= MathF.Sqrt(lenSq);

        RigidBody? best = null;

        foreach (var body in Bodies)
        {
            if (body.IsStatic) continue;
            foreach (ref var p in body.Proxies.AsSpan())
            {
                float t = p.Shape switch
                {
                    ShapeType.Sphere => RaySphere(origin, dir, p.Position, p.Radius),
                    ShapeType.Box => RayObb(origin, dir, in p),
                    ShapeType.Capsule => RayCapsule(origin, dir, in p),
                    _ => -1f,
                };
                if (t >= 0f && t < tHit)
                {
                    tHit = t;
                    best = body;
                }
            }
        }

        if (best is not null) hitPoint = origin + dir * tHit;
        return best;
    }

    private static float RaySphere(Vector3 o, Vector3 d, Vector3 center, float r)
    {
        Vector3 m = o - center;
        float c = Vector3.Dot(m, m) - r * r;
        // origin inside (or on) the sphere is an immediate hit,
        // matching the box slab test - previously the near root was negative,
        // the far root was ignored and the sphere reported "no hit", so the
        // three shapes disagreed on the same conceptual case
        if (c <= 0f) return 0f;
        float b = Vector3.Dot(m, d);
        float disc = b * b - c;
        if (disc < 0f) return -1f;
        float t = -b - MathF.Sqrt(disc);
        return t >= 0f ? t : -1f;
    }

    private static float RayObb(Vector3 o, Vector3 d, in ShapeProxy box)
    {
        // classic slab test, done in the box's local frame
        Quaternion invRot = Quaternion.Conjugate(box.Rotation);
        Vector3 lo = Vector3.Transform(o - box.Position, invRot);
        Vector3 ld = Vector3.Transform(d, invRot);
        Vector3 h = box.HalfExtents;

        float tMin = 0f, tMax = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            float oi = Comp(lo, i), di = Comp(ld, i), hi = Comp(h, i);
            if (MathF.Abs(di) < 1e-8f)
            {
                if (oi < -hi || oi > hi) return -1f;
                continue;
            }
            float inv = 1f / di;
            float t1 = (-hi - oi) * inv;
            float t2 = (hi - oi) * inv;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tMin = MathF.Max(tMin, t1);
            tMax = MathF.Min(tMax, t2);
            if (tMin > tMax) return -1f;
        }
        return tMin;
    }

    private static float RayCapsule(Vector3 o, Vector3 d, in ShapeProxy c)
    {
        var invRot = Quaternion.Conjugate(c.Rotation);
        var lo = Vector3.Transform(o - c.Position, invRot);
        var ld = Vector3.Transform(d, invRot);
        float r = c.Radius, h = c.HalfHeight;

        // unified inside-origin contract (t = 0, like the box
        // and the sphere) - previously an inside origin returned the EXIT
        // surface distance here
        float rr = r * r;
        bool insideCylinder = lo.X * lo.X + lo.Z * lo.Z <= rr && MathF.Abs(lo.Y) <= h;
        bool insideCapTop = (lo - new Vector3(0f, h, 0f)).LengthSquared() <= rr;
        bool insideCapBottom = (lo - new Vector3(0f, -h, 0f)).LengthSquared() <= rr;
        if (insideCylinder || insideCapTop || insideCapBottom) return 0f;

        float best = -1f;
        void Consider(float t) { if (t >= 0f && (best < 0f || t < best)) best = t; }

        // side: infinite cylinder x^2 + z^2 = r^2, accept hits within the segment span
        float a = ld.X * ld.X + ld.Z * ld.Z;
        if (a > 1e-9f)
        {
            float b = 2f * (lo.X * ld.X + lo.Z * ld.Z);
            float cc = lo.X * lo.X + lo.Z * lo.Z - r * r;
            float disc = b * b - 4f * a * cc;
            if (disc >= 0f)
            {
                float sq = MathF.Sqrt(disc);
                foreach (float t in stackalloc[] { (-b - sq) / (2f * a), (-b + sq) / (2f * a) })
                    if (MathF.Abs(lo.Y + ld.Y * t) <= h)
                        Consider(t);
            }
        }

        // the two sphere caps
        foreach (float cy in stackalloc[] { -h, h })
        {
            var co = lo - new Vector3(0, cy, 0);
            float b = 2f * Vector3.Dot(co, ld);
            float cc = co.LengthSquared() - r * r;
            float dlen = ld.LengthSquared();
            float disc = b * b - 4f * cc * dlen;
            if (disc < 0f) continue;
            float sq = MathF.Sqrt(disc);
            Consider((-b - sq) / (2f * dlen));
            Consider((-b + sq) / (2f * dlen));
        }

        return best;
    }
}
