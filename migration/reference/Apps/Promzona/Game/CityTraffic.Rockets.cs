namespace Promzona.Game;

/// <summary>Bounded arcade projectile; coordinates are world coordinates, including height.</summary>
public sealed class CityRocket
{
    public CityPoint Position, Direction;
    public double Height, Slope, Travel;
    public int OwnerCar=-1;
}

public sealed partial class CityTraffic
{
    public List<CityRocket> Rockets {get;}=[];
    public int PunchesThrown {get;private set;}
    private double _punchUntil;
    private bool _punchPending;
    public double PunchPhase=>OnFoot&&!Player.Dead&&Player.KnockedFor<=0&&Time<_punchUntil
        ? Math.Clamp(1-(_punchUntil-Time)/.55,.0001,1):0;
    private static readonly CityWeapon Fist=new(CityWeaponId.None,0,"FIST","КУЛАК",0,.6,1.55,0,.26,0,0,0,0,"punch");

    private void Punch(){
        _playerShotAt=Time+.6;_punchUntil=Time+.55;_punchPending=true;PunchesThrown++;
        Player.Heading=PlayerViewHeading;Player.Aiming=false;Player.PlayerWeapon=CityWeaponId.None;
        Sound("punch",PlayerPosition,.38);
    }
    private void PunchContact(){
        if(!_punchPending)return;
        if(PunchPhase==0||EnteringCarId>=0||Inventory.Active is not null){_punchPending=false;return;}
        if(PunchPhase<.3)return;_punchPending=false;
        var direction=new CityPoint(Math.Sin(Player.Heading),Math.Cos(Player.Heading));
        // A small forward cone makes a fist less demanding than a bullet reticle.
        // The final short ray still stops at walls, cars and the first bystander.
        var target=NearbyPeople(PlayerPosition,1.8).Where(p=>!p.IsPlayer&&!p.Dead&&p.KnockedFor<=0)
            .Where(p=>{var d=p.Position-PlayerPosition;return d.Length>0&&CityPoint.Dot(d,direction)/d.Length>.82;})
            .MinBy(p=>(p.Position-PlayerPosition).Length);
        if(target is not null){var d=target.Position-PlayerPosition;direction=d*(1/Math.Max(.01,d.Length));}
        TraceAttack(PlayerPosition,direction,Fist,true,1.2,Player,melee:true);
    }
    private void LaunchRocket(CityPoint direction,CityWeapon weapon){
        if(Rockets.Count>=16)Rockets.RemoveAt(0);
        double height=(_useScenery?PlayerGround:0)+(OnFoot?1.42:Hero.Kind==11?1.95:1.1);
        double slope=Math.Tan(OnFoot?PlayerViewPitch:Hero.TerrainPitch+Hero.Pitch);
        double muzzle=OnFoot?.8:Hero.HalfLength+.3;
        var hit=TraceAttack(PlayerPosition,direction,weapon with{Range=muzzle},true,0,Player,true,height,slope,OnFoot?-1:Hero.Id);
        Sound("rocket",PlayerPosition,.75);
        if((hit.Point-PlayerPosition).Length<muzzle-.001){RocketBlast(hit.Point-direction*.18,hit.Height);return;}
        Rockets.Add(new(){Position=hit.Point,Height=hit.Height,Direction=direction,Slope=slope,OwnerCar=OnFoot?-1:Hero.Id});
    }
    private void RocketStep(double dt){
        PunchContact();
        var weapon=CityWeapons.Find(CityWeaponId.Bazooka)!;
        for(int i=Rockets.Count-1;i>=0;i--){
            var rocket=Rockets[i];double remaining=Math.Min(Math.Max(0,dt),.25)*44/Math.Sqrt(1+rocket.Slope*rocket.Slope);
            bool hit=false;
            while(remaining>1e-8){
                double distance=Math.Min(.5,remaining);
                var trace=TraceAttack(rocket.Position,rocket.Direction,weapon with{Range=distance+.001},true,0,null,true,
                    rocket.Height,rocket.Slope,rocket.Travel<5?rocket.OwnerCar:-1);
                if((trace.Point-rocket.Position).Length<distance){
                    rocket.Position=trace.Point-rocket.Direction*.18;rocket.Height=trace.Height;hit=true;break;
                }
                rocket.Position+=rocket.Direction*distance;rocket.Height+=rocket.Slope*distance;
                rocket.Travel+=distance;remaining-=distance;
                if(rocket.Travel>=weapon.Range)break;
            }
            if(hit){Rockets.RemoveAt(i);RocketBlast(rocket.Position,rocket.Height);}
            else if(rocket.Travel>=weapon.Range)Rockets.RemoveAt(i);
            else if(Noise()<dt*40)Emit(rocket.Position.X,rocket.Height-(_useScenery?CityTerrain.Ground(rocket.Position):0),rocket.Position.Z,1,1,.25);
        }
    }
    private bool BlastVisible(CityPoint from,double y,CityPoint to,double targetY){
        if(!_useScenery)return true;
        double distance=(to-from).Length;int steps=Math.Max(1,(int)Math.Ceiling(distance/.4));
        for(int k=1;k<steps;k++){
            double f=k/(double)steps;var p=from+(to-from)*f;double h=y+(targetY-y)*f;
            if(CityRoadLevels.Solid(p.X,h,p.Z,.02))return false;
            int i=(int)Math.Floor((p.X-Coordinate(0))/Spacing),j=(int)Math.Floor((p.Z-Coordinate(0))/Spacing);
            for(int a=i-1;a<=i+1;a++)for(int b=j-1;b<=j+1;b++)foreach(int id in CityLayout.Current.BuildingIds(a,b)){
                var block=CityLayout.Current.Blocks[id];
                if(Math.Abs(p.X-block.X)<block.HalfX&&Math.Abs(p.Z-block.Z)<block.HalfZ&&h<CityTerrain.Ground(new(block.X,block.Z))+block.Height)return false;
            }
        }return true;
    }
    private void RocketBlast(CityPoint at,double y){
        Sound("explosion",at,.95);double ground=_useScenery?CityTerrain.Ground(at):0;
        Emit(at.X,y-ground,at.Z,4,45,6);Emit(at.X,y-ground,at.Z,1,26,3);Emit(at.X,y-ground,at.Z,2,24,7);
        ReportCrime(.45,80,sight:false);AddIncident(at,true,true);
        double Strength(CityPoint p,double h){double r=Math.Sqrt(Math.Pow((p-at).Length,2)+(h-y)*(h-y));return r<8&&BlastVisible(at,y,p,h)?1-r/8:0;}
        // Authoritative lists also cover vehicles which are just outside the local
        // rigid-body region. Height and walls prevent damage through bridge decks.
        foreach(var c in Cars){
            if(c.Falling)continue;
            double s=Strength(c.Position,(_useScenery?c.SurfaceHeight:0)+c.Lift+c.Height*.5);if(s<=0)continue;
            DamageCar(c,3.2*s,true);var d=c.Position-at;double length=Math.Max(.5,d.Length);
            c.Body.Wake();c.Body.Vx+=d.X/length*s*5000/c.Mass;c.Body.Vy+=d.Z/length*s*5000/c.Mass;c.VerticalVelocity+=s*2;
            c.WreckSupported=false;
            if(c.HealthRemaining<=0)Explode(c);
        }
        foreach(var p in Pedestrians){
            if(p.Dead)continue;double h=(_useScenery?CityRoadLevels.Ground(p):0)+p.FlightHeight+1;
            double s=Strength(p.Position,h);if(s<=0)continue;
            p.Fear=1;p.FearFrom=at;p.VoiceRevision++;p.Aiming=false;
            if(p.IsPlayer){DamagePlayer(6*s);continue;}
            p.GunDamage+=s*2;p.AggressionUntil=Time+8;
            var d=p.Position-at;double length=Math.Max(.5,d.Length);
            p.Body.Vx=d.X/length*s*6;p.Body.Vy=d.Z/length*s*6;p.Body.Wake();
            p.KnockedFor=2;p.FlightHeight=.02;p.VerticalSpeed=s*2;Remove(p.Body);p.Detached=true;
            if(p.GunDamage>=1)FatalPerson(p,true);
        }
        foreach(var a in Animals){
            if(a.Dead)continue;double s=Strength(a.Position,(_useScenery?CityTerrain.Ground(a.Position):0)+a.Height+.3);
            if(s<=0)continue;a.Fear=1;a.FearFrom=at;
            if(s<.35)continue;a.Dead=true;a.DeathTime=Time;a.Flying=false;a.Detached=true;a.KnockedFor=3;a.VerticalSpeed=2;
            if(a.Body is {} body){Remove(body);var d=a.Position-at;body.Vx=d.X/Math.Max(.5,d.Length)*3;body.Vy=d.Z/Math.Max(.5,d.Length)*3;}
        }
        foreach(var prop in NearbyProps(at,8))if(prop.Body is {} body){
            double s=Strength(prop.Position,(_useScenery?CityTerrain.Ground(prop.Position):0)+prop.Height+.4);if(s<=0)continue;
            var d=prop.Position-at;double v=Math.Min(12,180/Math.Max(1,prop.Mass))*s;body.Wake();body.Vx+=d.X/Math.Max(.5,d.Length)*v;body.Vy+=d.Z/Math.Max(.5,d.Length)*v;
        }
        if(_useScenery&&CityWater.Wet(at)&&Math.Abs(y-CityWater.Level)<.7)Splash(at,2);
    }
}
