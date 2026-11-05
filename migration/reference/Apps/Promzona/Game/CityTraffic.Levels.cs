using MakarovPhysics.RigidBodies2D;
namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private readonly Dictionary<Body2D,(double Low,double High)> _heights=[];
    private readonly Dictionary<Body2D,(double Low,double High)> _levelStatics=[];
    private readonly Dictionary<Body2D,(double Low,double High)> _sceneryHeights=[];
    private readonly Dictionary<Body2D,int> _supportStatics=[],_bodyLevels=[];
    private void SceneryHeight(int index,double low,double high)=>_sceneryHeights[World.Bodies[index]]=(low,high);
    private static (double X,double Z) SceneryExtent(Body2D b){
        if(b.Shape==Shape2D.Circle)return (b.Radius,b.Radius);
        double c=Math.Abs(Math.Cos(b.Angle)),s=Math.Abs(Math.Sin(b.Angle));
        return (c*b.HalfW+s*b.HalfH,s*b.HalfW+c*b.HalfH);
    }
    public double PlayerGround=>OnFoot?CityRoadLevels.Ground(Player):Hero.SurfaceHeight+Hero.Lift;
    private bool HeightContact(Body2D a,Body2D b)
    {
        if(!_useScenery)return true;
        // A deck supports actors on its own layer vertically; it must not repel
        // them sideways through the planar solver. Below it the slab is solid.
        bool Supports(Body2D slab,Body2D actor){
            if(!_supportStatics.TryGetValue(slab,out int id))return false;
            if(_bodyLevels.GetValueOrDefault(actor)==id)return true;
            if(!_heights.TryGetValue(actor,out var h)||CityRoadLevels.ById(id) is not {} road)return false;
            // A nose/wheel can overlap the flush entry before the actor's centre
            // crosses the layer boundary. Do not turn that ramp into a vertical
            // curb. This exception is confined to an actor outside an endpoint;
            // the low underside farther along the ramp remains a solid obstacle.
            if(Math.Abs(actor.X-road.X)<CityRoadLevel.Length ||
               Math.Abs(slab.X-road.X)<CityRoadLevel.Length-8)return false;
            double ground=CityTerrain.Ground(actor.X,actor.Y);
            double entryX=road.X+Math.Sign(actor.X-road.X)*CityRoadLevel.Length;
            return h.Low>=ground-.05&&h.Low<ground+.8&&Math.Abs(h.Low-road.Floor(entryX,actor.Y))<.8;
        }
        if(Supports(a,b)||Supports(b,a))return false;
        (double Low,double High) Bounds(Body2D body)=>_heights.TryGetValue(body,out var h)?h:
            _levelStatics.TryGetValue(body,out h)?h:_sceneryHeights.TryGetValue(body,out h)?h:
            (CityTerrain.Ground(body.X,body.Y),CityTerrain.Ground(body.X,body.Y)+1.2);
        var x=Bounds(a);var y=Bounds(b);return x.Low<y.High&&y.Low<x.High;
    }
    private bool ClearLevelSight(CityPoint a,int la,CityPoint b,int lb){
        if(la==0&&lb==0)return ClearSight(a,b);
        if(la!=lb)return false;
        return CityTerrain.Current.ClearLine(a,CityRoadLevels.Surface(a.X,a.Z,la)+1,b,CityRoadLevels.Surface(b.X,b.Z,lb)+1);
    }
    private static double CarContactFloor(CityCar c)=>c.TerrainActive?c.SurfaceHeight:CityRoadLevels.Surface(c.Position.X,c.Position.Z,c.RoadLevel);
    private bool SameHeight(CityCar a,CityCar b)=>!_useScenery||Math.Abs(CarContactFloor(a)+a.Lift-CarContactFloor(b)-b.Lift)<Math.Max(a.Height,b.Height);
    private bool SameHeight(CityCar a,CityPedestrian b)=>!_useScenery||Math.Abs(CarContactFloor(a)+a.Lift-CityRoadLevels.Ground(b)-b.FlightHeight)<2;
    private void RefreshLevelBodies()
    {
        _heights.Clear();_bodyLevels.Clear();
        // The ordinary districts still use their original planar contacts. Only
        // enable the additional height pass where a live local body can meet a
        // level surface or one of its streamed barriers/pillars.
        bool needed=false;
        foreach(var b in _activeScenery)if(_levelStatics.ContainsKey(b)){needed=true;break;}
        if(!needed)foreach(var c in Cars)if(c.Physical&&(c.RoadLevel!=0||c.Kind==12||c.Destroyed&&c.Lift>.03)){needed=true;break;}
        if(!needed)foreach(var p in Pedestrians)if(p.Physical&&p.RoadLevel!=0){needed=true;break;}
        if(!needed)foreach(var s in Scooters)if(s.Physical&&s.RoadLevel!=0){needed=true;break;}
        World.ContactFilter=needed?HeightContact:null;
        if(!needed)return;
        foreach(var c in Cars)if(c.Physical){_bodyLevels[c.Body]=c.RoadLevel;_heights[c.Body]=(c.SurfaceHeight+c.Lift+.1,c.SurfaceHeight+c.Lift+c.Height);}
        foreach(var p in Pedestrians)if(p.Physical){
            double old=CityRoadLevels.Ground(p);p.RoadLevel=CityRoadLevels.Select(p.Body.X,p.Body.Y,p.RoadLevel,old+p.FlightHeight);
            double h=CityRoadLevels.Surface(p.Body.X,p.Body.Y,p.RoadLevel)+p.FlightHeight;_bodyLevels[p.Body]=p.RoadLevel;_heights[p.Body]=(h+.1,h+p.Height);
        }
        foreach(var s in Scooters)if(s.Physical){
            double h=CityRoadLevels.Surface(s.Position.X,s.Position.Z,s.RoadLevel)+s.Height;
            _bodyLevels[s.Body]=s.RoadLevel;_heights[s.Body]=(h+.03,h+.35);
        }
        foreach(var a in Animals)if(a.Body is {} ab&&a.Physical){double h=CityTerrain.Ground(a.Position)+a.Height;_heights[ab]=(h+.1,h+.7);}
        foreach(var p in _liveProps)if(p.Body is {} pb){double h=CityTerrain.Ground(p.Position)+p.Height;_heights[pb]=(h+.03,h+1.2);}
    }
    private void AddLevelScenery()
    {
        _levelStatics.Clear();_supportStatics.Clear();World.ContactFilter=HeightContact;
        void Box(double x,double z,double hx,double hz,double low,double high,double angle=0,int support=0){
            var body=World.Bodies[World.AddBox(x,z,hx,hz,0,.35,.08,0,angle)];
            _levelStatics[body]=(low,high);if(support!=0)_supportStatics[body]=support;
        }
        foreach(var s in CityRoadLevels.All){
            // Match the actual one-metre wall segments, including their tangent
            // on the curved approach. Axis-aligned boxes cut across those bends.
            for(double x=s.X-CityRoadLevel.Length;x<s.X+CityRoadLevel.Length;x+=1){
                double next=x+1,mid=x+.5,z0=s.CentreZ(x),z1=s.CentreZ(next),angle=Math.Atan2(z1-z0,1);
                double half=Math.Sqrt(1+(z1-z0)*(z1-z0))*.5;
                for(int side=-1;side<=1;side+=2){
                    double edge=s.Tunnel?5.8:5.65,z=(z0+z1)*.5+side*(edge+.07);
                    double y0=s.Floor(x,z0+side*edge),y1=s.Floor(next,z1+side*edge);
                    double top=s.Tunnel?(s.Roof(mid)?Math.Max(y0,y1)+CityRoadLevel.Clearance:
                        Math.Max(Math.Max(y0,y1)+.8,Math.Max(CityTerrain.Ground(x,z0+side*edge),CityTerrain.Ground(next,z1+side*edge))+.6)):
                        Math.Max(y0,y1)+.85;
                    Box(mid,z,half+.008,.07,Math.Min(y0,y1)-(s.Tunnel?0:.55),top,angle);
                }
                if(!s.Tunnel){
                    double low=double.MaxValue,high=double.MinValue;
                    foreach(double q in new[]{x,next})foreach(int side in new[]{-1,1}){
                        double y=s.Floor(q,s.CentreZ(q)+side*5.8);low=Math.Min(low,y-.55);high=Math.Max(high,y);
                    }
                    Box(mid,(z0+z1)*.5,half+.008,5.8,low,high,angle,s.Id);
                }
            }
            if(!s.Tunnel)for(double x=s.X-54;x<=s.X+54;x+=36){
                for(int side=-1;side<=1;side+=2){double z=s.CentreZ(x)+side*7.1;
                    Box(x,z,.7,.7,CityTerrain.Ground(x,z),s.Floor(x,z)-.55);
                }
                double floor=s.CentreFloor(x);Box(x,s.CentreZ(x),1,7.8,floor-1.15,floor-.45,0,s.Id);
            }
            if(s.Tunnel)foreach(double x in new[]{s.X-54,s.X+54}){
                double y=s.CentreFloor(x),ground=CityTerrain.Ground(x,s.CentreZ(x));
                Box(x,s.CentreZ(x),.65,6.6,y+4.9,ground);
                for(int side=-1;side<=1;side+=2)Box(x,s.CentreZ(x)+side*6.2,.65,.4,y-.1,y+4.9);
            }
        }
    }
    // The connector is a single directed edge with its own continuous path.
    // Crossing surface streets do not share its node reservation or turn choices.
    public bool BeginLevelRoute(CityCar c,int id,bool east)
    {
        var s=CityRoadLevels.ById(id);if(s is null)return false;
        FreePoliceReservation(c);c.RoadLevel=id;c.Path=s.Route(east);c.Along=0;c.Phase=3;
        c.Direction=c.Outgoing=east?1:3;c.LaneIndex=0;c.I=s.I+(east?4:-4);c.J=s.J;
        return true;
    }
    private int MissionLevel(CityCar c)=>Mission(c) is {} m ? m.Kind==CityService.Police?(OnFoot?Player.RoadLevel:Hero.RoadLevel):Pedestrians.Find(p=>p.Id==m.PatientId)?.RoadLevel??0 : 0;
    private bool LevelWalk(CityPedestrian p,double dt){
        if(p.ServiceId!=0||CityRoadLevels.ById(p.RoadLevel) is not {} road)return false;
        double sign=p.Position.X<road.X?-1:1,x=p.Position.X+sign*2;
        var goal=new CityPoint(Math.Clamp(x,road.X-144,road.X+144),road.CentreZ(x)+Math.Sign(p.Position.Z-road.CentreZ(p.Position.X))*5);
        var d=goal-p.Position;var v=d*(p.Cruise/Math.Max(.2,d.Length));
        if(p.Physical)MovePerson(p,v,dt);else MoveFarSocial(p,v,dt);
        if(Math.Abs(p.Position.X-road.X)>143){p.RoadLevel=0;Reanchor(p);}
        return true;
    }
    private static CityRoadLevel? DepartureLevel(CityCar c){
        foreach(var s in CityLayout.Current.Levels)if(c.J==s.J&&
            (c.I==s.I-4&&c.Outgoing==1||c.I==s.I+4&&c.Outgoing==3))return s;
        return null;
    }
    private static bool GroundCarSpawnClear(CityCar c)=>!CityRoadLevels.Reserved(c.Position,c.HalfLength+.5);
    private void SeedLevelTraffic(){
        if(CityLayout.Current.RoadRevision<2)return;
        // Reassign a bounded number of existing vehicles: population is unchanged.
        // Ordinary controllers and physical/rule LOD take over immediately.
        int index=Cars.Count-1;
        foreach(var s in CityLayout.Current.Levels)foreach(bool east in new[]{true,false})for(int n=0;n<4;n++){
            while(index>0&&(Cars[index].Hero||Mission(Cars[index]) is not null))index--;
            if(index<=0)return;var c=Cars[index--];FreePoliceReservation(c);BeginLevelRoute(c,s.Id,east);
            c.Along=32+n*65;var pose=c.Path.Sample(c.Along);c.Position=pose.Point;c.Heading=pose.Heading;
            c.Body.X=pose.Point.X;c.Body.Y=pose.Point.Z;c.Body.Angle=-pose.Heading;c.Body.W=0;
            c.Body.Vx=Math.Sin(c.Heading)*c.Cruise;c.Body.Vy=Math.Cos(c.Heading)*c.Cruise;c.Speed=c.Cruise;
            c.Stuck=c.ReverseFor=0;UpdateTerrainSupport(c);
        }
    }
    private bool TryLevelRoute(CityCar c)
    {
        foreach(var s in CityRoadLevels.All){
            bool east=c.Outgoing==1&&c.I==s.I-4,west=c.Outgoing==3&&c.I==s.I+4;
            if(c.J==s.J&&(east||west)){
                // Already in the central junction: join the ramp at its current X.
                BeginLevelRoute(c,s.Id,east);c.Along=c.Path.ClosestDistance(c.Position);return true;
            }
        }
        return false;
    }
    private void FinishLevelRoute(CityCar c)
    {
        c.RoadLevel=0;c.Phase=0;c.Path=new(c.Position,new CityPoint(Coordinate(c.I),Coordinate(c.J))+Forward(c.Direction)*9+Right(Forward(c.Direction))*Lane);
        c.Along=0;c.Outgoing=c.Direction;c.Phase=1;
    }
}
