namespace Promzona.Game;
public enum CityNeighbourhood { Estate, Courtyard, PrivateHouses, Centre, Garages, Industry }
public static class CityNeighbourhoods
{
    public static CityNeighbourhood At(int i,int j,int seed,int nodes)
    {
        if(nodes==49&&CityIndustrial.Zone(i,j))return CityNeighbourhood.Industry;
        double scale=(nodes-1)/48d;
        var centres=new (double X,double Z,CityNeighbourhood Kind)[]{
            (12,12,CityNeighbourhood.PrivateHouses),(39,36,CityNeighbourhood.PrivateHouses),
            (22,25,CityNeighbourhood.Centre),(9,34,CityNeighbourhood.Garages),
            (36,11,CityNeighbourhood.Courtyard),(24,41,CityNeighbourhood.Estate)};
        double best=double.MaxValue;var result=CityNeighbourhood.Estate;
        foreach(var c in centres){double dx=i-(c.X+(seed%5-2))*scale,dz=j-(c.Z+((seed/5)%5-2))*scale;
            double distance=dx*dx+dz*dz;if(distance<best){best=distance;result=c.Kind;}}
        return result;
    }
    public static CityNeighbourhood At(CityPoint p){
        int i=(int)Math.Floor((p.X-CityTraffic.Coordinate(0))/36),j=(int)Math.Floor((p.Z-CityTraffic.Coordinate(0))/36);
        return At(i,j,CityLayout.Current.Seed,CityTraffic.Nodes);
    }
    public static string Name(CityNeighbourhood k,bool ru)=>k switch{
        CityNeighbourhood.PrivateHouses=>ru?"САДОВЫЙ ПОСЁЛОК":"GARDEN SUBURB",
        CityNeighbourhood.Courtyard=>ru?"СТАРЫЕ ДВОРЫ":"OLD COURTYARDS",
        CityNeighbourhood.Centre=>ru?"ЦЕНТР":"TOWN CENTRE",
        CityNeighbourhood.Garages=>ru?"ГАРАЖНЫЙ КООПЕРАТИВ":"GARAGE COOPERATIVE",
        CityNeighbourhood.Industry=>ru?"ПРОМЗОНА":"INDUSTRIAL DISTRICT",
        _=>ru?"МИКРОРАЙОН":"HOUSING ESTATE"};
    internal static IEnumerable<CityModel.Obstacle> Buildings(int i,int j,int seed,int nodes,double x,double z)
    {
        var kind=At(i,j,seed,nodes);double v=((uint)(i*7919+j*104729+seed)%4);
        if(kind==CityNeighbourhood.PrivateHouses){
            yield return new(x-6,z-5,4.4,5,4.5+v*.15);yield return new(x+6,z+5,4,4.5,4.8);yield break;
        }
        if(kind==CityNeighbourhood.Garages){
            for(int a=-1;a<=1;a++)for(int side=-1;side<=1;side+=2)yield return new(x+a*6.7,z+side*7,3.1,4,3.4);
            yield break;
        }
        if(kind==CityNeighbourhood.Courtyard){
            yield return new(x,z-8.6,11,3.2,12.5);yield return new(x-8.5,z+1.7,3.2,7.1,12.5);
            yield return new(x+8.5,z+4.5,3.2,4.3,10);yield break;
        }
        if(kind==CityNeighbourhood.Centre){
            yield return new(x-6.5,z,4.7,10,19+v*3);
            yield return new(x+6.5,z,4.7,9,14+v*2);yield break;
        }
        yield return new(x,z-7,11,3.8,17+v*3);
        yield return new(x,z+7,11,3.8,17+v*3);
    }
}
