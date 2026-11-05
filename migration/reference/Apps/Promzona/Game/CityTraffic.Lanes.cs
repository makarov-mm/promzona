namespace Promzona.Game;
public sealed partial class CityTraffic {
    public static double LanePosition(CityCar c)=>Lane+(CityLayout.WideRoads?c.LaneIndex*3.35:0);
    public bool LaneGap(CityCar c,int lane){
        var f=Forward(c.Direction);var right=Right(f);var centre=new CityPoint(Coordinate(c.I),Coordinate(c.J));
        var aim=centre+right*(Lane+lane*3.35);double extent=c.HalfLength;
        foreach(var other in NearbyCars(c.Position,40)){
            if(other==c||other.Falling||!SameHeight(c,other))continue;var d=other.Position-c.Position;double along=CityPoint.Dot(d,f);
            double lateral=Math.Abs(CityPoint.Dot(other.Position-aim,right));
            bool converging=other.Phase==0&&other.LaneIndex==lane&&Math.Cos(other.Heading-c.Heading)>.8;
            if((lateral<c.HalfWidth+other.HalfWidth+.55||converging)&&along>-extent-other.HalfLength-4-Math.Max(0,other.Speed-c.Speed)*2 && along<extent+other.HalfLength+6+Math.Max(0,c.Speed-other.Speed)*2)return false;
        }
        return !NearbyPeople(c.Position,18).Any(p=>SameHeight(c,p)&&Math.Abs(CityPoint.Dot(p.Position-aim,right))<c.HalfWidth+.7&&Math.Abs(CityPoint.Dot(p.Position-c.Position,f))<12);
    }
    public bool TryChangeLane(CityCar c,int lane){
        if(!CityLayout.WideRoads||lane is <0 or >1||lane==c.LaneIndex||c.Phase!=0||c.Path.Length-c.Along<6||!LaneGap(c,lane))return false;
        var f=Forward(c.Direction);var centre=new CityPoint(Coordinate(c.I),Coordinate(c.J));
        var end=centre-f*StopLine+Right(f)*(Lane+lane*3.35);
        double length=CityPoint.Dot(end-c.Position,f);if(length<6)return false;
        var start=c.Position;double handle=length*.40;
        c.Path=new(start,start+f*handle,end-f*handle,end);c.Along=0;c.LaneIndex=lane;c.NextLaneDecision=Time+8;c.LaneWait=0;return true;
    }
    private void ConsiderLaneChange(CityCar c,double dt){
        if(!CityLayout.WideRoads||c.Hero||c.Phase!=0||c.Kind==5||c.Driverless||c.BusUntil>Time)return;
        var f=Forward(c.Direction);var r=Right(f);
        // Only the nearest obstruction is needed; sorting/allocating every
        // candidate list for every distant vehicle cost several thousand lists/s.
        CityCar? blocker=null;double nearest=double.PositiveInfinity;
        foreach(var other in NearbyCars(c.Position,24)){
            if(other==c||!SameHeight(c,other))continue;
            var delta=other.Position-c.Position;
            if(CityPoint.Dot(delta,f)<=0||Math.Abs(CityPoint.Dot(delta,r))>=c.HalfWidth+other.HalfWidth+.4)continue;
            double distance=CityPoint.Dot(delta,delta);
            if(distance<nearest){nearest=distance;blocker=other;}
        }
        bool slow=blocker is not null&&(blocker.Destroyed||blocker.Kind==5||blocker.Speed<c.Cruise*.65);
        c.LaneWait=slow?c.LaneWait+dt:0;
        if(Time<c.NextLaneDecision)return;
        if(slow&&(blocker!.Kind==5||c.LaneWait>2)){c.NextLaneDecision=Time+1.5;if(Random()<.65)TryChangeLane(c,1-c.LaneIndex);}
    }
}
