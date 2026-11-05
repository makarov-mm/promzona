namespace MakarovPhysics.Renderer;

/// <summary>
/// A field made visible as a lattice of arrows: the layer several modules were
/// missing (Docs/ROADMAP.md, section A). A module supplies the plane, the
/// extent and a function that returns the field vector at a world point; the
/// whole lattice draws in one call through a streaming line mesh.
///
/// Geometry lives in <see cref="FieldArrowGeometry"/>, which knows nothing
/// about GL and is checked against analytic fields in SelfTest. This class is
/// only the buffer.
/// </summary>
public sealed class FieldArrowGrid : IDisposable
{
    private readonly FieldArrowGeometry _geometry;
    private ColorLineMesh? _mesh;

    public FieldArrowStyle Style = FieldArrowStyle.Default;

    /// <summary>Allocates the lattice and its GL buffer (context must be current).</summary>
    public FieldArrowGrid(int columns, int rows)
    {
        _geometry = new FieldArrowGeometry(columns, rows);

        try
        {
            _mesh = new ColorLineMesh(columns * rows * FieldArrowGeometry.VerticesPerArrow)
            {
                Mode = GL.LINES,
            };
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    /// <summary>Arrows in the current picture.</summary>
    public int ArrowCount => _geometry.ArrowCount;

    /// <summary>Magnitude the current picture is scaled by.</summary>
    public float Reference => _geometry.Reference;

    /// <summary>Resamples the field and uploads the new arrows.</summary>
    public void Update(in ScenePlane plane, float halfWidth, float halfHeight, Func<Vec3, Vec3> sample)
    {
        _geometry.Build(plane, halfWidth, halfHeight, sample, Style);
        _mesh?.Update(_geometry.Vertices);
    }

    public void Draw(Mat4 view, Mat4 projection) => _mesh?.Draw(view, projection);

    public void Dispose()
    {
        _mesh?.Dispose();
        _mesh = null;
    }
}
