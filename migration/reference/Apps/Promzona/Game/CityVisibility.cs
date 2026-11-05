using MakarovPhysics.Renderer;
namespace Promzona.Game;

internal sealed class CityVisibility
{
    private readonly (float X, float Y, float Z, float D)[] _planes = new (float, float, float, float)[6];

    public CityVisibility(Mat4 vp)
    {
        int n = 0;

        for (int axis = 0; axis < 3; axis++)
        {
            for (int sign = -1; sign <= 1; sign += 2)
            {
                float x = vp[3, 0] + sign * vp[axis, 0],
                    y = vp[3, 1] + sign * vp[axis, 1],
                    z = vp[3, 2] + sign * vp[axis, 2],
                    d = vp[3, 3] + sign * vp[axis, 3];

                float len = MathF.Sqrt(x * x + y * y + z * z);
                _planes[n++] = (x / len, y / len, z / len, d / len);
            }
        }
    }

    public bool Box(Vec3 centre, Vec3 half)
    {
        foreach (var p in _planes)
            if (p.X*centre.X + p.Y*centre.Y + p.Z*centre.Z + p.D <
                -(Math.Abs(p.X)*half.X + Math.Abs(p.Y)*half.Y + Math.Abs(p.Z)*half.Z)) return false;
        return true;
    }

    public bool BeyondNearPlane(Vec3 centre, Vec3 half)
    {
        var p = _planes[5]; // row 3 + row 2
        return p.X*centre.X + p.Y*centre.Y + p.Z*centre.Z + p.D >
               Math.Abs(p.X)*half.X + Math.Abs(p.Y)*half.Y + Math.Abs(p.Z)*half.Z;
    }

    public bool Sphere(Vec3 p, float radius)
    {
        // Called for every car, person, prop and tile each frame: a plain loop,
        // not a LINQ closure allocated per call.
        foreach ((float X, float Y, float Z, float D) q in _planes)
        {
            if (q.X * p.X + q.Y * p.Y + q.Z * p.Z + q.D < -radius) return false;
        }

        return true;
    }
}
