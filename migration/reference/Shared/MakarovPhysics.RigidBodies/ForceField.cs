using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>
/// A localized force region. Attract/Repel pull or push toward a point; Wind blows in a
/// fixed direction. All three are applied as accelerations (independent of mass, like
/// gravity) with a linear falloff to the edge of the radius.
/// </summary>
public sealed class ForceField
{
    public enum Kind { Attract, Repel, Wind }

    public Kind Type;
    public Vector3 Position;
    public float Radius = 6f;
    public float Strength = 20f;
    public Vector3 WindDir = Vector3.UnitX;

    /// <summary>Apply the field to a body. Returns true only when the body was
    /// actually inside the field and a force was applied - the caller uses this
    /// To wake sleeping bodies without waking the whole scene.
    /// Degenerate configurations (non-positive or non-finite radius, zero wind
    /// direction, non-finite strength) apply nothing instead of producing NaN
    /// Velocities.</summary>
    public bool Apply(RigidBody b, float h)
    {
        if (!(Radius > 0f) || !float.IsFinite(Radius) || !float.IsFinite(Strength))
            return false;

        Vector3 d = Position - b.Position;
        float dist = d.Length();
        if (!float.IsFinite(dist) || dist > Radius) return false;
        float falloff = 1f - dist / Radius;

        Vector3 accel;
        switch (Type)
        {
            case Kind.Attract:
                accel = (dist > 1e-3f ? d / dist : Vector3.Zero) * Strength;
                break;

            case Kind.Repel:
                accel = (dist > 1e-3f ? -d / dist : Vector3.UnitY) * Strength;
                break;

            default:
                float windLenSq = WindDir.LengthSquared();
                if (!float.IsFinite(windLenSq) || windLenSq < 1e-12f) return false;
                accel = WindDir / MathF.Sqrt(windLenSq) * Strength;
                break;
        }

        b.Velocity += accel * (falloff * h);
        return true;
    }
}
