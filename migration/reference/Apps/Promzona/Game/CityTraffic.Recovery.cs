namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private double _recoveryAt;
    /// <summary>Explicit rescue, not a repair: preserves damage, identity, inventory and wanted level.</summary>
    private bool RecoverToRoad(CityCar c){
        if(Time<_recoveryAt||c.Destroyed||c.Falling||Math.Abs(c.Speed)>1.5)return false;
        CityPoint? found=null;double heading=c.Heading,best=double.MaxValue;double railAlong=c.TransitAlong;
        if(c.Kind is 9 or 10&&CityTransit.ById(c.TransitRoute) is {} rail){
            for(double s=0;s<rail.Length;s+=6){var pose=rail.Sample(s);double d=(pose.Point-c.Position).Length;if(d<best&&SafeRecoveryPose(c,pose.Point,pose.Heading)){found=pose.Point;heading=pose.Heading;best=d;railAlong=s;}}
        }else{
            int ci=(int)Math.Round((c.Position.X-Coordinate(0))/Spacing),cj=(int)Math.Round((c.Position.Z-Coordinate(0))/Spacing);
            for(int i=Math.Max(1,ci-5);i<Math.Min(Nodes-1,ci+6);i++)for(int j=Math.Max(1,cj-5);j<Math.Min(Nodes-1,cj+6);j++)for(int dir=0;dir<4;dir++){
                if(!HasRoad(i,j,dir))continue;var f=Forward(dir);var at=new CityPoint(Coordinate(i),Coordinate(j))+f*18+Right(f)*Lane;
                double distance=(at-c.Position).Length;if(distance>=best||!SafeRecoveryPose(c,at,Math.Atan2(f.X,f.Z)))continue;
                found=at;heading=Math.Atan2(f.X,f.Z);best=distance;
            }
        }
        if(found is not {} point){Message("rescue_blocked");return false;}
        FreePoliceReservation(c);c.Position=point;c.Heading=heading;c.Body.X=point.X;c.Body.Y=point.Z;c.Body.Angle=-heading;
        c.Body.Vx=c.Body.Vy=c.Body.W=c.Speed=c.VerticalVelocity=0;c.Body.Wake();c.RoadLevel=0;c.Phase=0;c.TransitAlong=railAlong;
        c.Lift=0;UpdateTerrainSupport(c);_recoveryAt=Time+8;_sceneryClock=_detailClock=0;RebuildActorGrid();Message("rescued");return true;
    }
    private bool SafeRecoveryPose(CityCar c,CityPoint at,double heading){
        if(!CityWater.UrbanSite(at,c.HalfWidth+1)||CityWater.Depth(at)>.05||CityRoadLevels.At(at.X,at.Z,c.HalfLength+1)!=null)return false;
        var f=new CityPoint(Math.Sin(heading),Math.Cos(heading));var r=Right(f);double low=double.MaxValue,high=double.MinValue;
        for(int a=-1;a<=1;a++)for(int b=-1;b<=1;b++){
            var p=at+f*(a*(c.HalfLength+.4))+r*(b*(c.HalfWidth+.4));
            if(!CityRoadSurface.Contains(p)||_useScenery&&!ClearWalk(p)||CityLayout.Current.FurnitureNear(p.X,p.Z,.3))return false;
            double y=CityTerrain.Ground(p);low=Math.Min(low,y);high=Math.Max(high,y);
        }
        if(high-low>1.1||!CarClear(at,heading,c))return false;
        foreach(var p in NearbyPeople(at,10))if(!p.Dead&&CarPersonOverlap(at,heading,p.Position,.8,c.HalfLength,c.HalfWidth))return false;
        foreach(var p in NearbyProps(at,10))if(CarPersonOverlap(at,heading,p.Position,p.Radius+.4,c.HalfLength,c.HalfWidth))return false;
        return true;
    }
    private CityPoint? EmergencyExit(CityCar c){
        if(c.Destroyed||c.Falling||Math.Abs(c.Speed)>1.5)return null;
        // Search on the same road layer, so a tunnel exit never teleports onto its roof.
        for(double radius=2;radius<=10;radius+=1)for(int n=0;n<24;n++){
            var at=c.Position+new CityPoint(Math.Sin(n*Math.Tau/24),Math.Cos(n*Math.Tau/24))*radius;
            if(_useScenery&&CityWater.Depth(at)>.1||CarPersonOverlap(c.Position,c.Heading,at,.6,c.HalfLength,c.HalfWidth)||!ClearPlayerPoint(at,c))continue;
            double y=CityRoadLevels.Surface(at.X,at.Z,c.RoadLevel);
            if(_useScenery&&Math.Abs(y-c.SurfaceHeight)>3)continue;
            return at;
        }
        return null;
    }
}
