namespace MakarovPhysics.Renderer;

/// <summary>
/// Appearance of a field arrow lattice. A struct with sane defaults, so a
/// module that has no opinion passes <see cref="Default"/> and a module that
/// does (a magnetic field wants its own palette) changes one field.
/// </summary>
public struct FieldArrowStyle
{
    /// <summary>Longest arrow, as a fraction of the lattice spacing. Above 1 the arrows collide.</summary>
    public float MaxLengthFraction;

    /// <summary>Shortest arrow, as a fraction of the longest one.</summary>
    public float MinimumLengthFraction;

    /// <summary>
    /// Magnitude that maps to the middle of the scale. Zero means automatic:
    /// the median of the current sample, which keeps the picture readable
    /// while the field itself changes by orders of magnitude.
    /// </summary>
    public float Reference;

    /// <summary>Color of the weakest arrows.</summary>
    public Vec3 WeakColor;

    /// <summary>Color of the strongest arrows.</summary>
    public Vec3 StrongColor;

    public static FieldArrowStyle Default => new()
    {
        MaxLengthFraction = 0.82f,
        MinimumLengthFraction = FieldArrowScale.DefaultMinimumFraction,
        Reference = 0f,
        WeakColor = new Vec3(0.26f, 0.32f, 0.45f),
        StrongColor = new Vec3(0.96f, 0.86f, 0.42f),
    };
}
