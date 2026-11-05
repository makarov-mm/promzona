using MakarovPhysics.RigidBodies2D;
namespace Promzona.Game;

public sealed partial class CityTraffic
{
    public const int ScooterTarget=192, ScooterCap=256, CourierTarget=140;
    public List<CityScooter> Scooters {get;}=[];
    private readonly Dictionary<int,CityPedestrian> _scooterPeople=[];
    private double _scooterMaintenance;
    public bool ScooterBrake;
    public bool OnScooter=>OnFoot&&Player.ScooterId!=0;
    public int CompletedDeliveries {get;private set;}
    internal CityPedestrian? ScooterRider(CityScooter s)=>_scooterPeople.GetValueOrDefault(s.RiderId);

    private void RegisterScooter(CityScooter s){
        int i=World.AddBox(s.Position.X,s.Position.Z,CityScooter.HalfWidth,CityScooter.HalfLength,
            CityScooter.Mass/(4*CityScooter.HalfWidth*CityScooter.HalfLength),.5,.12,0,-s.Heading);
        s.Body=World.Bodies[i];s.Physical=s.RiderId==0&&Detailed(s.Position,false);
        if(!s.Physical)Remove(s.Body);
    }
    private bool ScooterEligible(CityPedestrian p)=>!p.IsPlayer&&!p.Dead&&!p.Detached&&p.KnockedFor<=0
        &&p.WaitingTransitRoute==0&&p.Boarding<0&&p.Persona<3&&p.ServiceId==0&&p.GroupId<0&&!p.Hailing&&p.Injured<=0&&p.Id!=ContractActorId&&p.RoadLevel==0;
    private void ResetScooters(){
        Scooters.Clear();_scooterPeople.Clear();_scooterMaintenance=0;CompletedDeliveries=0;
        PopulateScooters(true);
    }
    private void PopulateScooters(bool initial=false){
        int couriers=Pedestrians.Count(p=>p.Courier&&!p.Dead),riders=Scooters.Count;
        // Assign existing residents, distributed by stable identity, never duplicate
        // population. Replacements are offscreen and use valid pavement positions.
        foreach(var p in Pedestrians){
            if(!ScooterEligible(p)||!initial&&(p.Position-PlayerPosition).Length<SpawnDistance)continue;
            if(!p.Courier&&couriers<CourierTarget&&p.Id%29==0){p.Courier=true;p.WeaponReleased=true;couriers++;}
            if(riders>=ScooterTarget||p.OwnedScooterId!=0||p.ScooterId!=0||p.Id%11!=0)continue;
            if(_useScenery&&(!ClearWalk(p.Position)||CityWater.Depth(p.Position)>.15))continue;
            var s=new CityScooter{Id=++_nextId,OwnerId=p.Id,RiderId=p.Id,Position=p.Position,Heading=p.Heading};
            RegisterScooter(s);Scooters.Add(s);p.ScooterId=p.OwnedScooterId=s.Id;p.Cruise=3.4;
            if(riders%3==0&&!p.Courier&&couriers<CourierTarget){p.Courier=true;p.WeaponReleased=true;couriers++;}
            _scooterPeople[p.Id]=p;riders++;
        }
    }
    private void ScootersBefore(double dt){
        if((_scooterMaintenance-=dt)<=0){
            _scooterMaintenance=2;_scooterPeople.Clear();
            foreach(var p in Pedestrians)if(p.ScooterId!=0||p.OwnedScooterId!=0)_scooterPeople[p.Id]=p;
            if(Player.OwnedScooterId!=0)_scooterPeople[Player.Id]=Player;
            for(int i=Scooters.Count-1;i>=0;i--){var s=Scooters[i];
                if(s.RiderId!=0&&(!_scooterPeople.TryGetValue(s.RiderId,out var rider)||rider.Dead)){
                    if(rider is not null)DismountScooter(rider,true);else{s.RiderId=0;s.Fallen=1;s.ReleasedAt=Time;}
                }
                if(s.OwnerId!=0&&(!_scooterPeople.TryGetValue(s.OwnerId,out var owner)||owner.Dead)){
                    if(owner is not null)owner.OwnedScooterId=0;
                    s.OwnerId=0;
                }
                if(s.RiderId==0&&(Time-s.ReleasedAt>180||CityWater.Depth(s.Position)>.8)&&
                    (s.Position-PlayerPosition).Length>SpawnDistance){
                    if(_scooterPeople.TryGetValue(s.OwnerId,out var person)){person.OwnedScooterId=0;person.Cruise=1.5;}
                    Remove(s.Body);Scooters.RemoveAt(i);
                }
            }
            PopulateScooters();
        }
        foreach(var s in Scooters){
            bool physical=s.RiderId==0&&Detailed(s.Position,s.Physical);
            if(physical!=s.Physical){s.Physical=physical;Attach(s.Body,physical);}
            if(s.RiderId!=0)continue;
            s.Body.Vx*=Math.Exp(-dt*2.4);s.Body.Vy*=Math.Exp(-dt*2.4);s.Body.W*=Math.Exp(-dt*3);
            s.Fallen=Math.Min(1,s.Fallen+dt*3);
            if(s.Height>0||s.VerticalSpeed>0){s.VerticalSpeed-=9.81*dt;s.Height=Math.Max(0,s.Height+s.VerticalSpeed*dt);if(s.Height==0)s.VerticalSpeed=0;}
            // Far abandoned objects settle without contacts, instead of retaining
            // a moving rigid body on the opposite side of the city.
            if(!s.Physical){s.Body.Vx=s.Body.Vy=s.Body.W=0;s.Height=s.VerticalSpeed=0;}
        }
    }
    private void ScootersAfter(double dt){
        foreach(var s in Scooters){
            if(s.RiderId!=0&&_scooterPeople.TryGetValue(s.RiderId,out var p)){
                if(p.Dead||p.KnockedFor>0||p.Detached){DismountScooter(p,true);continue;}
                s.Position=p.Position;s.Heading=p.Heading;s.RoadLevel=p.RoadLevel;s.Fallen=s.Height=0;
                s.Body.X=p.Position.X;s.Body.Y=p.Position.Z;s.Body.Angle=-p.Heading;
                s.Body.Vx=p.Body.Vx;s.Body.Vy=p.Body.Vy;
            }else if(s.RiderId==0){s.Position=new(s.Body.X,s.Body.Y);s.Heading=-s.Body.Angle;}
        }
    }
    public void DismountScooter(CityPedestrian p,bool fall=false){
        if(p.ScooterId==0)return;
        var s=Scooters.Find(s=>s.Id==p.ScooterId);p.ScooterId=0;p.Cruise=1.5;
        if(s is null)return;
        s.Position=p.Position+new CityPoint(Math.Cos(p.Heading),-Math.Sin(p.Heading))*.65;
        if(_useScenery&&!ClearWalk(s.Position))s.Position=p.Position;
        s.RoadLevel=p.RoadLevel;s.Body.X=s.Position.X;s.Body.Y=s.Position.Z;s.Body.Angle=-p.Heading;
        s.Body.Vx=p.Body.Vx*.8;s.Body.Vy=p.Body.Vy*.8;s.Body.W=fall?2:0;
        s.Heading=p.Heading;s.RiderId=0;s.ReleasedAt=Time;s.ReturnTime=Time+(fall?7:3);
        s.Fallen=fall?.2:1;s.VerticalSpeed=fall?1.3:0;
        s.Physical=Detailed(s.Position,false);Attach(s.Body,s.Physical);
        p.Aiming=false;
    }
    public CityScooter? NearbyScooter()=>!OnFoot||PlayerUnavailable?null:Scooters
        .Where(s=>s.RiderId==0&&s.Height<.2&&(s.Position-Player.Position).Length<1.8&&
            Math.Abs(CityRoadLevels.Surface(s.Position.X,s.Position.Z,s.RoadLevel)-PlayerGround)<.7)
        .MinBy(s=>(s.Position-Player.Position).Length);
    private void AbandonScooter(CityPedestrian p){
        DismountScooter(p,true);
        if(Scooters.Find(s=>s.Id==p.OwnedScooterId) is {} own&&own.OwnerId==p.Id)own.OwnerId=0;
        p.OwnedScooterId=0;_scooterPeople.Remove(p.Id);
    }
    public bool MountScooter(CityPedestrian p,CityScooter s){
        if(!Scooters.Contains(s)||s.RiderId!=0||p.Dead||p.Detached||p.KnockedFor>0||p.ScooterId!=0||(s.Position-p.Position).Length>1.8||s.Height>.2||
            Math.Abs(CityRoadLevels.Surface(s.Position.X,s.Position.Z,s.RoadLevel)-CityRoadLevels.Ground(p))>.7||_useScenery&&CityWater.Depth(s.Position)>.3)return false;
        if(p.OwnedScooterId!=0&&p.OwnedScooterId!=s.Id&&Scooters.Find(x=>x.Id==p.OwnedScooterId) is {} old)old.OwnerId=0;
        if(s.OwnerId!=p.Id){
            if(_scooterPeople.TryGetValue(s.OwnerId,out var previous)){previous.OwnedScooterId=0;previous.Cruise=1.5;Reanchor(previous);}
            s.OwnerId=p.Id;
        }
        p.WaitingTransitRoute=0;p.ScooterId=p.OwnedScooterId=s.Id;p.Cruise=3.4;p.PlayerWeapon=CityWeaponId.None;p.Aiming=false;
        s.RiderId=p.Id;s.Fallen=0;s.Physical=false;Remove(s.Body);_scooterPeople[p.Id]=p;return true;
    }
    private bool ScooterWalk(CityPedestrian p,double dt){
        if(p.Courier&&p.Fear<.12){
            if(p.DeliveryWait>0){p.DeliveryWait=Math.Max(0,p.DeliveryWait-dt);p.Body.Vx=p.Body.Vy=0;return true;}
            if(p.Distance-p.DeliveryDistance>115+p.Id%90&&!p.Crossing&&p.Wait>0){
                p.Deliveries++;CompletedDeliveries++;p.DeliveryDistance=p.Distance;p.DeliveryWait=2+p.Id%4;return true;
            }
        }
        if(p.ScooterId!=0){p.Cruise=p.Crossing?2.2:3.4;return false;}
        if(p.OwnedScooterId==0)return false;
        var s=Scooters.Find(s=>s.Id==p.OwnedScooterId);
        if(s is null||s.RiderId!=0||Time-s.ReleasedAt>70||CityWater.Depth(s.Position)>.5){if(s?.OwnerId==p.Id)s.OwnerId=0;p.OwnedScooterId=0;p.Cruise=1.5;return false;}
        if(Time<s.ReturnTime||p.Fear>.35)return false;
        var delta=s.Position-p.Position;
        if(delta.Length<1.15&&s.Height<.1&&s.Body.Vx*s.Body.Vx+s.Body.Vy*s.Body.Vy<.2){MountScooter(p,s);Reanchor(p);return false;}
        if(delta.Length>45){s.OwnerId=0;p.OwnedScooterId=0;return false;}
        var desired=delta*(1.6/Math.Max(1,delta.Length));
        if(p.Physical)MovePerson(p,desired,dt,s.Position);else MoveFarSocial(p,desired,dt);
        return true;
    }
    private void RidePlayerScooter(double dt){
        var p=Player;var b=p.Body;p.Aiming=false;p.PlayerWeapon=CityWeaponId.None;PlayerFireHeld=false;
        var direction=WalkInput;double length=direction.Length;
        var desired=length>.01?direction*(6.8/Math.Max(1,length)):default;
        if(ScooterBrake)desired=default;
        if(_useScenery&&!CityRoadLevels.ClearAt(p.Position+desired*.20,p.RoadLevel)&&!ClearWalk(p.Position+desired*.20)){
            if(p.Speed>4){DismountScooter(p,true);p.KnockedFor=1.5;p.VerticalSpeed=1.2;return;}
            desired=default;
        }
        double mix=1-Math.Exp(-dt*(ScooterBrake?10:length>.01?3.2:1.2));
        b.Wake();b.Vx+=(desired.X-b.Vx)*mix;b.Vy+=(desired.Z-b.Vy)*mix;
    }

    private static void ValidateScooterSave(CityWorldSave save,CityPedestrian[] people,CityCar[] cars,int gridSize){
        var fleet=save.Scooters.Select(a=>{var s=new CityScooter();CityState.Restore(s,a.State);return s;}).ToArray();
        var all=people.ToList();if(save.Player is {} ps){var player=new CityPedestrian();CityState.Restore(player,ps.State);all.Add(player);}
        // CityCar.Id is an auto-property, so its captured name is the backing field,
        // not "Id": the former CityState.Int lookup by name found nothing and the
        // car-id collision check was a no-op. The restored cars carry the real ids.
        var otherIds=all.Select(p=>p.Id).Concat(cars.Select(c=>c.Id)).ToHashSet();
        int lastLevel=CityLayout.RoadLevelCount(gridSize,save.WideRoads,save.RoadRevision);
        if(fleet.Select(s=>s.Id).Distinct().Count()!=fleet.Length||fleet.Any(s=>s.Id<=0||s.Id==int.MaxValue||s.OwnerId<0||s.RiderId<0||s.Height<0||s.Height>100||s.Fallen<0||s.Fallen>1||s.RoadLevel<0||s.RoadLevel>lastLevel)||
            fleet.Any(s=>otherIds.Contains(s.Id)||s.RiderId!=0&&s.RiderId!=s.OwnerId||s.OwnerId!=0&&!all.Any(p=>p.Id==s.OwnerId&&p.OwnedScooterId==s.Id))||
            all.Any(p=>p.OwnedScooterId!=0&&!fleet.Any(s=>s.Id==p.OwnedScooterId&&s.OwnerId==p.Id))||
            save.Scooters.Any(a=>a.Body is null)||fleet.Any(s=>s.RiderId!=0&&!all.Any(p=>p.Id==s.RiderId&&p.ScooterId==s.Id&&!p.Dead))||
            all.Any(p=>p.ScooterId!=0&&!fleet.Any(s=>s.Id==p.ScooterId&&s.RiderId==p.Id))||
            fleet.Where(s=>s.RiderId!=0).Select(s=>s.RiderId).Distinct().Count()!=fleet.Count(s=>s.RiderId!=0))
            throw new InvalidDataException("Invalid scooter ownership or rider state");
    }
    private void RestoreScooters(CityWorldSave save){
        Scooters.Clear();_scooterPeople.Clear();_scooterMaintenance=0;
        foreach(var p in Pedestrians)if(p.ScooterId!=0||p.OwnedScooterId!=0)_scooterPeople[p.Id]=p;
        if(Player.OwnedScooterId!=0)_scooterPeople[Player.Id]=Player;
        foreach(var a in save.Scooters){var s=new CityScooter();CityState.Restore(s,a.State);RegisterScooter(s);CityState.RestoreBody(s.Body,a.Body);Scooters.Add(s);_nextId=Math.Max(_nextId,s.Id);}
        if(save.ScooterRevision==0)PopulateScooters(false);
    }
}
