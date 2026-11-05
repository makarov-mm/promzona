using System.Text.Json;
using Promzona.Game;
static partial class Tests
{
    static CityTraffic FootSandbox(){var t=Empty();t.Props.Clear();t.Pickups.Clear();t.Cars.Add(Car(t.World,0,0,0));t.Hero.Id=1;Call(t,"ResetPlayer");t.SpawnDistance=5000;return t;}
    static void Tick(CityTraffic t,double seconds){for(int i=0;i<(int)(seconds*60);i++)t.Step(1d/60);}
    static void WalkPlace(CityTraffic t,double x,double z){var p=t.Player;p.Position=new(x,z);p.Body.X=x;p.Body.Y=z;p.Body.Vx=p.Body.Vy=0;Call(t,"RebuildActorGrid");}
    static void RunPlayer(){
        CityLayout.Install(CityLayout.Generate(73921));
        Test("exit and pedestrian controls",()=>{
            var t=FootSandbox();var old=t.Hero;Check(t.InteractVehicle()&&t.OnFoot,"F exits a stopped car");
            Check(t.Pedestrians.Contains(t.Player)&&t.World.Bodies.Contains(t.Player.Body)&&old.Driverless,"player has a physical disc; abandoned car has no NPC driver");
            Check(!CityTraffic.CarPersonOverlap(old.Position,old.Heading,t.Player.Position,.3,old.HalfLength,old.HalfWidth),"player exits outside the vehicle footprint");
            var at=t.Player.Position;t.WalkInput=new(-1,0);Tick(t,1.5);double walk=(t.Player.Position-at).Length;
            Check(walk>3&&walk<5,"walking input moves the character at a human speed");Check(old.Position.Length<.05,"parked car does not drive itself");
            at=t.Player.Position;t.Sprint=true;Tick(t,1);Check((t.Player.Position-at).Length>4.8,"Shift produces faster running");
            t.ClearPlayerInput();Tick(t,.6);Check(t.Player.Speed<.05,"focus/menu input reset stops walking");
            Check(t.PlayerPosition==t.Player.Position&&t.PlayerPosition!=old.Position,"world focus follows the pedestrian");
            Check(t.Player.Height>=1.6&&t.Player.Height<1.9,"player uses the same human scale as other people");
        });
        Test("door constraints",()=>{
            var t=FootSandbox();t.Hero.Speed=12;Check(!t.InteractVehicle()&&!t.OnFoot&&t.InteractionMessage=="stop","cannot exit a fast vehicle");
            t.Hero.Speed=0;Tick(t,.4);t.Hero.Roll=2;Check(t.InteractVehicle()&&t.OnFoot,"stationary overturned vehicle allows emergency exit");
            Check(!CityTraffic.CarPersonOverlap(t.Hero.Position,t.Hero.Heading,t.PlayerPosition,.43,t.Hero.HalfLength,t.Hero.HalfWidth),"overturned exit remains outside the hull");
            t=FootSandbox();Npc(t,0,-2.2,.5);Npc(t,0,2.2,.5);Call(t,"RebuildActorGrid");Check(t.InteractVehicle()&&t.OnFoot,"blocked side doors use a clear emergency exit");
            Check(t.Cars.All(c=>!CityTraffic.CarPersonOverlap(c.Position,c.Heading,t.PlayerPosition,.43,c.HalfLength,c.HalfWidth)),"emergency exit cannot put player inside any blocking car");
        });
        Test("take another car without changing identity",()=>{
            var t=FootSandbox();var original=t.Hero;var c=Npc(t,2,-7,0);c.Id=25;c.Damage=.25;c.Path=new(c.Position,new(-7,20));
            var paint=CityAppearance.Paint(c);var mass=c.Mass;t.InteractVehicle();WalkPlace(t,c.Position.X-c.HalfWidth-1.3,.5);Tick(t,.4);
            var previousPeople=t.Pedestrians.Select(p=>p.Id).ToHashSet();var entryPoint=c.Position;
            int occupants=c.OccupantCount;Check(t.NearbyEnterableCar()==c&&t.InteractVehicle(),"nearby occupied truck offers entry");
            Tick(t,2);Check(!t.OnFoot&&t.Hero==c&&c.Hero&&!original.Hero,"possession transfers to the selected vehicle");
            // Ambient replenishment can add unrelated residents at the map edge
            // during this two-second animation. Count the actual ejected crew.
            Check(t.Pedestrians.Count(p=>!previousPeople.Contains(p.Id)&&(p.Position-entryPoint).Length<15)==occupants&&
                c.OccupantCount==0&&t.Pedestrians.All(p=>!p.IsPlayer),"all occupants are displaced and player body leaves contact world");
            Check(t.Pedestrians.Any(p=>p.Fear>.4&&!p.Dead)&&t.Wanted>0,"displaced driver survives and reacts; carjacking creates wanted level");
            Check(c.Mass==mass&&CityAppearance.Paint(c)==paint&&c.Id==25,"model dimensions, mass, identity and paint survive carjacking");
            Check(c.Damage>=.25&&original.Driverless,"car damage is retained and original remains parked");
            Check(!t.World.Bodies.Contains(t.Player.Body),"no invisible player collision body remains inside vehicle");
            Tick(t,.4);Check(t.InteractVehicle()&&t.OnFoot,"player can exit a stolen truck");Tick(t,.4);
            int before=t.Pedestrians.Count;Check(t.InteractVehicle(),"player can reenter an empty vehicle");Tick(t,2);
            Check(!t.OnFoot&&t.Pedestrians.Count==before-1,"empty vehicle does not create another driver");
        });
        Test("cancel and moving target",()=>{
            var t=FootSandbox();t.InteractVehicle();Tick(t,.4);Check(t.InteractVehicle(),"start reentry");t.WalkInput=new(-1,0);Tick(t,.1);
            Check(t.OnFoot&&t.EnteringCarId<0,"movement cancels entry without losing the pedestrian");
            t.ClearPlayerInput();Tick(t,.4);t.Hero.Speed=8;Check(t.NearbyEnterableCar() is null,"fast passing cars cannot be stolen by teleporting");
        });
        Test("pedestrian pickup and combat",()=>{
            var t=FootSandbox();t.InteractVehicle();WalkPlace(t,-1.8,-15);t.Player.GunDamage=.6;t.Hero.Damage=.4;
            t.Pickups.Add(new(){Id=1,Kind=CityPickupKind.Pills,Position=t.PlayerPosition,Created=-2});Call(t,"PickupStep",.01);
            Check(Math.Abs(t.Player.GunDamage-.25)<1e-9&&t.Hero.Damage==.4,"medicine heals the person rather than the parked car");
            t.Cash.Add(new(){Position=t.PlayerPosition,Created=-2,Amount=45});Call(t,"LifeAfterContacts",.01);Check(t.Money==45,"cash can be collected on foot");
            t.Inventory.Give(CityWeaponId.Pistol);t.PlayerViewHeading=0;t.PlayerFireHeld=true;var victim=Person(t,-1.8,-7);Call(t,"RebuildActorGrid");Call(t,"PlayerCombat");
            Check(t.PlayerShotsFired==1&&t.Inventory.Ammo(CityWeaponId.Pistol)==29&&victim.GunDamage>0,"on-foot shot follows view direction and hits the first actor");
            Check(!t.Player.Dead&&t.Player.GunDamage==.25,"player ray cannot hit its own body");
            var gunner=Person(t,-1.8,-25);gunner.Id=90010;gunner.AggressionUntil=100;gunner.AimTime=1;gunner.Heading=0;t.PlayerFireHeld=false;Call(t,"RebuildActorGrid");Call(t,"CombatStep",.1);
            Check(t.Player.GunDamage>.25,"armed NPC targets and damages the pedestrian");
            t.AddCrime(2);t.DamagePlayer(10);Check(t.Player.Dead&&!t.World.Bodies.Contains(t.Player.Body),"fatal damage removes player from solid contacts");
            Tick(t,3.3);Check(t.OnFoot&&!t.Player.Dead&&t.PlayerHealth==1&&t.Wanted==0&&t.Respawns==1,"on-foot death respawns at a clinic with wanted cleared");
        });
        Test("car strike and abandoned explosion",()=>{
            var t=FootSandbox();t.InteractVehicle();WalkPlace(t,-20,0);var c=Npc(t,0,-20,-3);c.Body.Vy=9;
            t.HitPerson(t.Player,c,9);Check(t.Player.Dead,"player can be hit by traffic");
            var safe=FootSandbox();safe.InteractVehicle();WalkPlace(safe,-30,0);safe.Hero.Burning=true;safe.Hero.FireAge=13;
            Call(safe,"VehicleLifeBefore",.1);Check(safe.Hero.Destroyed&&!safe.PlayerUnavailable,"abandoned burning car can explode without killing distant player");
            Tick(safe,4);Check(safe.Respawns==0&&safe.OnFoot,"abandoned wreck cannot trigger car-player respawn");
        });
        Test("terrain walking and local focus",()=>{
            var t=QuietCity();t.Props.Clear();Place(t.Hero,1.9,-70);t.UpdateTerrainSupport(t.Hero);Check(t.InteractVehicle(),"exit on a real slope");
            var start=t.PlayerPosition;t.WalkInput=new(0,-1);t.Sprint=true;Tick(t,2);
            Check((t.PlayerPosition-start).Length>7&&t.Player.FlightHeight<.1,"person runs along terrain without artificial lift");
            WalkPlace(t,1.9,320);Call(t,"RefreshDetail");Check(!t.Hero.Physical&&t.Player.Physical,"physics bubble follows player far from abandoned car");
            var camera=new ChaseCamera();camera.Reset(t);Check(camera.Target==t.PlayerPosition&&camera.Distance<10,"walking camera centers on the human");
            var building=CityModel.Blocks.First();WalkPlace(t,building.X-building.HalfX-1,building.Z);t.WalkInput=new(1,0);Tick(t,1);
            Check(t.PlayerPosition.X<=building.X-building.HalfX-.25,"player cannot run through a building facade");
        });
        Test("on-foot saves and legacy compatibility",()=>{
            var model=new CityModel();var t=model.Traffic;Check(t.InteractVehicle(),"full city allows initial exit");
            t.Player.GunDamage=.22;t.Inventory.Give(CityWeaponId.Automatic);var pos=t.PlayerPosition;var car=t.Hero.Position;
            var snapshot=JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(t.CaptureWorld(model),CityState.Json),CityState.Json)!;
            t.RestoreWorld(snapshot,model);Check(t.OnFoot&&t.PlayerPosition==pos&&t.Hero.Position==car,"save restores feet and parked car as separate positions");
            Check(t.Pedestrians.Count(p=>p.IsPlayer)==1&&t.World.Bodies.Count(b=>b==t.Player.Body)==1&&Math.Abs(t.Player.GunDamage-.22)<1e-9,"save creates exactly one player body and restores human health");
            Check(t.Inventory.Ammo(CityWeaponId.Automatic)==30,"inventory persists through foot save");
            snapshot.Player=null;snapshot.Clock.Remove("<OnFoot>k__BackingField");t.RestoreWorld(snapshot,model);
            Check(!t.OnFoot&&!t.Pedestrians.Any(p=>p.IsPlayer),"old format 2 save defaults to driving without a phantom pedestrian");
        });
    }
}
