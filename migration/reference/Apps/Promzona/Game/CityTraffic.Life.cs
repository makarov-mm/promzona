namespace Promzona.Game;

public enum AnimalKind { Cat,Dog,Pigeon,Bird }

public sealed class CashDrop
{
    public int RoadLevel;public CityPoint Position;public int Amount;public double Created;
}
public sealed partial class CityTraffic
{
    public List<CityAnimal> Animals {get;}=[];
    public List<CashDrop> Cash {get;}=[];
    public int Money {get;private set;}
    public int AnimalsDeparted {get;private set;}
    public int PeopleRemoved {get;private set;}
    private double _lifeSpawn;
    public void SetMoney(int amount)=>Money=Math.Clamp(amount,0,999999999);
    private void ResetLife()
    {
        AnimalPopulationRevision=2;
        Animals.Clear();Cash.Clear();Money=AnimalsDeparted=PeopleRemoved=0;_lifeSpawn=0;
        for(int k=0;Animals.Count<210&&k<350;k++)SpawnAnimal();
        int originalBirds=Animals.Count(a=>a.Kind==AnimalKind.Pigeon), originalOthers=Animals.Count-originalBirds;
        OriginalPigeons=originalBirds*2;InitialPigeonTarget=OriginalPigeons*3;
        OtherAnimalTarget=2*(int)Math.Ceiling(originalOthers*(240d/210));
        for(int k=0;Animals.Count(a=>a.Kind!=AnimalKind.Pigeon)<originalOthers*2&&k<2000;k++){
            _cancel.ThrowIfCancellationRequested();
            SpawnAnimal(Pick(3) switch{0=>AnimalKind.Cat,1=>AnimalKind.Dog,_=>AnimalKind.Bird});
        }
        for(int k=0;Animals.Count(a=>a.Kind==AnimalKind.Pigeon)<InitialPigeonTarget&&k<2000;k++){
            _cancel.ThrowIfCancellationRequested();int missing=InitialPigeonTarget-Animals.Count(a=>a.Kind==AnimalKind.Pigeon);
            // Avoid leaving a final solitary pigeon: split the last flock if needed.
            int size=Math.Min(8,missing);if(missing-size is >0 and <4)size-=4-(missing-size);
            SpawnAnimal(AnimalKind.Pigeon,Math.Max(4,size));
        }
        PigeonTarget=2*(int)Math.Ceiling((InitialPigeonTarget/2)*(240d/210));
    }
    private void SpawnAnimal(AnimalKind? force=null,int flockSize=0)
    {
        int i=Pick(Nodes-1),j=Pick(Nodes-1);
        var kind=force??(AnimalKind)Pick(4);
        if(kind==AnimalKind.Pigeon&&Random()<.78){
            for(int k=0;k<30&&!CityModel.ParkBlock(i,j);k++){i=Pick(Nodes-1);j=Pick(Nodes-1);}
        }
        bool park=CityModel.ParkBlock(i,j);
        double x=Coordinate(i)+(park?10+Random()*16:Sidewalk),z=Coordinate(j)+10+Random()*16;
        if(Time>0&&(new CityPoint(x,z)-PlayerPosition).Length<SpawnDistance)return;
        if(Cars.Any(c=>CarPersonOverlap(c.Position,c.Heading,new(x,z),.5,c.HalfLength,c.HalfWidth)))return;
        int count=kind==AnimalKind.Pigeon?(flockSize>0?flockSize:4+Pick(5)):1,flock=kind==AnimalKind.Pigeon?_nextId+1:-1;
        // Atomic flock spawn: no solitary bird left by a clipped facade.
        // Pavement flocks spread along the pavement, park flocks in two dimensions.
        if(Animals.Count+count>AnimalCap)return;
        var positions=new List<CityPoint>();
        for(int member=0;member<count;member++){
            double angle=member*2.399963,spread=member==0?0:.65*Math.Sqrt(member);
            var candidate=park?new CityPoint(x+Math.Sin(angle)*spread,z+Math.Cos(angle)*spread):new CityPoint(x+(member%2)*.40,z+(member/2)*.48);
            if(!ClearWalk(candidate)||Cars.Any(c=>CarPersonOverlap(c.Position,c.Heading,candidate,.35,c.HalfLength,c.HalfWidth)))return;
            positions.Add(candidate);
        }
        foreach(var atPoint in positions){
            var a=new CityAnimal{Id=++_nextId,Kind=kind,Flock=flock,FlockHome=new(x,z),FlockOffset=atPoint-new CityPoint(x,z),Variant=Pick(8),Position=atPoint,Target=atPoint,Wait=Random()*4,FlyAt=Time+20+Random()*50,VoiceAt=Time+5+Random()*20};
            int at=World.AddCircle(atPoint.X,atPoint.Z,a.Radius,a.Mass/(Math.PI*a.Radius*a.Radius),.13,.04,0);
            a.Body=World.Bodies[at];a.Body.InvI=0;a.Physical=Detailed(a.Position,false);if(!a.Physical)Remove(a.Body);Animals.Add(a);
        }
    }
    private void ReleasePerson(CityPedestrian p)
    {
        if(p.OriginKey>=0&&_pedPlaces[p.OriginKey]==p.Id)_pedPlaces[p.OriginKey]=-1;
        if(p.DestinationKey>=0&&_pedPlaces[p.DestinationKey]==p.Id)_pedPlaces[p.DestinationKey]=-1;
        p.OriginKey=p.DestinationKey=-1;p.Crossing=false;p.Path=null;
    }
    // Arcade consequences, deliberately not an injury/medical prediction.
    public void HitPerson(CityPedestrian p,CityCar car,double closingSpeed)
    {
        if(p.Dead||p.KnockedFor>0||closingSpeed<1.1)return;
        DismountScooter(p,true);
        p.KnockedFor=2.2+Math.Min(4,closingSpeed*.12);
        p.VerticalSpeed=closingSpeed>10?Math.Min(6,closingSpeed*.28):Math.Min(2,closingSpeed*.18);
        p.Body.Vx=car.Body.Vx*.55;p.Body.Vy=car.Body.Vy*.55;Remove(p.Body);p.Detached=true;
        p.VoiceRevision++;p.Aiming=false;Sound("impact",p.Position,.6);
        PedestrianImpacts++;if(PlayerControls(car)&&closingSpeed<5.5)ReportCrime(.18);
        if(closingSpeed>=5.5){
            FatalPerson(p,PlayerControls(car));
        }
        // A survivor limps about where it fell and can be driven to a clinic (jobs).
        else if(!p.IsPlayer&&!p.Officer&&p.ServiceId==0){p.Injured=Math.Max(p.Injured,40+closingSpeed*10);p.RoleHome=p.Position;p.Hailing=false;}
    }
    private void FatalPerson(CityPedestrian p,bool playerCaused){
        if(p.Dead||p.IsPlayer&&GodMode)return;
        AbandonScooter(p);
        if(p.IsPlayer){p.Dead=true;p.GunDamage=1;p.DeathTime=Time;p.Detached=true;p.Aiming=false;p.VoiceRevision++;Remove(p.Body);CancelEntry();return;}
        DropWeapon(p);p.Dead=true;p.DeathTime=Time;p.Detached=true;p.Aiming=false;p.VoiceRevision++;Remove(p.Body);ReleasePerson(p);if(!p.Drowned)AddIncident(p.Position,true,playerCaused);
        ContractDeath(p,playerCaused);
        if(playerCaused)ReportCrime(.85);
        if(Random()<.58&&Cash.Count<256)Cash.Add(new(){Position=p.Position,RoadLevel=p.RoadLevel,Amount=5+Pick(20)*5,Created=Time});
    }
    private void Blood(CityPoint position,double size=.65,int roadLevel=0)
    {
        Marks.Add(new(){Point=position,RoadLevel=roadLevel,Blood=true,Size=size,Life=240});
        if(Marks.Count>1000)Marks.RemoveRange(0,Marks.Count-1000);
    }
    private void LifeBeforeContacts(double dt)
    {
        RebuildAnimalThreatGrid();
        foreach(var a in Animals){
            if(!a.Physical){FarAnimal(a,dt);continue;}
            if(a.Flying){
                if(a.Kind==AnimalKind.Bird){FlyBird(a,dt);continue;}
                a.FlightTime+=dt;a.Height+=dt*(2.7+Math.Min(4,a.FlightTime));
                a.Position+=new CityPoint(Math.Sin(a.Heading),Math.Cos(a.Heading))*(dt*(4+a.FlightTime));
                if(a.Height>.8&&a.Body is {} flyingBody){Remove(flyingBody);a.Body=null;}
                else if(a.Body is {} low){low.X=a.Position.X;low.Y=a.Position.Z;}
                continue;
            }
            if(a.Body is not {} b)continue;
            a.KnockedFor=Math.Max(0,a.KnockedFor-dt);
            if(a.Height>0||a.VerticalSpeed>0){a.VerticalSpeed-=9.81*dt;a.Height=Math.Max(0,a.Height+a.VerticalSpeed*dt);if(a.Height==0)a.VerticalSpeed=0;}
            if(a.Dead||a.KnockedFor>0){double drag=Math.Exp(-dt*(a.Height>0?.25:4));b.Vx*=drag;b.Vy*=drag;if(a.Detached){b.X+=b.Vx*dt;b.Y+=b.Vy*dt;}continue;}
            if(a.Detached){if(NearbyCars(a.Position,8).Any(c=>CarPersonOverlap(c.Position,c.Heading,a.Position,a.Radius+.5,c.HalfLength,c.HalfWidth)))continue;if(a.Physical)World.Bodies.Add(b);a.Detached=false;}
            if(a.Kind==AnimalKind.Bird&&Time>=a.FlyAt){StartBirdFlight(a);continue;}
            a.Fear=Math.Max(0,a.Fear-dt*.13);AnimalReactions(a,dt);if(a.Flying)continue;
            foreach(var c in NearbyCars(a.Position,16))if(Threat(c,a.Position,out var source)){a.Fear=Math.Min(1,a.Fear+dt*3);a.FearFrom=source;}
            if(a.Fear>.08&&a.Kind is AnimalKind.Cat or AnimalKind.Dog){var escape=Escape(a.Position,a.FearFrom);a.Target=a.Position+escape*8;a.Wait=3;}

            bool alarm=false;
            foreach(var c in NearbyCars(a.Position,10))if((c.Position-a.Position).Length<5+Math.Abs(c.Speed)*.32){a.FearFrom=c.Position;a.Heading=Math.Atan2(a.Position.X-c.Position.X,a.Position.Z-c.Position.Z);alarm=true;break;}
            if(!alarm)foreach(var p in NearbyPeople(a.Position,4))if(!p.Dead&&(p.Position-a.Position).Length<2.8){a.FearFrom=p.Position;a.Heading=Math.Atan2(a.Position.X-p.Position.X,a.Position.Z-p.Position.Z);alarm=true;break;}
            if(a.Kind==AnimalKind.Pigeon&&alarm){LaunchFlock(a);continue;}
            if(a.Kind==AnimalKind.Pigeon){var walk=FlockVelocity(a);b.Wake();b.Vx+=(walk.X-b.Vx)*(1-Math.Exp(-dt*5));b.Vy+=(walk.Z-b.Vy)*(1-Math.Exp(-dt*5));continue;}
            if(a.Wait>0)a.Wait-=dt;
            if((a.Target-a.Position).Length<.7||a.Wait<=0){
                int i=(int)Math.Round((a.Position.X-Coordinate(0))/Spacing),j=(int)Math.Round((a.Position.Z-Coordinate(0))/Spacing);
                double angle=Random()*Math.Tau;
                // Sidewalk corners form the same connected pedestrian graph; parks
                // get shorter free walks inside the lawn.
                if(a.Kind==AnimalKind.Pigeon){a.Target=a.Position+new CityPoint(Math.Sin(angle),Math.Cos(angle))*2.5;}
                else{int d=Pick(4);var f=Forward(d);a.Target=new(Coordinate(i+(int)f.X)+Sidewalk,Coordinate(j+(int)f.Z)+Sidewalk);}
                a.Wait=12+Random()*20;
            }
            var delta=a.Target-a.Position;double speed=a.Kind==AnimalKind.Cat?.85:a.Kind==AnimalKind.Dog?1.35:.45;
            if(a.Fear>.08)speed*=1+3*a.Fear;
            if(a.Fear>.3&&Time>a.VoiceAt){Sound(a.Kind==AnimalKind.Cat?"cat":"dog",a.Position,.5);a.VoiceAt=Time+4+Random()*6;}
            if(delta.Length>.1){a.Heading+=Math.Clamp(Math.IEEERemainder(Math.Atan2(delta.X,delta.Z)-a.Heading,Math.Tau),-dt*4,dt*4);}
            double vx=Math.Sin(a.Heading)*speed,vy=Math.Cos(a.Heading)*speed;
            b.Wake();b.Vx+=(vx-b.Vx)*(1-Math.Exp(-dt*3));b.Vy+=(vy-b.Vy)*(1-Math.Exp(-dt*3));
        }
        foreach(var a in Animals){
            if(!a.Physical||a.Dead||a.KnockedFor>0||a.Height>.8||a.Body is not {} b)continue;
            foreach(var c in NearbyCars(a.Position,8)){
                var delta=a.Position-c.Position;double approach=((c.Body.Vx-b.Vx)*delta.X+(c.Body.Vy-b.Vy)*delta.Z)/Math.Max(.25,delta.Length);
                if(_useScenery&&Math.Abs(c.SurfaceHeight-CityTerrain.Ground(a.Position)-a.Height)>2||approach<1.1||!CarPersonOverlap(c.Position,c.Heading,a.Position,a.Radius,c.HalfLength,c.HalfWidth))continue;
                a.Flying=false;a.KnockedFor=3;a.VerticalSpeed=Math.Min(4,approach*.21);b.Vx=c.Body.Vx*.5;b.Vy=c.Body.Vy*.5;Remove(b);a.Detached=true;Sound("impact",a.Position,.3);
                if(approach>4){a.Dead=true;a.DeathTime=Time;}
                break;
            }
        }
    }
    private void LifeAfterContacts(double dt)
    {
        for(int n=Pedestrians.Count-1;n>=0;n--){
            var p=Pedestrians[n];
            if(p.Dead&&!p.Drowned){
                p.BloodClock+=dt;
                if(p.FlightHeight>.05&&p.BloodClock>.12){p.BloodClock=0;Blood(p.Position,.14,p.RoadLevel);}
                if(p.FlightHeight==0&&!p.BloodPool){p.BloodPool=true;Blood(p.Position,.65,p.RoadLevel);}
            }
            bool outside=Math.Abs(p.Position.X)>EdgeHigh-2||Math.Abs(p.Position.Z)>EdgeHigh-2;
            if(!p.IsPlayer&&(outside||(p.Dead&&Time-p.DeathTime>180&&!PatientClaimed(p.Id)&&(p.Position-PlayerPosition).Length>SpawnDistance))){
                AbandonScooter(p);ReleasePerson(p);Remove(p.Body);Pedestrians.RemoveAt(n);PeopleRemoved++;ExitedPedestrians++;
            }
        }
        for(int n=Animals.Count-1;n>=0;n--){
            var a=Animals[n];var old=a.Position;if(a.Body is {} b)a.Position=new(b.X,b.Y);a.Distance+=(a.Position-old).Length;
            if(a.Dead&&a.Height==0&&!a.BloodPool){a.BloodPool=true;Blood(a.Position,a.Kind is AnimalKind.Pigeon or AnimalKind.Bird?.18:.35);}
            bool outside=Math.Abs(a.Position.X)>EdgeHigh-2||Math.Abs(a.Position.Z)>EdgeHigh-2;
            if(outside||(a.Kind!=AnimalKind.Bird&&a.FlightTime>5)||(a.Dead&&Time-a.DeathTime>8&&(a.Position-PlayerPosition).Length>SpawnDistance)){
                if(a.Body is {} body)Remove(body);Animals.RemoveAt(n);AnimalsDeparted++;
            }
        }
        bool collected=false;
        for(int n=Cash.Count-1;n>=0;n--){
            var c=Cash[n];
            if((!_useScenery||Math.Abs(PlayerGround-CityRoadLevels.Surface(c.Position.X,c.Position.Z,c.RoadLevel))<1.5)&&!PlayerUnavailable&&Time-c.Created>=1&&(c.Position-PlayerPosition).Length<(OnFoot?1.1:Hero.HalfLength+1)){Money=Math.Min(999999999,Money+c.Amount);Cash.RemoveAt(n);collected=true;continue;}
            if(Time-c.Created>300||(Time-c.Created>12&&(c.Position-PlayerPosition).Length>170))Cash.RemoveAt(n);
        }
        if(collected)Sound("cash",PlayerPosition,.85,ui:true);
        // Remove distant blood with its scene, without retaining actor references.
        for(int n=Marks.Count-1;n>=0;n--)if(Marks[n].Blood&&Marks[n].Age>8&&(Marks[n].Point-PlayerPosition).Length>SpawnDistance)Marks.RemoveAt(n);
        _lifeSpawn+=dt;if(_lifeSpawn>2){_lifeSpawn=0;if(Animals.Count<AnimalCap){int pigeons=Animals.Count(a=>!a.Dead&&a.Kind==AnimalKind.Pigeon),others=Animals.Count(a=>!a.Dead&&a.Kind!=AnimalKind.Pigeon);
            if(pigeons<PigeonTarget)SpawnAnimal(AnimalKind.Pigeon);else if(others<OtherAnimalTarget)SpawnAnimal(Pick(3) switch{0=>AnimalKind.Cat,1=>AnimalKind.Dog,_=>AnimalKind.Bird});}}
    }
    private void RecoverOverturned(CityCar c,double dt)
    {
        if(!c.Recovering){
            bool stuck=Math.Cos(c.Roll)*Math.Cos(c.Pitch)<-.45&&Math.Abs(c.Speed)<.7&&c.Lift<.12;
            c.OverturnedTime=stuck?c.OverturnedTime+dt:0;
            if(c.OverturnedTime>4){c.RecoveryTime=.001;c.RecoveryRoll=Math.IEEERemainder(c.Roll,Math.Tau);c.RecoveryPitch=Math.IEEERemainder(c.Pitch,Math.Tau);}
            return;
        }
        c.RecoveryTime+=dt;double t=Math.Clamp(c.RecoveryTime/2.2,0,1);
        double turn=Math.Clamp((t-.18)/.64,0,1);turn=turn*turn*(3-2*turn);
        c.Roll=c.RecoveryRoll*(1-turn);c.Pitch=c.RecoveryPitch*(1-turn);c.Lift=1.5*Math.Sin(Math.PI*t);
        c.Body.Vx=c.Body.Vy=c.Body.W=c.VerticalVelocity=c.RollVelocity=c.PitchVelocity=0;
        if(t>=1){c.RecoveryTime=c.OverturnedTime=c.Roll=c.Pitch=c.Lift=0;}
    }
}
