namespace MakarovPhysics.Renderer;

/// <summary>
/// A polyline parameterized by arc length: give it a distance, it gives back
/// the point there and the direction of travel. This is what lets something
/// move ALONG a path at a physical speed instead of interpolating corner to
/// corner, where a long leg and a short one would take the same time and the
/// motion would visibly hurry and stall at every bend.
///
/// Refillable in place: modules rebuild it per net or per wire every frame, so
/// it must not allocate once it has warmed up.
/// </summary>
public sealed class FlowPath
{
    private const float MinimumStep = 1e-6f;
    private readonly List<Vec3> _points = [];
    private readonly List<float> _distances = []; // arc length up to each point
    public float TotalLength { get; private set; }
    public int PointCount => _points.Count;

    public void Clear()
    {
        _points.Clear();
        _distances.Clear();
        TotalLength = 0f;
    }

    /// <summary>
    /// Appends a point. Duplicates and non-finite values are dropped: a
    /// zero-length leg carries no direction, and one NaN corner would poison
    /// every distance after it.
    /// </summary>
    public void Add(Vec3 point)
    {
        if (!point.IsFinite) return;

        if (_points.Count == 0)
        {
            _points.Add(point);
            _distances.Add(0f);
            return;
        }

        float step = Vec3.Distance(_points[^1], point);
        if (!(step > MinimumStep)) return;

        TotalLength += step;
        _points.Add(point);
        _distances.Add(TotalLength);
    }

    /// <summary>
    /// Point and unit tangent at a distance along the path. false when the
    /// path is degenerate or the distance falls outside it.
    /// </summary>
    public bool TryPointAt(float distance, out Vec3 point, out Vec3 tangent)
    {
        point = Vec3.Zero;
        tangent = Vec3.Zero;

        if (_points.Count < 2 || !(TotalLength > 0f)) return false;
        if (!float.IsFinite(distance) || distance < 0f || distance > TotalLength) return false;

        // Binary search for the leg containing the distance.
        int low = 0, high = _points.Count - 1;

        while (high - low > 1)
        {
            int middle = (low + high) / 2;

            if (_distances[middle] <= distance)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        float legLength = _distances[high] - _distances[low];
        Vec3 from = _points[low], to = _points[high];
        if (!(to - from).TryNormalize(out tangent)) return false;
        float t = legLength > MinimumStep ? (distance - _distances[low]) / legLength : 0f;
        point = from + (to - from) * t;
        return true;
    }
}
