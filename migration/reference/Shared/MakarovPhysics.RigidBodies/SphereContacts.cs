using System.Numerics;

namespace MakarovPhysics.RigidBodies;
/// <summary>Sphere-pair geometry shared by the sandbox and granular solver.</summary>
public static class SphereContacts
{
    public static bool TryContact(Vector3 a, float ra, Vector3 b, float rb, float margin, out Vector3 normal, out Vector3 point, out float penetration)
    {
        var d = b - a;
        float distance = d.Length(), sum = ra + rb;
        normal = distance > 1e-6f ? d / distance : Vector3.UnitY;
        penetration = sum - distance;
        point = a + normal * (ra - penetration * .5f);
        return distance < sum + margin;
    }
}
