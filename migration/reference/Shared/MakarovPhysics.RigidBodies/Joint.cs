using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>
/// A two-body constraint solved with sequential impulses, right next to the contacts.
/// Point keeps two anchor points coincident (a ball-socket - good for chains and ragdolls);
/// Distance holds them a fixed length apart (a rigid rod); Rope only resists stretching.
/// If body B is null the joint is pinned to a fixed point in the world.
/// </summary>
public sealed class Joint
{
    public enum Kind { Point, Distance, Rope, Spring }

    public Kind Type;
    public RigidBody A = null!;
    public RigidBody? B; // null => anchored to the world at AnchorB (world space)
    public Vector3 LocalA; // attach point in A's local frame
    public Vector3 LocalB; // in B's local frame, or world point if B is null
    public float Length; // for Distance / Rope / Spring
    public float Stiffness = 18f; // for Spring
    public float Damping = 2.2f; // for Spring

    // recomputed each substep
    private Vector3 _rA, _rB, _worldA, _worldB;
    private Matrix3x3 _kInv; // effective-mass matrix for the Point constraint
    private Vector3 _axis; // unit A->B direction for Distance / Rope
    private float _axisMass;

    /// <summary>False when the constraint is currently unsolvable (both
    /// bodies static, singular effective mass, missing body). Solve() skips
    /// inactive joints instead of throwing out of the physics step
    /// (a static-static point joint used to take the whole
    /// module down with InvalidOperationException from Matrix3x3.Inverse).</summary>
    public bool Active { get; private set; }

    public Vector3 WorldA => _worldA;
    public Vector3 WorldB => _worldB;
    private Vector3 VelA => A.Velocity + Vector3.Cross(A.AngularVelocity, _rA);
    private Vector3 VelB => B is null ? Vector3.Zero : B.Velocity + Vector3.Cross(B.AngularVelocity, _rB);
    public bool Involves(RigidBody body) => A == body || B == body;

    public void WakePair()
    {
        if (!A.IsStatic) A.Wake();
        if (B is { IsStatic: false }) B.Wake();
    }

    public void Presolve(float h)
    {
        // defensive even though PhysicsWorld.AddJoint validates: the public
        // Joints list is still writable and fields are mutable
        if (A is null || (A.IsStatic && (B is null || B.IsStatic)))
        {
            Active = false;
            return;
        }

        _rA = Vector3.Transform(LocalA, A.Rotation);
        _worldA = A.Position + _rA;

        if (B is not null)
        {
            _rB = Vector3.Transform(LocalB, B.Rotation);
            _worldB = B.Position + _rB;
        }
        else
        {
            _rB = Vector3.Zero;
            _worldB = LocalB; // world anchor
        }

        if (Type == Kind.Point)
        {
            // K = invMassSum*I - skew(rA) IinvA skew(rA) - skew(rB) IinvB skew(rB)
            Matrix3x3 k = Matrix3x3.Diagonal(new Vector3(InvMassSum()));
            k += SkewInertiaTerm(A, _rA);
            if (B is { IsStatic: false }) k += SkewInertiaTerm(B, _rB);
            // singular K (degenerate inertia, static pair) deactivates the
            // joint for this step instead of throwing
            Active = k.TryInvert(out _kInv);
        }
        else
        {
            Vector3 d = _worldB - _worldA;
            float len = d.Length();
            _axis = len > 1e-5f ? d / len : Vector3.UnitY;
            float effMass = EffMassAlong(_axis);
            Active = float.IsFinite(effMass) && effMass > 1e-9f;
            if (Active) _axisMass = 1f / effMass;
        }
    }

    public void Solve(float h)
    {
        if (!Active) return;   // skip unsolvable constraints

        const float beta = 0.2f;

        if (Type == Kind.Point)
        {
            var c = _worldB - _worldA;
            Vector3 vrel = VelB - VelA;
            Vector3 rhs = -(vrel + c * (beta / h));
            Vector3 impulse = _kInv * rhs;
            ApplyImpulse(impulse);
        }
        else
        {
            float c = (_worldB - _worldA).Length() - Length;
            float vrel = Vector3.Dot(VelB - VelA, _axis);

            if (Type == Kind.Spring)
            {
                // Soft distance link. Unlike Distance/Rope, it deliberately overshoots and oscillates.
                // _axisMass keeps heavy/light pairs from exploding too easily.
                float jn = -(Stiffness * c + Damping * vrel) * h * _axisMass;
                jn = Math.Clamp(jn, -2.5f, 2.5f);
                ApplyImpulse(_axis * jn);
                return;
            }

            if (Type == Kind.Rope && c < 0f) return; // slack rope does nothing

            float jnRigid = -(vrel + beta / h * c) * _axisMass;
            ApplyImpulse(_axis * jnRigid);
        }
    }

    private void ApplyImpulse(Vector3 p)
    {
        if (!A.IsStatic)
        {
            A.Velocity -= p * A.InvMass;
            A.AngularVelocity -= A.InvInertiaWorld * Vector3.Cross(_rA, p);
        }
        if (B is { IsStatic: false })
        {
            B.Velocity += p * B.InvMass;
            B.AngularVelocity += B.InvInertiaWorld * Vector3.Cross(_rB, p);
        }
    }

    private float InvMassSum() => (A.IsStatic ? 0f : A.InvMass) + (B is { IsStatic: false } ? B.InvMass : 0f);

    private float EffMassAlong(Vector3 n)
    {
        float k = 1e-6f;

        if (!A.IsStatic)
        {
            var rn = Vector3.Cross(_rA, n);
            k += A.InvMass + Vector3.Dot(Vector3.Cross(A.InvInertiaWorld * rn, _rA), n);
        }

        if (B is { IsStatic: false })
        {
            var rn = Vector3.Cross(_rB, n);
            k += B.InvMass + Vector3.Dot(Vector3.Cross(B.InvInertiaWorld * rn, _rB), n);
        }

        return k;
    }

    private static Matrix3x3 SkewInertiaTerm(RigidBody b, Vector3 r)
    {
        // -skew(r) * Iinv * skew(r), built column by column
        var c0 = SkewMul(r, b.InvInertiaWorld * SkewMul(r, Vector3.UnitX));
        var c1 = SkewMul(r, b.InvInertiaWorld * SkewMul(r, Vector3.UnitY));
        var c2 = SkewMul(r, b.InvInertiaWorld * SkewMul(r, Vector3.UnitZ));

        return new Matrix3x3(
            -c0.X, -c1.X, -c2.X,
            -c0.Y, -c1.Y, -c2.Y,
            -c0.Z, -c1.Z, -c2.Z);
    }

    // skew(r) * v == r x v
    private static Vector3 SkewMul(Vector3 r, Vector3 v) => Vector3.Cross(r, v);
}
