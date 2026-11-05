using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>Rigid parts for mechanism experiments, carrying the inertia of the real shape (a disc,
/// a slender link, a block) rather than that of the box that stands in as their collision proxy.
/// All parts share one positive collision group, so an assembly never collides with itself.</summary>
public static class MechanismParts
{
    public const int AssemblyGroup = 1100;

    /// <summary>Rotation that carries local +Z onto the given direction (shortest arc).</summary>
    public static Quaternion AlignZ(Vector3 axis)
    {
        axis = Vector3.Normalize(axis);
        float d = Vector3.Dot(Vector3.UnitZ, axis);
        if (d > .999999f) return Quaternion.Identity;
        if (d < -.999999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI);
        var c = Vector3.Cross(Vector3.UnitZ, axis);
        return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1 + d));
    }

    /// <summary>Rotation that carries local +X onto the given direction (shortest arc).</summary>
    public static Quaternion AlignX(Vector3 direction)
    {
        direction = Vector3.Normalize(direction);
        float d = Vector3.Dot(Vector3.UnitX, direction);
        if (d > .999999f) return Quaternion.Identity;
        if (d < -.999999f) return Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI);
        var c = Vector3.Cross(Vector3.UnitX, direction);
        return Quaternion.Normalize(new Quaternion(c.X, c.Y, c.Z, 1 + d));
    }

    /// <summary>A solid cylinder - disc, wheel, pulley or shaft - on the given axis. Local +Z is the axis.</summary>
    public static RigidBody Rotor(Vector3 centre, Vector3 axis, float radius, float length, float mass)
    {
        Positive(radius, nameof(radius));
        Positive(length, nameof(length));
        float axial = .5f * mass * radius * radius;
        float transverse = mass * (3 * radius * radius + length * length) / 12;
        var body = Make(centre, new Vector3(radius, radius, length / 2), mass, new Vector3(transverse, transverse, axial));
        body.Rotation = AlignZ(axis);
        body.UpdateDerived();
        return body;
    }

    /// <summary>A slender link from a to b with a square section of the given width. Local +X runs a→b.</summary>
    public static RigidBody Bar(Vector3 a, Vector3 b, float width, float mass)
    {
        Positive(width, nameof(width));
        float length = Vector3.Distance(a, b);
        Positive(length, nameof(length));
        float along = mass * width * width / 6;
        float across = mass * (length * length + width * width) / 12;
        var body = Make((a + b) / 2, new Vector3(length / 2, width / 2, width / 2), mass, new Vector3(along, across, across));
        body.Rotation = AlignX(b - a);
        body.UpdateDerived();
        return body;
    }

    /// <summary>A rectangular block with the given half extents, axis-aligned.</summary>
    public static RigidBody Block(Vector3 centre, Vector3 half, float mass)
    {
        Positive(half.X, nameof(half));
        Positive(half.Y, nameof(half));
        Positive(half.Z, nameof(half));
        var inertia = new Vector3(half.Y * half.Y + half.Z * half.Z, half.X * half.X + half.Z * half.Z, half.X * half.X + half.Y * half.Y) * (mass / 3);
        var body = Make(centre, half, mass, inertia);
        body.UpdateDerived();
        return body;
    }

    /// <summary>Applies a force at the centre of mass for one step, as a velocity increment.</summary>
    public static void Push(RigidBody body, Vector3 force, float dt)
    {
        if (body.IsStatic) return;
        body.Velocity += force * (body.InvMass * dt);
    }

    /// <summary>Applies a force at a point of the body for one step: the push at the centre and the
    /// twist of its lever.</summary>
    public static void PushAt(RigidBody body, Vector3 point, Vector3 force, float dt)
    {
        Push(body, force, dt);
        Twist(body, Vector3.Cross(point - body.Position, force), dt);
    }

    /// <summary>Applies a torque for one step, as an angular velocity increment.</summary>
    public static void Twist(RigidBody body, Vector3 torque, float dt)
    {
        if (body.IsStatic) return;
        body.AngularVelocity += body.InvInertiaWorld * (torque * dt);
    }

    /// <summary>Translational plus rotational kinetic energy, J. Valid for the parts built here,
    /// whose local inverse inertia is diagonal.</summary>
    public static double KineticEnergy(RigidBody body)
    {
        if (body.IsStatic) return 0;
        var w = Vector3.Transform(body.AngularVelocity, Quaternion.Conjugate(body.Rotation));
        var inverse = body.InvInertiaLocal;
        return .5 * body.Mass * body.Velocity.LengthSquared()
             + .5 * (w.X * w.X / inverse.M00 + w.Y * w.Y / inverse.M11 + w.Z * w.Z / inverse.M22);
    }

    private static RigidBody Make(Vector3 centre, Vector3 half, float mass, Vector3 inertia)
    {
        Positive(mass, nameof(mass));
        var body = RigidBody.CreateBox(centre, half, 1);
        body.Mass = mass;
        body.InvMass = 1 / mass;
        body.InvInertiaLocal = Matrix3x3.Diagonal(new Vector3(1 / inertia.X, 1 / inertia.Y, 1 / inertia.Z));
        body.CollisionGroup = AssemblyGroup;
        return body;
    }

    private static void Positive(float value, string name)
    {
        if (!(value > 0) || !float.IsFinite(value)) throw new ArgumentOutOfRangeException(name);
    }
}
