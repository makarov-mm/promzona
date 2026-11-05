namespace MakarovPhysics.Numerics;

public sealed class GeometryFrame
{
    public List<GeometryStroke> Lines { get; } = [];
    public List<GeometryDisc> Circles { get; } = [];
    public List<GeometryMark> Marks { get; } = [];
    public List<GeometryFace> Faces { get; } = [];
    public List<GeometryReading> Readings { get; } = [];
    public double Error { get; set; }
    public bool Goal { get; set; }
    public string Formula { get; set; } = string.Empty;
    public void Line(GeoPoint a, GeoPoint b, int ink = 0, bool dashed = false) => Lines.Add(new GeometryStroke(a, b, ink, dashed));

    public void Path(IReadOnlyList<GeoPoint> points, int ink = 0, bool closed = false, bool fill = false)
    {
        for (int i = 1; i < points.Count; i++)
        {
            Line(points[i - 1], points[i], ink);
        }

        if (closed && points.Count > 2)
        {
            Line(points[^1], points[0], ink);
        }

        if (fill && points.Count > 2)
        {
            Faces.Add(new GeometryFace([.. points], ink));
        }
    }

    public void Mark(GeoPoint p, string label, int ink = 1, int handle = -1) => Marks.Add(new GeometryMark(p, label, ink, handle));
    public void Read(string label, double value) => Readings.Add(new GeometryReading(label, value));
}
