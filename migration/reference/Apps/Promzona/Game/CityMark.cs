namespace Promzona.Game;

public sealed class CityMark
{
    public int RoadLevel;
    public CityPoint Point, End;
    public double Age, Life = 24, Height;
    public int CarId = -1;
    public double Heading;
    public bool Skid, Blood, Wet, Sand;
    public double Size = .65;
    public double Opacity => Math.Clamp(1 - Age / Life, 0, 1);
}
