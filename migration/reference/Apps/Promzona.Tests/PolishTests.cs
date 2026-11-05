using Promzona.Game;
using System.Text.Json;
static partial class Tests
{
    static void RunPolish(){
        RunTransitCabins();
        int RiderId(TransitRider r){var p=new CityPedestrian();CityState.Restore(p,r.Person.State);return p.Id;}
        CityLayout.Install(CityLayout.Generate(73921));
        Test("independent human and vehicle health",()=>{
            var t=FootSandbox();t.Player.GunDamage=.65;t.Hero.Damage=.8;
            Check(Math.Abs(t.PlayerHealth-.35)<1e-9&&Math.Abs(t.VehicleHealth-.2)<1e-9,"human and car meters use different state");
            t.Pickups.Add(new(){Id=999,Kind=CityPickupKind.Pills,Position=t.Hero.Position,Created=-2});Call(t,"PickupStep",.1);
            Check(Math.Abs(t.PlayerHealth-.7)<1e-9&&t.Hero.Damage==.8,"in-car pills heal occupant without repairing vehicle");
            t.Hero.Damage=0;t.InteractVehicle();Check(t.OnFoot&&Math.Abs(t.PlayerHealth-.7)<1e-9,"leaving preserves human health");
            Tick(t,.5);t.InteractVehicle();Tick(t,2);Check(!t.OnFoot&&Math.Abs(t.PlayerHealth-.7)<1e-9,"reentry preserves human health");
        });
        Test("person animation transitions",()=>{
            var p=new CityPedestrian{Id=10,Heading=3.12,Speed=0};var m=new CityPersonMotion();m.Sample(p,0);
            p.Speed=1.4;p.Heading=-3.12;var a=m.Sample(p,.016);
            Check(a.Blend>0&&a.Blend<1&&a.From<64&&a.To>=64&&a.To<128,"idle to walk is blended");
            Check(m.Sample(p,.016)==a,"shadow and color share same-time animation sample");
            Check(Math.Abs(m.Heading-3.12)<.1,"heading takes short arc across wrap");
            m.Sample(p,.2);p.Speed=0;var b=m.Sample(p,.216);Check(b.Blend<1,"stopping also blends");
            p.GunDamage=.1;m.Sample(p,.232);Check(m.Lean>0,"hit starts a short flinch");
            p.Dead=true;var dead=m.Sample(p,.248);Check(dead.Blend==1&&dead.To<64&&m.Lean==0,"death cancels locomotion and hit overlay");
        });
        Test("save slot isolation",()=>{
            string root=Path.Combine(Path.GetTempPath(),"promzona-slots-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try{
                for(int n=1;n<=3;n++)File.WriteAllText(CitySaveSlots.PathFor(root,n),"slot "+n);
                Check(Enumerable.Range(1,3).All(n=>File.ReadAllText(CitySaveSlots.PathFor(root,n))=="slot "+n),"slot paths cannot overwrite each other");
                Check(Path.GetFileName(CitySaveSlots.PathFor(root,1))=="save.json","legacy file remains slot one");
                bool threw=false;try{CitySaveSlots.PathFor(root,4);}catch(ArgumentOutOfRangeException){threw=true;}Check(threw,"out-of-range slot rejected");
            }finally{foreach(var file in Directory.GetFiles(root))File.Delete(file);Directory.Delete(root);}
        });
        Test("armed NPC uses a parked car as cover",()=>{
            var t=FootSandbox();Place(t.Hero,10,0);var cover=Npc(t,0,0,0);cover.Driverless=true;
            var p=Person(t,-5,0,3);p.Id=990003;p.AggressionUntil=100;Call(t,"RebuildActorGrid");
            typeof(CityTraffic).GetProperty("Time")!.SetValue(t,1d);Call(t,"CoverWalk",p,.016);
            typeof(CityTraffic).GetProperty("Time")!.SetValue(t,2d);
            Check((bool)Call(t,"CoverWalk",p,.016)!&&p.Body.Vx>0,"threatened armed person approaches the sheltered side");
            p.AggressionUntil=0;Check(!(bool)Call(t,"CoverWalk",p,.016)!,"cover is released when aggression ends");
        });
        Test("garage retains actual car offscreen and in save",()=>{
            var m=new CityModel();var t=m.Traffic;var g=t.Garages.First();Place(t.Hero,g.Door.X,g.Door.Z-5);t.UpdateTerrainSupport(t.Hero);t.Hero.Damage=.21;t.Hero.Paint=5;t.SetMoney(1000);Call(t,"RebuildActorGrid");
            Check(t.UseOwnedGarage()&&t.OwnedGarageId==g.Id&&t.Money==600,"garage ownership costs 400");
            int id=t.Hero.Id;Check(t.UseOwnedGarage()&&t.OnFoot&&t.Hero.OwnedGarageId==g.Id,"parking exits and retains the same car");
            WalkPlace(t,300,300);Call(t,"VehicleLifeAfter",.1);
            Check(t.Cars.Any(c=>c.Id==id&&c.Paint==5&&c.Damage==.21),"owned car survives distant abandonment cleanup");
            var save=JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(t.CaptureWorld(m),CityState.Json),CityState.Json)!;
            t.RestoreWorld(save,m);Check(t.OwnedGarageId==g.Id&&t.OwnedVehicleCount==1&&t.Cars.Any(c=>c.Id==id&&c.Paint==5),"garage and car identity survive save/load");
            var owned=t.Hero;var other=t.Cars.First(c=>c!=owned);owned.Hero=false;other.Hero=true;int idx=t.Cars.IndexOf(other);(t.Cars[0],t.Cars[idx])=(t.Cars[idx],t.Cars[0]);
            Call(t,"VehicleLifeAfter",.1);Check(t.Cars.Contains(owned),"a retained car also survives when it is no longer the hero slot");
            var legacy=t.CaptureWorld(m);legacy.Clock.Remove("<OwnedGarageId>k__BackingField");legacy.Clock.Remove("<ChainStage>k__BackingField");
            t.RestoreWorld(legacy,m);Check(t.OwnedGarageId==0&&t.ChainStage==0,"missing old-save progression fields do not leak from current game");
        });
        Test("contract chain awards once and persists stage",()=>{
            var m=new CityModel();var t=m.Traffic;
            Check(t.StartChainContract()&&t.JobFare==300&&t.ContractMinimumHealth==.5,"chain starts with vehicle handover");
            Call(t,"CompleteContract");Check(t.ChainStage==1&&t.Money==300,"first stage advances");
            var s=t.CaptureWorld(m);t.RestoreWorld(s,m);
            Check(t.ChainStage==1&&t.StartChainContract()&&t.JobKind==JobKind.Elimination&&t.JobFare==500,"next stage restored as elimination");
            t.CancelContract();Check(t.ChainStage==1&&t.StartChainContract(),"failure permits retry without skipping a stage");
            Call(t,"CompleteContract");Check(t.ChainStage==2&&t.StartChainContract()&&t.ContractMinimumHealth==.7&&t.JobTimeLeft<421,"final handover demands 70 percent in seven minutes");
            Call(t,"CompleteContract");Check(t.ChainStage==0&&t.ChainsCompleted==1&&t.Money==2000,"three payouts plus finale bonus exactly once");
            int money=t.Money;Call(t,"ContractDeath",new CityPedestrian(),true);Check(t.Money==money,"unrelated death cannot repeat payout");
        });
        Test("transit passengers board alight and save",()=>{
            var m=new CityModel();var t=m.Traffic;var c=t.Cars.First(c=>c.Kind==9);var r=CityTransit.ById(c.TransitRoute)!;
            int stop=Enumerable.Range(0,r.Stops.Length).First(k=>CityWater.UrbanSite(CityTraffic.TransitWaitingPoint(r,k),.8));
            var pose=r.Sample(r.Stops[stop]);c.Position=pose.Point;c.Heading=pose.Heading;c.Body.X=pose.Point.X;c.Body.Y=pose.Point.Z;c.Body.Angle=-pose.Heading;c.Speed=0;c.Body.Vx=c.Body.Vy=0;c.TransitAlong=r.Stops[stop];c.TransitStopUntil=t.Time+5;
            var door=(CityPoint)typeof(CityTraffic).GetMethod("DoorPoint",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,new object[]{c,1,0d})!;var p=Person(t,door.X,door.Z);p.Id=990001;p.WaitingTransitRoute=r.Id;p.WaitingTransitStop=stop;p.TransitWaitUntil=100;p.Fear=0;
            Call(t,"RebuildActorGrid");Call(t,"PassengerStep",1d);
            Check(!t.Pedestrians.Contains(p)&&t.TransitRiders.Any(a=>RiderId(a)==p.Id),"a walker enters as the same identified passenger");
            var save=t.CaptureWorld(m);CityTraffic.ValidateWorld(save);t.RestoreWorld(save,m);
            Check(t.TransitRiders.Any(a=>RiderId(a)==p.Id),"passenger survives save/load");
            var rider=t.TransitRiders.First(a=>RiderId(a)==p.Id);c=t.Cars.First(a=>a.Id==rider.CarId);
            c.TransitAlong=rider.Destination;pose=r.Sample(c.TransitAlong);c.Position=pose.Point;c.Body.X=pose.Point.X;c.Body.Y=pose.Point.Z;c.TransitStopUntil=t.Time+5;c.Speed=0;
            Call(t,"RebuildActorGrid");Call(t,"PassengerStep",1d);
            Check(!t.TransitRiders.Any(a=>RiderId(a)==p.Id)&&t.Pedestrians.Any(a=>a.Id==p.Id&&!a.Dead),"same person leaves on destination sidewalk");
            // A stolen vehicle must release the saved rider, not manufacture a duplicate crew member.
            var returned=t.Pedestrians.First(a=>a.Id==p.Id);t.Pedestrians.Remove(returned);Call(t,"Remove",returned.Body);
            t.TransitRiders.Add(rider with{Destination=r.Wrap(c.TransitAlong+200)});c.Driverless=true;
            Call(t,"RebuildActorGrid");Call(t,"PassengerStep",1d);
            Check(t.TransitRiders.All(a=>RiderId(a)!=p.Id)&&t.Pedestrians.Count(a=>a.Id==p.Id)==1,"a stopped stolen tram evacuates the original passenger");
        });
        Test("player rides with a driver rather than hijacking",()=>{
            var t=FootSandbox();var c=Npc(t,9,-8,0);c.Id=90;c.TransitRoute=1;c.TransitStopUntil=100;c.Occupants=1;c.Driverless=false;
            t.InteractVehicle();WalkPlace(t,c.Position.X-c.HalfWidth-1,.5);typeof(CityTraffic).GetProperty("Time")!.SetValue(t,.4);c.Speed=0;Call(t,"RebuildActorGrid");
            Check(t.RideTransit()&&t.EnteringCarId==c.Id,"B starts passenger entry at a stopped tram");
            // Exercise entry animation without applying the remote route constraint in this door fixture.
            var door=(CityPoint)typeof(CityTraffic).GetMethod("DoorPoint",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static)!.Invoke(null,new object[]{c,1,0d})!;WalkPlace(t,door.X,door.Z);
            for(int i=0;i<60;i++)Call(t,"EntryStep",1d/60);
            Check(t.PassengerMode&&!t.OnFoot&&t.Hero==c&&!c.Driverless&&!t.PlayerControls(c)&&t.Wanted==0,"riding preserves driver and ignores driving commands");
            typeof(CityTraffic).GetProperty("Time")!.SetValue(t,2d);c.Speed=0;c.Body.Vx=c.Body.Vy=0;
            Check(t.InteractVehicle()&&t.OnFoot&&!t.PassengerMode&&!c.Driverless,"leaving keeps public transport driver active");
        });
        foreach(int seed in new[]{73921,42,1})Test("district layout "+seed,()=>{
            CityLayout.Install(CityLayout.Generate(seed,gridSize:49,wideRoads:true,roadRevision:4));var l=CityLayout.Current;
            var kinds=l.Blocks.GroupBy(b=>CityNeighbourhoods.At(new(b.X,b.Z))).ToDictionary(g=>g.Key,g=>g.ToArray());
            Check(kinds.Count==6,"all six district families appear");
            Check(kinds[CityNeighbourhood.PrivateHouses].Count(b=>b.Height<5.5)>100,"private quarter has low houses");
            Check(kinds[CityNeighbourhood.Garages].Count(b=>b.Height==3.4)>100,"cooperatives have small garage rows");
            Check(l.Blocks.All(b=>CityWater.Depth(new(b.X,b.Z))<.05),"district buildings stay out of water");
            Check(CityTransit.Routes.Count==4&&CityTransit.Routes.All(r=>r.Length>1800),"district style preserves connected transit loops");
        });
        CityLayout.Install(CityLayout.Generate(73921));
    }
}
