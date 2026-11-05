using MakarovPhysics.RigidBodies2D;

namespace Promzona.Game;

public sealed class StreetProp
{
    public int Id;
    public StreetKind Kind;
    public CityPoint Home;
    public Body2D? Body;
    public double Height, Vy, Tilt, Spin, DisplacedFor, Water;

    public double Mass => Kind switch
    {
        StreetKind.PhoneBooth => 180, 
        StreetKind.DrinkMachine => 140, 
        StreetKind.Mailbox => 28,
        StreetKind.Dumpster => 110, 
        StreetKind.Bin => 22, 
        StreetKind.Hydrant => 65, 
        StreetKind.Bench => 80,
        StreetKind.GarbageBag => 5, 
        StreetKind.Bag => .08, 
        StreetKind.Bottle => .4, 
        _ => .06
    };

    public double Radius => Kind switch
    {
        StreetKind.PhoneBooth => .70, 
        StreetKind.DrinkMachine => .55, 
        StreetKind.Mailbox => .35,
        StreetKind.Dumpster => .85, 
        StreetKind.Bench => 1, 
        StreetKind.Bin => .3, 
        StreetKind.Hydrant => .25,
        StreetKind.GarbageBag => .27, 
        StreetKind.Bag => .16, 
        _ => .10
    };

    public CityPoint Position => Body is { } b ? new CityPoint(b.X, b.Y) : Home;
    public bool Moved => (Position - Home).Length > .6 || Height > .1 || Math.Abs(Tilt) > .2;
}
