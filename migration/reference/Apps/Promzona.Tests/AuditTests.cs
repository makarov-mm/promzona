using System.Reflection;
using System.Text.Json;
using Promzona.Game;
static partial class Tests
{
    // Guards for the source audit of 03.11.2026 (Docs/PROMZONA-AUDIT-2026-11-03.md).
    static void RunAudit(){
        Test("scooter support is validated against the saved city",()=>{
            var city=CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:4);
            CityLayout.Install(city);
            var model=new CityModel();
            var traffic=model.Traffic;
            var scooter=traffic.Scooters.First();
            var owner=traffic.Pedestrians.First(p=>p.Id==scooter.OwnerId);
            var bridge=city.Levels.Last(s=>!s.Tunnel);
            owner.Position=new(bridge.X,bridge.CentreZ(bridge.X));
            owner.RoadLevel=bridge.Id;
            traffic.DismountScooter(owner);
            var saved=traffic.CaptureWorld(model);
            var menuCity=CityLayout.Generate(42);
            CityLayout.Install(menuCity);
            CityTraffic.ValidateWorld(saved);
            Check(CityLayout.Current==menuCity,"reading a slot from another city leaves the active city intact");
            CityLayout.Install(city);
            traffic.RestoreWorld(saved,model);
            Check(traffic.Scooters.Single(s=>s.Id==scooter.Id).RoadLevel==bridge.Id,"a parked scooter reloads on its bridge");
            saved.Scooters.First().State[nameof(CityScooter.RoadLevel)]=city.Levels.Length+1;
            bool rejected=false;
            try{CityTraffic.ValidateWorld(saved);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"a scooter on a nonexistent road level is rejected");
        });
        Test("a saved point carries no derived length",()=>{
            string json=JsonSerializer.Serialize(new CityPoint(3,4),CityState.Json);
            Check(!json.Contains("Length")&&JsonSerializer.Deserialize<CityPoint>(json,CityState.Json).Length==5,"CityPoint serializes X and Z only and reads back whole");
        });
        Test("walker route state is validated before restore",()=>{
            CityLayout.Install(CityLayout.Generate(73921));var m=new CityModel();m.Step(1d/60);
            var world=m.Traffic.CaptureWorld(m);CityTraffic.ValidateWorld(world);
            var state=world.People[0].State;bool had=state.TryGetValue("OriginKey",out object? kept);
            state["OriginKey"]=100000;bool rejected=false;
            try{CityTraffic.ValidateWorld(world);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"a place key beyond the table is refused by ValidateWorld, not by the first Step");
            if(had)state["OriginKey"]=kept;else state.Remove("OriginKey");
            CityTraffic.ValidateWorld(world);
            state["NextI"]=CityTraffic.Nodes;rejected=false;
            try{CityTraffic.ValidateWorld(world);}catch(InvalidDataException){rejected=true;}
            Check(rejected,"a node index beyond the grid is refused");
        });
        Test("a restored dumpster keeps its box",()=>{
            CityLayout.Install(CityLayout.Generate(73921));var m=new CityModel();for(int k=0;k<6;k++)m.Step(1d/60);
            var before=m.Traffic.Props.FirstOrDefault(p=>p.Kind==StreetKind.Dumpster&&p.Body is not null);
            if(before is null){Console.WriteLine("no dumpster body near the start; skipped");return;}
            var world=m.Traffic.CaptureWorld(m);var second=new CityModel();second.Traffic.RestoreWorld(world,second);
            var after=second.Traffic.Props.First(p=>p.Id==before.Id);var was=before.Body!;
            Check(after.Body is {} b&&b.Shape==was.Shape&&Math.Abs(b.HalfW-was.HalfW)<1e-12&&Math.Abs(b.HalfH-was.HalfH)<1e-12&&
                Math.Abs(b.InvMass-was.InvMass)<1e-12&&b.Friction==was.Friction&&b.Restitution==was.Restitution,"a dumpster comes back from a save with the body streaming gave it");
        });
        Test("the handover car of a running contract is not swept as abandoned",()=>{
            var t=Empty();t.Cars.Add(Car(t.World,0,0,0));t.Hero.Id=1;Call(t,"ResetPlayer");t.SpawnDistance=100;
            var far=Npc(t,0,0,400);far.Driverless=true;
            FieldInfo kind=typeof(CityTraffic).GetField("_jobKind",BindingFlags.NonPublic|BindingFlags.Instance)!;
            FieldInfo carId=typeof(CityTraffic).GetField("_jobCarId",BindingFlags.NonPublic|BindingFlags.Instance)!;
            kind.SetValue(t,JobKind.VehicleDelivery);carId.SetValue(t,far.Id);
            Call(t,"VehicleLifeAfter",1d/60);Check(t.Cars.Contains(far),"the contract car stays while the job runs, however far the player drives");
            kind.SetValue(t,JobKind.None);
            Call(t,"VehicleLifeAfter",1d/60);Check(!t.Cars.Contains(far),"without the job the same driverless car leaves out of view");
        });
        Test("a respawn after a passenger ride leaves the vehicle its driver",()=>{
            var t=FootSandbox();var hero=t.Hero;hero.Driverless=false;
            PropertyInfo onFoot=typeof(CityTraffic).GetProperty("OnFoot")!,passenger=typeof(CityTraffic).GetProperty("PassengerMode")!;
            onFoot.SetValue(t,false);passenger.SetValue(t,true);
            bool placed=(bool)Call(t,"PlaceAt",new CityPoint(20,20))!;
            Check(placed&&t.OnFoot&&!t.PassengerMode&&!hero.Driverless,"the tram the player rode keeps its driver after the clinic");
            onFoot.SetValue(t,false);
            placed=(bool)Call(t,"PlaceAt",new CityPoint(24,24))!;
            Check(placed&&t.OnFoot&&hero.Driverless,"a car the player was driving is left without a driver");
        });
        Test("incremental actor grid equals a fresh rebuild",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true));var t=new CityTraffic();
            for(int k=0;k<20;k++)t.Step(1d/60);
            Call(t,"RefreshActorGrid");
            var rng=new Random(5);bool ok=true;int hits=0;
            for(int n=0;n<300&&ok;n++){
                var p=new CityPoint((rng.NextDouble()*2-1)*CityTraffic.LandEdge,(rng.NextDouble()*2-1)*CityTraffic.LandEdge);double r=3+rng.NextDouble()*40;
                var cars=((IEnumerable<CityCar>)Call(t,"NearbyCars",p,r)!).ToList();
                var people=((IEnumerable<CityPedestrian>)Call(t,"NearbyPeople",p,r)!).ToList();
                ok&=cars.Count==cars.Distinct().Count()&&people.Count==people.Distinct().Count();
                ok&=t.Cars.Where(c=>Math.Abs(c.Position.X-p.X)<=r&&Math.Abs(c.Position.Z-p.Z)<=r).All(cars.Contains);
                ok&=t.Pedestrians.Where(q=>Math.Abs(q.Position.X-p.X)<=r&&Math.Abs(q.Position.Z-p.Z)<=r).All(people.Contains);
                ok&=cars.All(c=>t.Cars.Contains(c)&&Math.Abs(c.Position.X-p.X)<r+16&&Math.Abs(c.Position.Z-p.Z)<r+16);
                ok&=people.All(q=>t.Pedestrians.Contains(q)&&Math.Abs(q.Position.X-p.X)<r+8&&Math.Abs(q.Position.Z-p.Z)<r+8);
                hits+=cars.Count+people.Count;
            }
            Check(ok&&hits>0,"after twenty frames the refreshed cells list every live actor once, within range");
            var mover=t.Cars[1];var old=mover.Position;mover.Position=old+new CityPoint(100,0);Call(t,"RefreshActorGrid");
            Check(((IEnumerable<CityCar>)Call(t,"NearbyCars",mover.Position,1)!).Contains(mover)&&!((IEnumerable<CityCar>)Call(t,"NearbyCars",old,1)!).Contains(mover),"a moved car is re-bucketed by the refresh");
            var gone=t.Cars[t.Cars.Count-1];t.Cars.RemoveAt(t.Cars.Count-1);Call(t,"RefreshActorGrid");
            Check(!((IEnumerable<CityCar>)Call(t,"NearbyCars",gone.Position,1)!).Contains(gone),"a removed car is not listed after the next refresh");
        });
    }
}
