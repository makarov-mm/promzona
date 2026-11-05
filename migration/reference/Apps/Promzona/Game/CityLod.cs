namespace Promzona.Game;

public static class CityLod
{
    public const int MinimumDistance = 130, DefaultDistance = 1000, MaximumDistance = 3000;
    // Upgrade only the old untouched default. Explicit non-default preferences survive.
    public static int RestoreDistance(int? saved,int revision) => ClampDistance(saved is null || ((revision==0 && saved==205) || (revision<2 && saved==600))?DefaultDistance:saved.Value);
    public static float ActorRange(int distance) => Math.Min(ActorVisible,ClampDistance(distance)-20);
    public static float VehicleRange(int distance) => Math.Min(VehicleVisible,ClampDistance(distance)-20);
    public static int SpawnRange(int distance) => (int)Math.Max(ActorRange(distance),VehicleRange(distance))+80;
    public static int ClampDistance(int value) => Math.Clamp(value, MinimumDistance, MaximumDistance);
    public static float FarPlane(int distance, float cameraDistance) => Math.Max(650, ClampDistance(distance) + cameraDistance + 100);
    // At overview distances a 12 cm near plane wastes almost all depth precision.
    // Retracting the chase camera also retracts this plane, preserving close views.
    public static float NearPlane(float cameraDistance) => Math.Clamp(cameraDistance * .01f, .12f, 8);
    public const float DetailScale = 1.5f;
    public static bool InHorizontalRange(double x, double z, double eyeX, double eyeZ, double radius)
    {
        double dx=x-eyeX, dz=z-eyeZ;
        return dx*dx+dz*dz < radius*radius;
    }

    public const float Vehicle = 85 * DetailScale, Actor = 125 * DetailScale;

    // Far actors use simple silhouettes; high-detail and local physics radii do not grow.
    // SpawnRange keeps replenishment outside actor visibility. Scenery can now span the entire island.
    public const float ActorVisible = 320, VehicleVisible = 420;
}
