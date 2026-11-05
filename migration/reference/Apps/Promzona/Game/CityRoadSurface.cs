namespace Promzona.Game;

/// <summary>Shared planar carriageway footprint, including the outside half of
/// perimeter streets. Pedestrian promenades and sand are not roads.</summary>
public static class CityRoadSurface
{
    public static bool Contains(CityPoint p)
    {
        double low=CityTraffic.Coordinate(0),r=CityTraffic.RoadHalf;
        int i=(int)Math.Round((p.X-low)/36),j=(int)Math.Round((p.Z-low)/36);
        if(i<0||j<0||i>=CityTraffic.Nodes||j>=CityTraffic.Nodes)return false;
        double dx=p.X-CityTraffic.Coordinate(i),dz=p.Z-CityTraffic.Coordinate(j);
        if(Math.Abs(dx)<=r&&Math.Abs(dz)<=r)return CityTraffic.Degree(i,j)>0;
        if(Math.Abs(dx)<=r)return CityTraffic.HasRoad(i,j,dz>=0?0:2);
        if(Math.Abs(dz)<=r)return CityTraffic.HasRoad(i,j,dx>=0?1:3);
        return false;
    }
    public static bool Rectangle(double x,double z,double hx,double hz)=>
        Contains(new(x-hx,z-hz))&&Contains(new(x+hx,z-hz))&&
        Contains(new(x-hx,z+hz))&&Contains(new(x+hx,z+hz));
}
