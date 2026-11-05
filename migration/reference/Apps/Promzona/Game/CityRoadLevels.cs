namespace Promzona.Game;

/// <summary>Explicit, deterministic additional road surfaces. Ground remains the
/// foundation datum; bridges and covered tunnels never replace that datum.</summary>
public sealed class CityRoadLevel(int id, int i, int j, bool tunnel)
{
    public int Id {get;}=id;
    public int I {get;}=i;
    public int J {get;}=j;
    public bool Tunnel {get;}=tunnel;
    public const double Length=144, HalfWidth=5.8, Covered=54, Clearance=4.9;
    public double X=>CityTraffic.Coordinate(I);
    public double Z=>CityTraffic.Coordinate(J);
    private static double Smooth(double t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
    public double CentreZ(double x)=>Z+18*Smooth((Length-Math.Abs(x-X))/36);
    public bool Contains(double x,double z,double margin=0)=>Math.Abs(x-X)<=Length+margin&&Math.Abs(z-CentreZ(x))<=HalfWidth+margin;
    public double Elevation(double x)=>(Tunnel?-8:8)*Smooth((Length-Math.Abs(x-X))/90);
    private double[] _floor=[];
    public double FloorSlope {get;private set;}
    internal void Bake(CityTerrain terrain){
        _floor=new double[289];
        double left=terrain.BedHeight(X-Length,Z),right=terrain.BedHeight(X+Length,Z);FloorSlope=(right-left)/(2*Length);
        double depth=8;
        if(Tunnel)for(int k=90;k<=198;k++){double x=X-Length+k;depth=Math.Max(depth,left+k*FloorSlope-terrain.BedHeight(x,CentreZ(x))+Clearance+2);}
        for(int k=0;k<_floor.Length;k++){double x=X-Length+k;
            _floor[k]=(Tunnel?left+k*FloorSlope-depth*Smooth((Length-Math.Abs(x-X))/90):terrain.BedHeight(x,CentreZ(x))+Elevation(x))+.012;
        }
    }
    public double CentreFloor(double x){
        if(_floor.Length==0)return CityTerrain.Ground(x,CentreZ(x));
        double u=Math.Clamp(x-X+Length,0,288);int k=Math.Min(287,(int)u);return _floor[k]+(_floor[k+1]-_floor[k])*(u-k);
    }
    public double Floor(double x,double z){
        double cross=Smooth((Math.Abs(x-X)-(Length-30))/30);
        // Join the entire width to the existing road crossfall, not only its
        // centre point. Both wheel support and the mesh use this same blend.
        return CentreFloor(x)+(cross>0?cross*(CityTerrain.Ground(x,z)-CityTerrain.Ground(x,CentreZ(x))):0);
    }
    public bool Roof(double x)=>Tunnel&&Math.Abs(x-X)<=Covered;
    public bool Open(double x,double z)=>Tunnel&&Contains(x,z,.1)&&!Roof(x);
    public bool Reserve(double x,double z,double radius=0)=>Contains(x,z,radius+1.3)&&(!Tunnel||Math.Abs(x-X)+radius>Covered-.8);
    public CityPath Route(bool east)
    {
        var points=new CityPoint[289];
        for(int n=0;n<points.Length;n++){
            double x=X+(east?-Length:Length)+(east?1:-1)*n;
            points[n]=new(x,CentreZ(x)+(east?1:-1)*CityTraffic.Lane);
        }
        return CityPath.Restore(points);
    }
}

public static class CityRoadLevels
{
    public static IReadOnlyList<CityRoadLevel> All=>CityLayout.Current.Levels;
    public static CityRoadLevel? At(double x,double z,double margin=0){foreach(var s in CityLayout.Current.Levels)if(s.Contains(x,z,margin))return s;return null;}
    public static CityRoadLevel? ById(int id){if(id==0)return null;foreach(var s in CityLayout.Current.Levels)if(s.Id==id)return s;return null;}
    public static bool OpenCut(double x,double z){foreach(var s in CityLayout.Current.Levels)if(s.Open(x,z))return true;return false;}
    public static bool GroundWalkEdge(CityPoint a,CityPoint b)
    {
        foreach(var s in CityLayout.Current.Levels){
            if(!s.Tunnel||Math.Max(a.X,b.X)<s.X-CityRoadLevel.Length-1||Math.Min(a.X,b.X)>s.X+CityRoadLevel.Length+1||
                Math.Max(a.Z,b.Z)<s.Z-7||Math.Min(a.Z,b.Z)>s.Z+25)continue;
            var delta=b-a;int count=Math.Max(1,(int)Math.Ceiling(delta.Length));
            for(int n=0;n<=count;n++){var p=a+delta*(n/(double)count);
                if(s.Contains(p.X,p.Z,.65)&&!s.Roof(p.X))return false;
            }
        }
        return true;
    }
    public static bool Reserved(CityPoint p,double radius=0)=>All.Any(s=>s.Reserve(p.X,p.Z,radius));
    public static bool ReservedBlock(int i,int j)=>All.Any(s=>s.Reserve(CityTraffic.Coordinate(i)+18,CityTraffic.Coordinate(j)+18,16));
    public static double Surface(double x,double z,int level)=>ById(level) is {} s&&s.Contains(x,z,1)?s.Floor(x,z):CityTerrain.Ground(x,z);
    public static int Select(double x,double z,int previous,double height)
    {
        var s=At(x,z);
        if(s is null)return 0;
        double y=s.Floor(x,z),ground=CityTerrain.Ground(x,z);
        if(previous==s.Id)return previous;
        // An open cutting has no district surface above it. Select its floor
        // even before an airborne actor lands; the caller preserves world Y.
        // The covered section and bridge deck still have two distinct layers.
        if(s.Open(x,z))return s.Id;
        // A surface may only be acquired at its entry ramp or by landing on it.
        // Being directly underneath a bridge never snaps an actor onto its deck.
        if(Math.Abs(height-y)<.65 && (Math.Abs(s.Elevation(x))<.9 || Math.Abs(height-y)<Math.Abs(height-ground)))return s.Id;
        return 0;
    }
    public static bool Solid(double x,double y,double z,double radius=.1)
    {
        double ground=CityTerrain.Ground(x,z);
        var s=At(x,z,radius);
        if(s is null)return y<ground+radius;
        double floor=s.Floor(x,z);
        bool wall=Math.Abs(z-s.CentreZ(x))>CityRoadLevel.HalfWidth-.2-radius&&Math.Abs(x-s.X)<CityRoadLevel.Length-2;
        double wallTop=s.Tunnel?(s.Roof(x)?floor+CityRoadLevel.Clearance:Math.Max(ground+.6,floor+.8)):floor+.85;
        if(wall&&y>floor-(s.Tunnel?0:.55)-radius&&y<wallTop+radius)return true;
        if(s.Tunnel){
            if(s.Roof(x))return y<floor+radius || y>floor+CityRoadLevel.Clearance-radius && y<ground+radius;
            return y<floor+radius;
        }
        return y<ground+radius || y>floor-.55-radius&&y<floor+radius;
    }
    public static bool ClearAt(CityPoint p,int level)=>ById(level) is {} s&&s.Contains(p.X,p.Z,-.45);
    public static double Ground(CityPedestrian p)=>Surface(p.Position.X,p.Position.Z,p.RoadLevel);
}
