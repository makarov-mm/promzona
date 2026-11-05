using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;
/// <summary>A mathematical diagram remains square to the screen. Only presentation
/// helpers live here; algorithms and mathematical state belong to each module.</summary>
public abstract class DiagramModule : ExhibitModule
{
    protected override bool Table => false;
    protected override Vec3 Lower => new(-3.25f, -.35f, -.1f);
    protected override Vec3 Upper => new(3.25f, 3.35f, .1f);

    protected DiagramModule()
    {
        Camera.SetPlanar(true);
        Camera.Target = new(0, 1.5f, 0);
    }

    protected void Axes(DioramaGeometry g, Vec3 origin, float width = 2.8f, float height = 1.5f)
    {
        ExhibitGeometry.Arrow(g, origin - new Vec3(width, 0, 0), origin + new Vec3(width, 0, 0), Ivory, .009f);
        ExhibitGeometry.Arrow(g, origin - new Vec3(0, height, 0), origin + new Vec3(0, height, 0), Ivory, .009f);
    }

    protected void Dot(DioramaGeometry g, double x, double y, Vec3 color, float size = .055f) => g.Ellipsoid(ExhibitGeometry.V(x, y, .035), new(size, size, .025f), color);
    protected void Cell(DioramaGeometry g, double x, double y, double width, double height, Vec3 color, string text = "")
    {
        var material = g.Material;
        g.Material = SurfaceMaterial.Flat;
        g.Box(ExhibitGeometry.V(x, y), ExhibitGeometry.V(width * .48, height * .48, .012), color);
        g.Material = material;
        if (text.Length > 0)
            Pin(ExhibitGeometry.V(x, y - .08, .04), text, Ivory);
    }

    protected bool OnPlane(Vec3 origin, Vec3 direction, out double x, out double y)
    {
        x = y = 0;
        if (Math.Abs(direction.Z) < 1e-7)
            return false;
        double t = -origin.Z / direction.Z;
        if (t < 0)
            return false;
        x = origin.X + t * direction.X;
        y = origin.Y + t * direction.Y;
        return true;
    }
}
