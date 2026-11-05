namespace Promzona.Game;

public sealed partial class CityTraffic
{
    private double _fireClock;
    public void IgnitePerson(CityPedestrian p,bool directPlayerFlame=false,bool playerCaused=false)
    {
        if(p.Dead||p.IsPlayer&&(directPlayerFlame||GodMode))return;
        // The blame follows the current burn: a fresh fire starts from its own
        // cause, a re-ignition while already burning keeps the player's share.
        // The flag used to be sticky, so an NPC the player had set alight once
        // charged the player with a death caused by an NPC car fire much later.
        bool burning=p.BurnUntil>Time;
        p.BurnUntil=Math.Max(p.BurnUntil,Time+(p.IsPlayer?6:9));
        p.BurnPlayerCaused=burning?p.BurnPlayerCaused|playerCaused:playerCaused;p.Fear=1;p.AggressionUntil=0;p.Aiming=false;
    }
    private void IgniteCar(CityCar c){if(c.Destroyed||c.Falling||GodMode&&c==Hero)return;c.PlayerDamageUntil=Time+45;DamageCar(c,.015,true);if(!c.Burning){c.Burning=true;c.FireAge=0;c.PanicDriver=!c.Hero;}}
    private void Flame(CityPoint direction)
    {
        var weapon=CityWeapons.Find(CityWeaponId.Flamethrower)!;
        double baseY=_useScenery?PlayerGround:0,y=baseY+(OnFoot?1.32:1.1);
        double heading=Math.Atan2(direction.X,direction.Z);
        var heatedCars=new HashSet<CityCar>();
        for(int ray=-2;ray<=2;ray++){
            double h=heading+ray*.075;var d=new CityPoint(Math.Sin(h),Math.Cos(h));
            var hit=TraceAttack(PlayerPosition,d,weapon,true,0,Player,true,y,0,OnFoot?-1:Hero.Id);
            double distance=(hit.Point-PlayerPosition).Length;
            // Short moving tongues, rather than a solid sheet of stationary fire sprites.
            for(int n=0;n<6&&Particles.Count<900;n++){
                // Random emission phase avoids visible rows at the weapon's fixed fire cadence.
                double start=.9+Noise()*2.1; if(distance<=start)continue;
                var at=PlayerPosition+d*start+new CityPoint(d.Z,-d.X)*((Noise()-.5)*.12);
                Particles.Add(new CityParticle{RoadLevel=OnFoot?Player.RoadLevel:Hero.RoadLevel,
                    X=at.X,Y=y+(Noise()-.5)*.12,Z=at.Z,Vx=d.X*18,Vz=d.Z*18,Vy=.3+Noise()*.5,
                    Kind=6,Life=Math.Min(.38,(distance-start)/18),Size=.065+Noise()*.055});
            }
            foreach(var c in NearbyCars(PlayerPosition,13)){
                if(!OnFoot&&c==Hero||c.Destroyed||Math.Abs(c.SurfaceHeight+c.Lift+c.Height*.5-y)>c.Height*.5+1)continue;
                double at=RayBox(PlayerPosition,d,c.Position,c.Heading,c.HalfWidth,c.HalfLength,weapon.Range);
                if(at<=distance+.05&&at<weapon.Range&&heatedCars.Add(c))IgniteCar(c);
            }
            foreach(var p in NearbyPeople(PlayerPosition,10)){
                if(p.IsPlayer||p.Dead||Math.Abs(CityRoadLevels.Ground(p)+p.Height*.65-y)>1.3)continue;
                var delta=p.Position-PlayerPosition;double along=CityPoint.Dot(delta,d);
                if(along>.3&&along<=distance+.4&&(delta-d*along).Length<.38)IgnitePerson(p,true,true);
            }
        }
        if(Time>=_nextFlameSound){Sound("fire",PlayerPosition,.35);_nextFlameSound=Time+.65;}
    }
    private double _nextFlameSound;
    private void FireStep(double dt)
    {
        _fireClock+=dt;if(_fireClock<.1)return;double elapsed=Math.Min(_fireClock,.2);_fireClock=0;
        // Only local burning vehicles spread fire. Distant burn timers still expire normally.
        foreach(var c in NearbyCars(PlayerPosition,100))if(c.Burning&&!c.Destroyed){
            double radius=Math.Max(c.HalfWidth,c.HalfLength)+1.1;
            foreach(var p in NearbyPeople(c.Position,radius))if(!p.Dead&&(p.Position-c.Position).Length<radius&&SameHeight(c,p)&&ClearLevelSight(c.Position,c.RoadLevel,p.Position,p.RoadLevel))IgnitePerson(p,false,Time<c.PlayerDamageUntil);
            // An enclosed car uses vehicle damage; walking into its fire burns the person.
        }
        // The list already owns these actors; no permanent parallel actor cache.
        foreach(var p in Pedestrians){
            if(p.Dead||p.BurnUntil<=Time)continue;
            BurnPerson(p,elapsed);
        }
        if(!OnFoot&&!Player.Dead&&Player.BurnUntil>Time)BurnPerson(Player,elapsed);
    }
    private void BurnPerson(CityPedestrian p,double dt)
    {
        if(p.IsPlayer&&GodMode){p.BurnUntil=0;return;}
        var position=p.IsPlayer&&!OnFoot?PlayerPosition:p.Position;
        double lift=p.IsPlayer&&!OnFoot?Hero.Lift:p.FlightHeight;
        int level=p.IsPlayer&&!OnFoot?Hero.RoadLevel:p.RoadLevel;
        if(CityWater.Depth(position)>.35&&lift<.2){p.BurnUntil=0;p.BurnPlayerCaused=false;return;}
        p.GunDamage=Math.Min(1,p.GunDamage+dt*(p.IsPlayer?.065:.18));
        if((position-PlayerPosition).Length<110){Emit(position.X,.65+lift,position.Z,4,2,.6,level);Emit(position.X,1.3+lift,position.Z,1,1,.4,level);}
        if(p.GunDamage>=1){p.BurnUntil=0;FatalPerson(p,p.BurnPlayerCaused);}
    }
}
