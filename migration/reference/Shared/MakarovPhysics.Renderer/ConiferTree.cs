namespace MakarovPhysics.Renderer;

/// <summary>
/// The low-poly fir of the Quadcopter landscape, written down once so that
/// every scene that plants one plants the SAME tree (29.08, Michael: "take
/// the firs from Quadcopter, they are perfect there").
///
/// It was already spelled out twice - baked into the Quadcopter world mesh
/// and drawn cone by cone on the meadow - and the two had drifted: the
/// meadow's horizon belt had no trunk at all, so its skirts floated a metre
/// above the ground, and all three of them shared one flat green instead of
/// growing lighter towards the light. Both scenes now read the table below,
/// which is the only place the proportions exist.
///
/// The unit is the one the Quadcopter numbers were written in: the tip
/// stands <see cref="FirTip"/> = 0.94 above the foot. A caller that wants a
/// tree of a given height multiplies by height / FirTip - the table is not
/// normalised on purpose, so these numbers still match the landscape they
/// came from line for line.
/// </summary>
public static class ConiferTree
{
    /// <summary>One cone of the tree. <paramref name="BaseY"/> is the height
    /// of its base above the tree's foot; the cone rises <paramref name="Height"/>
    /// from there to a point.</summary>
    public readonly record struct Tier(float BaseY, float Radius, float Height, int Sides, Vec3 Color);

    private static readonly Tier[] FirTiers =
    [
        // trunk: thin, five-sided, and short enough to disappear inside the
        // lowest skirt - at its top the skirt is still 0.13 wide against the
        // trunk's 0.055, which is what keeps a fir from reading as a stick
        // wearing hats
        new(0.00f, 0.055f, 0.42f, 5, new Vec3(0.30f, 0.20f, 0.11f)),
        // three skirts, each overlapping the one below by about 40 % so the
        // silhouette has no waist, and each a shade lighter going up
        new(0.18f, 0.30f, 0.42f, 7, new Vec3(0.09f, 0.30f, 0.13f)),
        new(0.42f, 0.23f, 0.36f, 7, new Vec3(0.10f, 0.33f, 0.14f)),
        new(0.64f, 0.15f, 0.30f, 7, new Vec3(0.12f, 0.37f, 0.16f)),
    ];

    private static readonly Tier[] BroadleafTiers =
    [
        new(0.00f, 0.05f, 0.36f, 5, new Vec3(0.32f, 0.22f, 0.12f)),
        // one squat cone reads as a round crown at these distances
        new(0.26f, 0.26f, 0.34f, 8, new Vec3(0.20f, 0.40f, 0.14f)),
    ];

    /// <summary>Trunk plus three shrinking skirts, foot upwards.</summary>
    public static ReadOnlySpan<Tier> Fir => FirTiers;

    /// <summary>Trunk plus one round crown, foot upwards.</summary>
    public static ReadOnlySpan<Tier> Broadleaf => BroadleafTiers;

    /// <summary>Height of the fir's tip above its foot, in table units.</summary>
    public static float FirTip => 0.94f;

    /// <summary>Height of the broadleaf's tip above its foot, in table units.</summary>
    public static float BroadleafTip => 0.60f;

    /// <summary>
    /// Walk the triangles of one tier and hand each to <paramref name="emit"/>
    /// as three vertices IN WINDING ORDER plus the OUTWARD unit normal. The
    /// tier is placed with its tree's foot at <paramref name="foot"/> and its
    /// table units multiplied by <paramref name="scale"/>.
    ///
    /// Both the winding and the cross product live here and nowhere else,
    /// because they have to agree and it is invisible when they do not.
    ///
    /// They did not. Every cone in the suite was wound apex, b0, b1, whose
    /// right-hand normal points INTO the cone - so with `CullFace(BACK)` and
    /// `FrontFace(CCW)`, which is the meadow's state for characters, the facet
    /// facing the camera was DISCARDED and what reached the screen was the far
    /// inside wall. Same silhouette, same colour range, no near-side facets:
    /// a flat green triangle where a fir should be (29.08, Michael, twice:
    /// "in the quadcopter the firs are three-dimensional, in Lotka-Volterra
    /// they are flat"). The Quadcopter landscape never showed it because it
    /// draws its world with culling switched off entirely.
    ///
    /// So: vertices apex, b1, b0 - counter-clockwise seen from OUTSIDE - and
    /// the matching normal (b1 - apex) x (b0 - apex), which points out and up
    /// the slope.
    /// </summary>
    public static void Facets(Tier tier, Vec3 foot, float scale, Action<Vec3, Vec3, Vec3, Vec3> emit)
    {
        ArgumentNullException.ThrowIfNull(emit);
        if (tier.Sides < 3 || !(scale > 0)) return;

        Vec3 baseCentre = foot + new Vec3(0, tier.BaseY * scale, 0);
        Vec3 apex = baseCentre + new Vec3(0, tier.Height * scale, 0);
        float radius = tier.Radius * scale;
        for (int k = 0; k < tier.Sides; k++)
        {
            float a0 = k * MathF.Tau / tier.Sides, a1 = (k + 1) * MathF.Tau / tier.Sides;
            Vec3 b0 = baseCentre + new Vec3(radius * MathF.Cos(a0), 0, radius * MathF.Sin(a0));
            Vec3 b1 = baseCentre + new Vec3(radius * MathF.Cos(a1), 0, radius * MathF.Sin(a1));
            Vec3 n = Vec3.Cross(b1 - apex, b0 - apex);
            float len = MathF.Sqrt(Vec3.Dot(n, n));
            if (len < 1e-9f) continue;
            emit(apex, b1, b0, n / len);
        }
    }
}
