using MakarovPhysics.Renderer;

namespace Promzona.Game;

/// <summary>A fully opaque facade occludes a box only if the whole box is inside
/// its perspective shadow. Buildings near the camera's clipping plane are excluded.</summary>
internal sealed class CityDistrictOcclusion
{
    private readonly record struct Face(bool AlongX, float Sign, float Depth,
        float Left, float Right, float Bottom, float Top)
    {
        public bool Hides(Vec3 delta, Vec3 half)
        {
            float d = Sign * (AlongX ? delta.X : delta.Z);
            float width = AlongX ? half.Z : half.X, depth = AlongX ? half.X : half.Z;
            if (d - depth <= Depth) return false;
            float x = AlongX ? delta.Z : delta.X;
            return x - Left*d > width + Math.Abs(Left)*depth &&
                   Right*d - x > width + Math.Abs(Right)*depth &&
                   delta.Y - Bottom*d > half.Y + Math.Abs(Bottom)*depth &&
                   Top*d - delta.Y > half.Y + Math.Abs(Top)*depth;
        }
    }

    private readonly List<Face> _faces = [];
    private Vec3 _eye;
    public void Reset(Vec3 eye) { _eye = eye; _faces.Clear(); }

    // Only boxes known to be present in the resident drawing mesh are supplied.
    public void Add(Vec3 centre, Vec3 half, CityVisibility frustum)
    {
        if (!frustum.Box(centre, half) || !frustum.BeyondNearPlane(centre, half)) return;
        var d = centre - _eye;
        if (Math.Abs(d.X) > half.X + 1) AddFace(true, d.X, half.X, d.Z, half.Z, d.Y, half.Y);
        if (Math.Abs(d.Z) > half.Z + 1) AddFace(false, d.Z, half.Z, d.X, half.X, d.Y, half.Y);
    }

    private void AddFace(bool alongX, float along, float extent, float lateral,
                         float width, float height, float halfHeight)
    {
        float depth = Math.Abs(along) - extent;
        _faces.Add(new(alongX, Math.Sign(along), depth, (lateral-width)/depth,
            (lateral+width)/depth, (height-halfHeight)/depth, (height+halfHeight)/depth));
    }

    public bool Hidden(Vec3 centre, Vec3 half)
    {
        var delta = centre - _eye;
        foreach (var face in _faces)
            if (face.Hides(delta, half)) return true;
        return false;
    }
}
