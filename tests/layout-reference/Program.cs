using System.Globalization;
using Promzona.Game;
CultureInfo.CurrentCulture=CultureInfo.InvariantCulture;
using var output=new StreamWriter(args[0]);
foreach(int seed in new[]{0,73921,20261105,-1947,int.MinValue,int.MaxValue}) {
    output.Write($"random {seed}");var r=new Random(seed);
    for(int k=0;k<128;k++)output.Write($" {r.NextDouble():R}");output.WriteLine();
}
foreach(var (n,wide) in new[]{(33,false),(33,true),(49,false),(49,true)})
foreach(int seed in new[]{73921,20261105,-1947}) {
    // Actual snapshot algorithm, revision zero; excluded dependencies throw if reached.
    var city=CityLayout.Generate(seed,gridSize:n,wideRoads:wide,roadRevision:0);
    output.WriteLine($"city {seed} {n} {(wide?1:0)}");
    for(int j=0;j<n;j++)for(int i=0;i<n;i++)for(int d=0;d<4;d++)output.Write(city.Road(i,j,d)?'1':'0');
    output.WriteLine();
    for(int j=0;j<n;j++)for(int i=0;i<n;i++)output.Write(city.Park(i,j)?'1':'0');
    output.WriteLine();
    foreach(var boxes in new[]{city.Blocks,city.Furniture}) {
        output.WriteLine(boxes.Length);
        foreach(var b in boxes)output.WriteLine($"{b.X:R} {b.Z:R} {b.HalfX:R} {b.HalfZ:R} {b.Height:R}");
    }
}
output.WriteLine("end");

namespace Promzona.Game {
    // Only services not used by the revision-zero generator are stubbed. Terrain
    // creation is omitted: these fixtures prove layout parity, not terrain parity.
    public static class CityModel {public static Obstacle[] Blocks=>throw new NotSupportedException();public record struct Obstacle(double X,double Z,double HalfX,double HalfZ,double Height);}
    public sealed class CityTerrain {public CityTerrain(int seed,List<CityModel.Obstacle> b,Action<double,string>? p,CancellationToken c,bool industry){if(industry)throw new NotSupportedException();}}
    public sealed class CityRoadLevel {
        public int I,J;public CityRoadLevel(int id,int i,int j,bool tunnel)=>throw new NotSupportedException();
        public bool Reserve(double x,double z,double margin)=>throw new NotSupportedException();
        public void Bake(CityTerrain terrain)=>throw new NotSupportedException();
    }
    public static class CityIndustrial {
        public const int Revision=3,ZoneWest=0,ZoneEast=0,ZoneRow=0;
        public static bool RailEdge(int i,int j,int d)=>throw new NotSupportedException();
        public static bool Zone(int i,int j)=>throw new NotSupportedException();
        public static IEnumerable<(CityModel.Obstacle,int)> Obstacles(int i,int j)=>throw new NotSupportedException();
    }
    public static class CityNeighbourhoods {public static IEnumerable<CityModel.Obstacle> Buildings(int i,int j,int seed,int n,double x,double z)=>throw new NotSupportedException();}
    public static class CityImportedSites {public static bool ExtraClear(CityPoint p,double r)=>throw new NotSupportedException();}
    public static class CityTraffic {
        public const int Spacing=36; public static int Nodes=>CityLayout.GridSize;
        public static double Coordinate(int i)=>-(Nodes-1)*18+i*36;
        public static double BlockCentre(int i)=>Coordinate(i)+18;
        public static int BlockIndex(double x)=>(int)Math.Floor((x-Coordinate(0))/36);
        public static CityPoint Forward(int d)=>d switch {0=>new(0,1),1=>new(1,0),2=>new(0,-1),_=>new(-1,0)};
        public static readonly (int I,int J)[] ClinicBlocks=[(14,14),(19,16),(12,20),(20,10)];
        public static bool IsClinic(int i,int j)=>ClinicBlocks.Contains((i,j));
        public static bool IsStation(int i,int j)=>i==21&&j==13;
    }
    public static class CityWater {
        public static bool UrbanSite(CityPoint p,double radius)=>Math.Max(Math.Abs(p.X),Math.Abs(p.Z))+radius<=CityTraffic.Coordinate(CityTraffic.Nodes-1)+7&&RiverDistance(p.X)>18+radius;
        static double RiverDistance(double x)=>!CityLayout.Rivers?double.PositiveInfinity:Math.Min(Math.Abs(x-CityTraffic.BlockCentre(CityTraffic.Nodes/3)),Math.Abs(x-CityTraffic.BlockCentre(2*CityTraffic.Nodes/3)));
    }
    public static class CityAmenities {
        public static bool ServiceClear(CityPoint p,double r)=>throw new NotSupportedException();
        public static bool ClinicClear(CityPoint p)=>CityTraffic.ClinicBlocks.All(s=>(CityCivicSites.Entrance(s.I,s.J)-p).Length>3)&&(CityCivicSites.Entrance(21,13)-p).Length>3;
    }
}
