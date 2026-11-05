using MakarovPhysics.RigidBodies2D;
namespace Promzona.Game;

/// <summary>A persistent vehicle, independent of its rider. Only loose nearby
/// scooters join the impulse world; occupied scooters use the rider's controller.</summary>
public sealed class CityScooter
{
    public int Id, OwnerId, RiderId, RoadLevel;
    public CityPoint Position;
    public double Heading, Fallen, VerticalSpeed, Height, ReleasedAt, ReturnTime;
    public Body2D Body=null!;
    public bool Physical;
    public const double Mass=16, HalfWidth=.22, HalfLength=.64;
}
