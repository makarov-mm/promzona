namespace Promzona.Game;

public static class CityAim
{
    // Camera looks along +Z at heading zero; screen right is world -X.
    // The renderer and shell ray both use heading + SpecialYaw.
    public static double MouseYaw(double yaw, double dx, double sensitivity = .004) => Math.IEEERemainder(yaw - Math.Clamp(double.IsFinite(dx) ? dx : 0, -500, 500) * sensitivity, Math.Tau);
}
