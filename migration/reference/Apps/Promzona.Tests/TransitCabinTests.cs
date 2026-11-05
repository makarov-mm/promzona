using Promzona.Game;
using System.Text.Json;
static partial class Tests
{
    static void RunTransitCabins()
    {
        int RiderId(TransitRider r){var p=new CityPedestrian();CityState.Restore(p,r.Person.State);return p.Id;}
        foreach(int kind in new[]{9,10})Test("transit cabin seating "+kind,()=>{
            var car=new CityCar{Kind=kind};var seats=Enumerable.Range(0,CityTransitCabin.Capacity).Select(n=>CityTransitCabin.At(kind,n)).ToArray();
            foreach(var seat in seats){
                Check(Math.Abs(seat.X)+.26<car.HalfWidth&&Math.Abs(seat.Z)+.65<car.HalfLength,"seated body remains within cabin");
                Check(seat.Floor>.5&&seat.Floor+1.6<car.Height,"passenger feet and head fit cabin height");
            }
            Check(seats.SelectMany((a,i)=>seats.Skip(i+1).Select(b=>Math.Sqrt(Math.Pow(a.X-b.X,2)+Math.Pow(a.Z-b.Z,2)))).All(d=>d>.9),"seat centres do not overlap");
            Check(kind==9?seats.All(s=>s.X*Math.Sin(s.Heading)<0):seats.All(s=>s.Heading==0),"tram passengers face aisle; trolley passengers face forward");
            var driver=CityTransitCabin.Driver(kind);Check(driver.Z>seats.Max(s=>s.Z)+1,"driver separate from passenger rows");
            var aisle=CityTransitCabin.Aisle(kind);Check(seats.All(s=>Math.Sqrt(Math.Pow(s.X-aisle.X,2)+Math.Pow(s.Z-aisle.Z,2))>.65),"full cabin has a clear player standing place");
        });
        foreach(int seed in new[]{73921,42})Test("initial occupied transit preserves actors "+seed,()=>{
            CityLayout.Install(CityLayout.Generate(seed,roadRevision:4));var model=new CityModel();var t=model.Traffic;
            var initial=t.Pedestrians.ToDictionary(p=>p.Id);int before=t.Pedestrians.Count;
            Call(t,"PassengerStep",1d);
            Check(t.TransitRiders.Count>20&&t.TransitRiders.Count<=96,"operating routes start with a bounded occupied fleet");
            Check(t.Pedestrians.Count+t.TransitRiders.Count==before,"boarding transfers population without duplication");
            Check(t.TransitRiders.Select(RiderId).Distinct().Count()==t.TransitRiders.Count,"each saved passenger has a unique actor ID");
            Check(t.TransitRiders.All(r=>!t.Pedestrians.Any(p=>p.Id==RiderId(r))&&!t.World.Bodies.Contains(initial[RiderId(r)].Body)),"riders have no invisible walking body");
            Check(t.TransitRiders.All(r=>initial.ContainsKey(RiderId(r))&&CityTransit.ById(t.Cars.First(c=>c.Id==r.CarId).TransitRoute)!.Stops.Contains(r.Destination)),"same citizens have valid destination stops");
            Check(new[]{9,10}.All(kind=>t.Cars.Any(c=>c.Kind==kind&&t.TransitRiders.Any(r=>r.CarId==c.Id))),"both tram and trolley carry people");
            var ids=t.TransitRiders.Select(RiderId).Order().ToArray();
            var save=JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(t.CaptureWorld(model),CityState.Json),CityState.Json)!;
            CityTraffic.ValidateWorld(save);t.RestoreWorld(save,model);Call(t,"PassengerStep",1d);
            Check(t.TransitRiders.Select(RiderId).Order().SequenceEqual(ids),"loading preserves riders without initial reseeding");
            var car=t.Cars.First(c=>c.Id==t.TransitRiders[0].CarId);var victims=t.TransitRiders.Where(r=>r.CarId==car.Id).Select(RiderId).ToArray();
            var route=CityTransit.ById(car.TransitRoute)!;var pose=route.Sample(car.TransitAlong);Place(car,pose.Point.X,pose.Point.Z);car.Heading=pose.Heading;
            // Driverless evacuation uses the same route-aware sidewalk placement as a stolen vehicle.
            car.Driverless=true;Call(t,"RebuildActorGrid");Call(t,"PassengerStep",1d);
            Check(victims.All(id=>!t.TransitRiders.Any(r=>RiderId(r)==id)&&t.Pedestrians.Count(p=>p.Id==id)==1),"evacuated passengers return exactly once");
        });
        CityLayout.Install(CityLayout.Generate(73921));
    }
}
