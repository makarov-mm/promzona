namespace Promzona.Game;

public readonly record struct GarageSite(int Id, CityPoint Position)
{
    public const double HalfX = 3.2, HalfZ = 2.6;
    internal const double WallHeight = 3.4;
    internal const double ForecourtDepth = 3.5;
    internal const double ServiceRadius = 10;
    public CityPoint Door => Position + new CityPoint(0, -HalfZ);
}