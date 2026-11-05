namespace MakarovPhysics.Renderer;

/// <summary>
/// The flat surface an experiment is measured on: a point plus two orthonormal
/// in-plane axes. A module declares the plane its scene actually lives in (the
/// XY plane of a flat 2D scene, the ground plane of a 3D one) and every
/// instrument reads world units off it. That is what keeps a reading honest:
/// the number the user sees comes from the simulation's own metric, not from
/// pixels, so it survives camera moves, zoom and perspective unchanged.
/// </summary>
public readonly struct ScenePlane
{
    public readonly Vec3 Origin;
    public readonly Vec3 AxisU;
    public readonly Vec3 AxisV;
    public readonly Vec3 Normal;

    /// <summary>
    /// Builds the plane through <paramref name="origin"/>. The axes are
    /// orthonormalized, so a caller may pass any two independent directions and
    /// still get a metric frame in which one unit of U is one world unit.
    /// </summary>
    public ScenePlane(Vec3 origin, Vec3 axisU, Vec3 axisV)
    {
        if (!origin.IsFinite || !axisU.IsFinite || !axisV.IsFinite) throw new ArgumentException("ScenePlane requires a finite origin and finite axes.");
        if (!axisU.TryNormalize(out Vec3 u)) throw new ArgumentException("ScenePlane requires a non-degenerate U axis.", nameof(axisU));

        // Gram-Schmidt: drop the part of V that lies along U.
        if (!(axisV - u * Vec3.Dot(u, axisV)).TryNormalize(out Vec3 v)) throw new ArgumentException("ScenePlane requires a V axis independent of U.", nameof(axisV));

        Origin = origin;
        AxisU = u;
        AxisV = v;
        Normal = Vec3.Cross(u, v);
    }

    /// <summary>Plane of flat 2D scenes: X to the right, Y up.</summary>
    public static ScenePlane Xy => new(Vec3.Zero, Vec3.UnitX, Vec3.UnitY);

    /// <summary>Ground plane of 3D scenes: X to the right, Z toward the viewer.</summary>
    public static ScenePlane Xz => new(Vec3.Zero, Vec3.UnitX, Vec3.UnitZ);

    /// <summary>Vertical plane across the view: Z to the right, Y up.</summary>
    public static ScenePlane Zy => new(Vec3.Zero, Vec3.UnitZ, Vec3.UnitY);

    /// <summary>
    /// false for a default-constructed value. Instruments are created before a
    /// module hands them a plane, so every reading path checks this first
    /// instead of dividing by a zero-length axis.
    /// </summary>
    public bool IsValid => Normal.LengthSquared() > 0.5f;

    /// <summary>The same orientation shifted to another point (a moving stage, a water line).</summary>
    public ScenePlane MovedTo(Vec3 origin) => new(origin, AxisU, AxisV);

    public Vec3 FromLocal(float u, float v) => Origin + AxisU * u + AxisV * v;

    public (float U, float V) ToLocal(Vec3 world)
    {
        Vec3 offset = world - Origin;
        return (Vec3.Dot(offset, AxisU), Vec3.Dot(offset, AxisV));
    }

    /// <summary>Signed distance from the plane along its normal.</summary>
    public float DistanceTo(Vec3 world) => Vec3.Dot(world - Origin, Normal);

    /// <summary>
    /// Intersects a ray with the plane. false when the ray runs parallel to the
    /// plane or points away from it, in which case an instrument keeps its last
    /// good reading rather than jumping to infinity.
    /// </summary>
    public bool TryIntersect(Vec3 rayOrigin, Vec3 rayDirection, out Vec3 hit)
    {
        hit = Vec3.Zero;
        if (!IsValid || !rayOrigin.IsFinite || !rayDirection.IsFinite) return false;

        float denominator = Vec3.Dot(rayDirection, Normal);
        if (!float.IsFinite(denominator) || MathF.Abs(denominator) < 1e-6f) return false;

        float t = Vec3.Dot(Origin - rayOrigin, Normal) / denominator;
        if (!float.IsFinite(t) || t < 0f) return false;

        hit = rayOrigin + rayDirection * t;
        return hit.IsFinite;
    }
}
