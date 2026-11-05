namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public bool DroneActive {get;private set;}
    public CityPoint DronePosition {get;private set;}
    public double DroneHeight {get;private set;}
    public double DroneHeading,DronePitch=.3;
    public CityPoint DroneInput;
    public double DroneLiftInput;
    public bool DroneScanHeld;
    public double DroneBattery {get;private set;}
    public double DroneHealth {get;private set;}=1;
    public double DroneScanProgress {get;private set;}
    public int DroneScans {get;private set;}
    public string DroneNotice {get;private set;}="";
    public CityModel.Obstacle[] DroneTargets {get;private set;}=[];
    private double _droneCooldown,_droneShotAt;
    private CityPoint _droneVelocity;
    private CityModel.Obstacle[]? _droneWorld;
    private CityModel.Obstacle[] _droneBuildings=[];
    private CityPoint _droneFocus;
    private void DroneBuildings(CityPoint p)
    {
        if(ReferenceEquals(_droneWorld,CityModel.Blocks)&&(p-_droneFocus).Length<12)return;
        _droneWorld=CityModel.Blocks;_droneFocus=p;
        _droneBuildings=_droneWorld.Where(b=>Math.Abs(b.X-p.X)<70+b.HalfX&&Math.Abs(b.Z-p.Z)<70+b.HalfZ).ToArray();
    }
    private bool DroneSight(CityPoint target,double height)
    {
        var d=target-DronePosition;double dy=height-DroneHeight;
        int steps=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(d.Length*d.Length+dy*dy)/.3));
        for(int n=1;n<steps;n++)if(!DroneClear(DronePosition+d*((double)n/steps),DroneHeight+dy*n/steps))return false;
        return true;
    }
    public void ToggleDrone()
    {
        if(DroneActive){EndDrone("returned");return;}
        if(!OnFoot||OnScooter||PlayerUnavailable||Player.KnockedFor>0||Player.Detached||EnteringCarId>=0||Time<_droneCooldown)return;
        // Local reconnaissance: player remains a vulnerable actor at the launch site.
        // Flight data is transient, intentionally cancelled on load rather than serializing a remote possession.
        DroneTargets=CityModel.Blocks.Where(b=>b.Height>=6&&b.Height<25&&(new CityPoint(b.X,b.Z)-PlayerPosition).Length is >22 and <135)
            .OrderBy(b=>(new CityPoint(b.X,b.Z)-PlayerPosition).Length).Take(3).ToArray();
        if(DroneTargets.Length<3){DroneNotice="no_targets";return;}
        DroneBuildings(PlayerPosition);
        CityPoint? launch=null;
        for(int n=0;n<16;n++){
            var p=PlayerPosition+new CityPoint(Math.Sin(n*Math.Tau/16),Math.Cos(n*Math.Tau/16))*.8;
            if(DroneClear(p,PlayerGround+2.4)){launch=p;break;}
        }
        if(launch is null){DroneNotice="blocked";return;}
        DroneActive=true;DronePosition=launch.Value;DroneHeight=PlayerGround+2.4;
        DroneHeading=PlayerViewHeading;DronePitch=.3;DroneBattery=100;DroneHealth=1;DroneScans=0;DroneScanProgress=0;
        DroneNotice="";_droneVelocity=default;WalkInput=default;PlayerFireHeld=false;
    }
    private void EndDrone(string reason)
    {
        DroneActive=false;DroneNotice=reason;DroneInput=default;DroneLiftInput=0;DroneScanHeld=false;
        _droneCooldown=Time+(reason=="complete"?60:reason=="returned"?2:20);
    }
    public void ResetDrone(){DroneActive=false;DroneTargets=[];DroneNotice="";_droneCooldown=0;DroneInput=default;DroneLiftInput=0;DroneScanHeld=false;}
    public void DamageDrone(double damage)
    {
        if(!DroneActive||!double.IsFinite(damage)||damage<=0)return;
        DroneHealth=Math.Max(0,DroneHealth-damage);if(DroneHealth<=0)EndDrone("lost");
    }
    private bool DroneClear(CityPoint p,double height)
    {
        if(Math.Abs(p.X)>LandEdge||Math.Abs(p.Z)>LandEdge||CityRoadLevels.Solid(p.X,height,p.Z,.35))return false;
        foreach(var b in _droneBuildings)
            if(Math.Abs(p.X-b.X)<b.HalfX+.35&&Math.Abs(p.Z-b.Z)<b.HalfZ+.35&&height<CityTerrain.Ground(b.X,b.Z)+b.Height+.4)return false;
        // Grade-separated slabs must not be passed through from above or below.
        foreach(var body in _activeScenery){
            if(!_sceneryHeights.TryGetValue(body,out var h)&&!_levelStatics.TryGetValue(body,out h))continue;
            if(height<h.Low-.35||height>h.High+.35)continue;
            var e=SceneryExtent(body);if(Math.Abs(p.X-body.X)<e.X+.35&&Math.Abs(p.Z-body.Y)<e.Z+.35)return false;
        }
        return true;
    }
    private void DroneStep(double dt)
    {
        if(!DroneActive)return;
        WalkInput=default;PlayerFireHeld=false;
        if(PlayerUnavailable||!OnFoot||Player.KnockedFor>0){EndDrone("interrupted");return;}
        DroneBuildings(DronePosition);
        DroneBattery=Math.Max(0,DroneBattery-dt);
        if(DroneBattery<=0){EndDrone("battery");return;}
        var desired=DroneInput;if(desired.Length>1)desired*=1/desired.Length;
        _droneVelocity+=(desired*11-_droneVelocity)*(1-Math.Exp(-dt*4));
        double lift=Math.Clamp(DroneLiftInput,-1,1)*5;
        int steps=Math.Max(1,(int)Math.Ceiling(Math.Max(_droneVelocity.Length,Math.Abs(lift))*dt/.18));
        for(int i=0;i<steps&&DroneActive;i++){
            var next=DronePosition+_droneVelocity*(dt/steps);
            double height=Math.Min(PlayerGround+65,DroneHeight+lift*dt/steps);
            if((next-PlayerPosition).Length>170){_droneVelocity=default;DroneNotice="range";break;}
            if(!DroneClear(next,height)){_droneVelocity=default;DamageDrone(dt/steps*.35);break;}
            DronePosition=next;DroneHeight=height;DroneNotice="";
        }
        // Nearby hostile officers can shoot down a low-flying drone. No remote actor activation.
        if(Time>=_droneShotAt&&WantedLevel>0){
            // Grid query: the former scan of all walkers ran every frame while wanted.
            CityPedestrian? officer=null;
            foreach(var p in NearbyPeople(DronePosition,55))
                if(p.Officer&&!p.Dead&&p.KnockedFor<=0&&p.Armed&&(p.Position-DronePosition).Length<55&&DroneHeight-CityRoadLevels.Ground(p)<32&&ClearSight(p.Position,DronePosition)){officer=p;break;}
            if(officer is not null){_droneShotAt=Time+.8;Sound("shot",officer.Position,.5);DamageDrone(.12);}
        }
        if(!DroneActive||DroneScans>=DroneTargets.Length||DroneTargets.Length==0)return;
        var target=DroneTargets[DroneScans];var delta=new CityPoint(target.X,target.Z)-DronePosition;
        double dy=CityTerrain.Ground(target.X,target.Z)+target.Height+.7-DroneHeight;
        double distance=Math.Sqrt(delta.Length*delta.Length+dy*dy);
        double dot=(delta.X*Math.Sin(DroneHeading)*Math.Cos(DronePitch)+delta.Z*Math.Cos(DroneHeading)*Math.Cos(DronePitch)-dy*Math.Sin(DronePitch))/Math.Max(1,distance);
        bool view=distance<45&&dot>.90&&DroneHeight>CityTerrain.Ground(target.X,target.Z)+target.Height+1&&DroneSight(new(target.X,target.Z),CityTerrain.Ground(target.X,target.Z)+target.Height+.7);
        DroneScanProgress=view&&DroneScanHeld?DroneScanProgress+dt:Math.Max(0,DroneScanProgress-dt);
        if(DroneScanProgress>=2){
            DroneScans++;DroneScanProgress=0;Sound("pickup",PlayerPosition,.5);
            if(DroneScans==DroneTargets.Length){Money+=200;EndDrone("complete");}
        }
    }
}
