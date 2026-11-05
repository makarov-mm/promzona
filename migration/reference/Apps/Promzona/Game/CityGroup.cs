namespace Promzona.Game;

public sealed class CityGroup
{
    public int Id;
    public List<int> Members = [];
    public List<CityPoint> Trail = [];
    public int Leader = -1;
}
