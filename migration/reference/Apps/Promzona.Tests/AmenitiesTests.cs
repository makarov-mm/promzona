using Promzona.Game;
using System.Text.Json;
static partial class Tests {
 static void RunAmenities(){
  CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true));
  Test("avenues and dry placement",()=>{
   Check(CityLayout.WideRoads&&CityTraffic.RoadHalf>7&&CityTraffic.Sidewalk>9,"avenues have two lanes per direction and displaced pavements");
   var layout=CityLayout.Current;Check(layout.Furniture.All(p=>CityWater.UrbanSite(new(p.X,p.Z),.65)),"all generated street furniture excludes river banks and outer beach");
   Check(layout.Furniture.All(p=>CityAmenities.ClinicClear(new(p.X,p.Z))),"clinic doorways have no street furniture");
   double river=CityTraffic.Coordinate(16)+18;Check(Math.Abs(CityTerrain.Ground(river-17,23)-2.2)<.05,"canal-side road rests on the protected road corridor");
   var food=CityAmenities.Food();Console.WriteLine($"food={food.Count}, shawarma={food.Count(x=>x.Shawarma)}, stops={CityAmenities.Stops().Count}");
   Check(food.Count>20&&food.Any(s=>s.Shawarma)&&food.Any(s=>!s.Shawarma),"city includes both food services");
   Check(food.All(s=>CityWater.UrbanSite(s.Position,3)&&CityWater.Depth(s.Interaction)<.01),"food services and interaction points stay on dry developed land");
   Check(CityAmenities.Stops().Count>20&&CityAmenities.Stops().All(p=>CityWater.UrbanSite(p,2)),"bus stops never stand on sand or in rivers");
   var t=new CityTraffic();Check(t.Props.All(p=>CityWater.UrbanSite(p.Home,p.Radius+.2)),"all new movable props have dry urban homes");Check(t.Props.All(p=>CityAmenities.ClinicClear(p.Home)),"clinic healing squares have no movable prop homes");
   Check(t.Props.All(p=>food.All(s=>(Math.Abs(p.Home.X-s.Position.X)>(s.Shawarma?2.05:.9)+p.Radius||Math.Abs(p.Home.Z-s.Position.Z)>(s.Shawarma?1.6:.8)+p.Radius)&&(p.Home-s.Interaction).Length>1.35+p.Radius)),"food kiosks and purchase circles contain no prop homes");
   Check(t.Cars.Any(c=>c.Kind==8)&&t.Cars.Where(c=>c.Kind==8).All(c=>c.Mass==3100&&!c.PoliceVehicle),"gas service vans form an ambient mass-bearing class");
   Check(t.Pickups.Count(p=>p.Kind==CityPickupKind.EnergyDrink)==CityTraffic.EnergyTarget,"energy drink population is seeded to its target");
   Check(t.Pickups.All(p=>CityWater.UrbanSite(p.Position,1)),"new map pickups exclude sand and river");
  });
  Test("food footprints and district seams",()=>{
   double x=CityTraffic.Coordinate(3), z=CityTraffic.Coordinate(7), step=CityTraffic.Spacing;
   foreach(var p in new[]{new CityPoint(x,z),new(x+step,z),new(x,z+step),new(x+step,z+step),new(x-0.01,z+.01)})
    Check((from i in Enumerable.Range(-1,3) from j in Enumerable.Range(-1,3)
           where CityTraffic.InBlock(p,x+i*step,z+j*step) select (i,j)).Count()==1,"a site belongs to exactly one tile, including seams");
   var sites=CityImportedSites.All();
   foreach(var food in CityAmenities.Food().GroupBy(f=>f.Shawarma).Select(g=>g.First())){
    var imported=sites.Single(s=>s.Position==food.Position&&s.Model==food.Fixture.Model);
    Check(imported.Width==food.Fixture.Width&&imported.Length==food.Fixture.Depth&&imported.Height==food.Fixture.Height,"GLB uses fixture dimensions");
    Check(CityAmenities.BlocksWalk(food.Position)&&!CityAmenities.BlocksWalk(food.Interaction),"both service counters can be reached on foot");
   }
  });
  Test("food transactions and clinic",()=>{
   var t=FootSandbox();t.InteractVehicle();var site=t.FoodSites.First(s=>!s.Shawarma);WalkPlace(t,site.Interaction.X,site.Interaction.Z);t.Player.GunDamage=.6;t.SetMoney(20);
   Check(t.BuyFood()&&t.Money==15&&Math.Abs(t.PlayerHealth-.6)<1e-8,"vending charges five dollars and heals twenty points");
   Check(t.Sounds.Any(s=>s.Name=="spend"&&s.Ui),"successful purchase emits centered payment sound");
   Check(!t.BuyFood()&&t.Money==15,"held interaction cannot charge twice in one instant");
   Tick(t,1);t.Player.GunDamage=0;Check(!t.BuyFood()&&t.Money==15,"full health never spends money");
   Tick(t,1);t.Player.GunDamage=.6;t.SetMoney(0);Check(!t.BuyFood()&&t.PlayerHealth==.4,"insufficient funds neither heal nor overdraw");
   Tick(t,1);site=t.FoodSites.First(s=>s.Shawarma);WalkPlace(t,site.Interaction.X,site.Interaction.Z);t.SetMoney(30);Check(t.BuyFood()&&t.Money==15&&Math.Abs(t.PlayerHealth-.9)<1e-8,"shawarma charges fifteen and heals fifty points");
   t.AddCrime(4);t.Player.Dead=true;t.Player.GunDamage=1;Check((bool)Call(t,"PlaceAtClinic")!,"clinic finds an unobstructed doorway");
   Check(t.OnFoot&&!t.PlayerUnavailable&&t.PlayerHealth==1&&t.Wanted==0,"clinic respawn is healthy, on foot, without wanted level");
   Check(CityTraffic.ClinicBlocks.Any(s=>(CityAmenities.ClinicEntrance(s.I,s.J)-t.PlayerPosition).Length<1.6),"respawn lies inside blue clinic square");
   t.Player.GunDamage=.5;Call(t,"AmenitiesStep",2d);Check(Math.Abs(t.PlayerHealth-.65)<1e-8,"standing inside clinic square gradually heals");
   WalkPlace(t,t.PlayerPosition.X+10,t.PlayerPosition.Z);Call(t,"AmenitiesStep",2d);Check(Math.Abs(t.PlayerHealth-.65)<1e-8,"healing stops outside square");
  });
  Test("energy pickup lifecycle",()=>{
   var t=FootSandbox();t.InteractVehicle();WalkPlace(t,-20,0);t.Pickups.Add(new(){Id=900,Kind=CityPickupKind.EnergyDrink,Position=t.PlayerPosition,Created=-2,MapSpawn=true});
   Call(t,"PickupStep",.02);Check(t.EnergySeconds==25&&t.Pickups.Count==0,"drink starts a timed effect and removes the collected pickup");
   var pos=t.PlayerPosition;t.WalkInput=new(-1,0);Tick(t,1);double boosted=(t.PlayerPosition-pos).Length;t.WalkInput=default;
   Call(t,"AmenitiesStep",30d);Check(t.EnergySeconds==0,"boost expires without speeding up the whole world");
   pos=t.PlayerPosition;t.WalkInput=new(-1,0);Tick(t,1);double normal=(t.PlayerPosition-pos).Length;
   Check(boosted>normal*1.35&&boosted<normal*1.85,"energy increases actual pedestrian displacement");
   var populated=new CityTraffic();var drink=populated.Pickups.First(p=>p.Kind==CityPickupKind.EnergyDrink);populated.Pickups.Remove(drink);Call(populated,"FillMapPickups",false);
   Check(populated.Pickups.Count(p=>p.Kind==CityPickupKind.EnergyDrink)==CityTraffic.EnergyTarget,"collected drinks replenish away from the player");
   var world=populated.CaptureWorld(new CityModel());CityTraffic.ValidateWorld(world);Check(world.WideRoads,"save records wide-road geometry explicitly");
   world.Props.Add(new(CityState.Capture(new StreetProp{Id=999999,Home=populated.FoodSites.First().Position,Kind=StreetKind.Hydrant}),null,null));
   populated.RestoreWorld(world,new CityModel());Check(populated.Props.All(p=>p.Id!=999999),"legacy prop removed for a new kiosk does not break save loading");
  });
  Test("safe lane transition and bus dwell",()=>{
   var t=FootSandbox();var c=Npc(t,0,-1.9,-24);c.I=c.J=CityTraffic.Nodes/2;c.Direction=c.Outgoing=0;c.Phase=0;c.Path=new(c.Position,new(-1.9,-11));c.Cruise=5;
   var start=c.Position;Check(t.TryChangeLane(c,1),"clear adjacent lane permits a change");
   Check((c.Position-start).Length<1e-12&&(c.Path.Sample(0).Point-start).Length<1e-12,"lane change never teleports the vehicle");
   Check(Math.Abs(c.Path.Sample(c.Path.Length).Point.X+5.25)<1e-8,"outer lane stays on the same side of the road");
   c.LaneIndex=0;c.Path=new(start,new(-1.9,-11));c.Along=0;var other=Npc(t,0,-5.25,-24);Call(t,"RebuildActorGrid");Check(!t.TryChangeLane(c,1),"occupied adjacent lane prevents merging");
   t.Cars.Remove(other);var stop=t.BusStops.First();c.Kind=5;c.LaneIndex=1;c.Direction=0;Place(c,stop.X+3.85,stop.Z);Check((bool)Call(t,"BusStop",c)!,"bus dwells beside its stop");
   Check(c.BusUntil>t.Time&&c.NextBusStop>c.BusUntil,"bus dwell is bounded and has a cooldown");
   c.BusUntil=t.Time-1;Check(!(bool)Call(t,"BusStop",c)!,"bus resumes without immediately stopping again");
  });
  Test("old layout remains compatible",()=>{CityLayout.Install(CityLayout.Generate(73921));Check(!CityLayout.WideRoads&&CityTraffic.Sidewalk==6.5,"legacy geometry still has its original pavements");});
 }
}
