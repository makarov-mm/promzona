namespace Promzona.Game;

/// <summary>Seat coordinates in the same metre frame as the vehicle GLBs.
/// Passengers stay in the vehicle record; no extra physics actors are spawned.</summary>
public static class CityTransitCabin
{
    public const int Capacity=8;
    public const double VisibleRange=82;
    public readonly record struct Seat(double X,double Floor,double Z,double Heading);
    public static Seat At(int kind,int index)
    {
        if(kind is not (9 or 10)||index is < 0 or >= Capacity)throw new ArgumentOutOfRangeException();
        int row=index/2;double side=index%2==0?-1:1;
        // The old tram has longitudinal benches; the trolley has forward-facing rows.
        return kind==9?new(side*.83,.76,-2.25+row*1.5,-side*Math.PI/2)
            :new(side*.74,.80,-3.05+row*1.65,0);
    }
    public static Seat Driver(int kind)=>kind==9?new(-.55,.76,4.15,0):new(-.65,.80,4.65,0);
    public static Seat Aisle(int kind)=>new(0,kind==9?.76:.80,kind==9?2.9:3.5,0);
}
