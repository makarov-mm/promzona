using MakarovPhysics.RigidBodies2D;

namespace Promzona.Game;

public sealed class CityAnimal
{
    public CityPoint FlockHome, FlockOffset;
    public double WanderAt;
    public bool FlockNeedsGather;
    public int Id, Variant, Flock = -1;
    public bool Physical = true;
    public AnimalKind Kind;
    public CityPoint Position, Target;
    public Body2D? Body;
    public double Heading, Height, VerticalSpeed, FlightTime, KnockedFor, DeathTime, Wait, Distance;
    public bool Flying, Dead, BloodPool, Detached;
    public double Fear, VoiceAt, FlyAt;
    public CityPoint FearFrom, Landing;
    public double Radius => Kind switch
    {
        AnimalKind.Pigeon or AnimalKind.Bird => .16,
        AnimalKind.Cat => .25,
        _ => .38
    };

    public double Mass => Kind is AnimalKind.Pigeon or AnimalKind.Bird
        ? .35
        : Kind == AnimalKind.Cat 
            ? 4 
            : Variant % 3 == 0 
                ? 13 
                : 27;
}
