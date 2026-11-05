using Promzona.Game;
using System.Text.Json;
static partial class Tests {
 static void RunPromzona(){
  Test("extended island and river routing",()=>{
   var layout=CityLayout.Generate(73921,gridSize:49);CityLayout.Install(layout);
   Check(CityTraffic.Nodes==49&&CityTraffic.LandEdge>900,"new city has 49 nodes per side and more than double the former area");
   var seen=new HashSet<int>{0};var q=new Queue<int>();q.Enqueue(0);int crossings=0;
   while(q.Count>0){int a=q.Dequeue(),i=a%49,j=a/49;for(int d=0;d<4;d++)if(CityTraffic.HasRoad(i,j,d)){
     int ni=i+(d==1?1:d==3?-1:0),nj=j+(d==0?1:d==2?-1:0);if(ni<0||nj<0||ni>=49||nj>=49)continue;
     if(ni!=i&&CityLayout.CanalColumn(Math.Min(i,ni))){Check(CityLayout.BridgeRow(j),"river road exists only at a bridge");crossings++;}
     if(seen.Add(ni+49*nj))q.Enqueue(ni+49*nj);
   }}
   Check(seen.Count==49*49&&crossings>0,"all districts remain connected across both canals");
   double river=CityTraffic.Coordinate(16)+18;
   Check(CityWater.Depth(new(river,18))>4,"canal has an actual submerged bed");
   Check(CityWater.Depth(new(river,0))==0&&CityTerrain.Ground(river,0)>3.5,"bridge supports actors over the river");
   Check(CityTerrain.Ground(river,0)>CityTerrain.Ground(river-18,0)+1,"bridge deck rises smoothly to its middle");
   Check(Math.Abs(CityTerrain.Ground(river-18,0)-CityTerrain.Ground(river-18.01,0))<.03,"bridge entry meets the bank without a vertical step");
   Check(CityWater.Depth(new(CityTraffic.LandEdge-35,50))==0&&CityWater.Depth(new(CityTraffic.LandEdge-3,50))>.8,"beach slopes from dry sand into swimmable then drowning depth");
   Check(layout.Terrain.Maximum-layout.Terrain.Minimum>40,"new landscape has broad hills and depressions");
   Check(CityModel.Blocks.All(b=>Math.Abs(layout.Terrain.Height(b.X-b.HalfX,b.Z-b.HalfZ)-layout.Terrain.Height(b.X+b.HalfX,b.Z+b.HalfZ))<.05),"building footprint corners remain level");
  });
  Test("crew and survival",()=>{
   var t=FootSandbox();var c=Npc(t,7,15,0);c.Id=42;t.AddCrime(5);
   Check(c.OccupantCount==4&&c.Mass==4200,"guard van has four occupants and 4.2 tonne mass");
   var mission=new ServiceMission{CarId=c.Id,Kind=CityService.Police};t.Services.Add(mission);
   Check(t.DeployCrew(c,mission,false)==4&&c.OccupantCount==0&&c.Driverless,"all guards exit exactly once");
   Check(t.DeployCrew(c,mission,false)==0&&mission.Medics.Count==4,"repeat dispatch cannot duplicate crew");
   Check(t.Pedestrians.Count(p=>p.Persona==6&&p.Armed&&p.Weapon==CityWeaponId.Automatic)==4,"guards have their own role and automatic weapons");
   Check(t.Pedestrians.All(p=>t.Pedestrians.All(other=>p==other||(p.Position-other.Position).Length>.7)),"exit points are reserved without overlapping people");
   var bobik=Npc(t,6,-15,0);bobik.Id=43;Check(bobik.OccupantCount==2&&t.DeployCrew(bobik,null,false)==2,"bobik carries two militia officers");
   t.InteractVehicle();double before=t.PlayerHealth;t.DamagePlayer(.5);Check(Math.Abs(t.PlayerHealth-(before-.1))<1e-9,"foot gun health is five times the previous value");
   t.DamagePlayer(10);Check(!t.World.Bodies.Contains(t.Player.Body),"dead player is non-solid");
  });
  Test("river consequences and beach actors",()=>{
   var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();t.Services.Clear();
   double river=CityTraffic.Coordinate(16)+18;Place(t.Hero,river,18);t.UpdateTerrainSupport(t.Hero);
   Call(t,"VehicleLifeBefore",.05);
   Check(t.Hero.Falling&&!t.Hero.Destroyed&&!t.Hero.Burning&&t.Splashes.Count>0,"car enters water with splash, never an explosion");
   var p=Person(t,river,20);p.Physical=true;Call(t,"WaterStep",.1);
   Check(p.Dead&&p.Drowned&&!t.World.Bodies.Contains(p.Body),"drowned person leaves the collision world immediately");
   Check(t.Marks.All(m=>!m.Blood),"water death creates no blood decal on water");
   Tick(t,3.5);Check(t.Respawns==1&&t.OnFoot&&!t.PlayerUnavailable&&t.Wanted==0,"drowning respawns at clinic and resets pursuit");
   var m=new CityModel();Check(m.Traffic.Pedestrians.Count(p=>p.Persona==11)>100,"beach is populated from the first frame");
   var old=m.Traffic.CaptureWorld(m);CityTraffic.ValidateWorld(old);Check(CityTraffic.SavedGridSize(old)==49,"save records larger layout through reservation dimensions");
   var json=JsonSerializer.Serialize(old,CityState.Json);Check(json.Length<90_000_000,"new full city save stays within declared load cap");
   var walker=m.Traffic.Pedestrians.First(p=>!p.IsPlayer&&p.Persona!=11);walker.Persona=7;walker.Position=new(850,850);walker.Body.X=850;walker.Body.Y=850;
   m.Traffic.RolesStep(5,9);Check(!m.Traffic.Pedestrians.Contains(walker),"night worker disappears at dawn only outside visible area");
  });
  Test("cancel preserves old world dimension",()=>{
   CityLayout.Install(CityLayout.Generate(21));var old=CityLayout.Current;using var cancel=new CancellationTokenSource();cancel.Cancel();
   bool stopped=false;try{CityLayout.Generate(22,cancel:cancel.Token,gridSize:49);}catch(OperationCanceledException){stopped=true;}
   Check(stopped&&CityLayout.GridSize==33&&CityLayout.Current==old,"cancelled larger generation preserves installed layout and dimension");
  });
  Test("police stops, exits and attacks on foot",()=>{
   var t=FootSandbox();Place(t.Hero,-1.8,-15);t.InteractVehicle();WalkPlace(t,-1.8,-7);t.ConsoleWanted(4);
   var c=Npc(t,6,-1.8,-23);c.Id=42;c.Speed=0;c.Body.Vx=c.Body.Vy=0;
   var mission=new ServiceMission{CarId=c.Id,Kind=CityService.Police};t.Services.Add(mission);
   Call(t,"ServicesStep",.1);
   Check(c.Driverless&&mission.Medics.Count==2,"militia exits a stopped responder near a wanted pedestrian");
   double hp=t.PlayerHealth;for(int n=0;n<180;n++)t.Step(1d/60);
   Check(t.ShotsFired>0&&t.PlayerHealth<hp,"deployed police aims and damages wanted foot player");
   var fire=Npc(t,3,0,-40);fire.Id=43;fire.Burning=true;fire.Damage=.7;fire.Speed=0;
   Call(t,"VehicleLifeBefore",.01);Check(fire.Driverless&&fire.CrewDeployed&&fire.Occupants==0,"stopped burning police evacuates its crew");
  });
  Test("water shot and stable preview",()=>{
   CityLayout.Install(CityLayout.Generate(73921,gridSize:49));var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();
   double river=CityTraffic.Coordinate(16)+18;Place(t.Hero,river-24,23);t.UpdateTerrainSupport(t.Hero);Check(t.InteractVehicle(),"water-shot fixture exits on the bank");WalkPlace(t,river-CityWater.BankHalf-.6,23);
   t.Inventory.Give(CityWeaponId.Pistol);t.PlayerViewHeading=Math.PI/2;t.PlayerViewPitch=Math.Atan2(CityWater.Level-CityTerrain.Ground(t.PlayerPosition)-1.32,CityWater.BankHalf+.6);
   int ammo=t.Inventory.Ammo(CityWeaponId.Pistol),shots=t.PlayerShotsFired;
   var hit=t.AimHit();t.AimHit();Console.WriteLine($"water aim foot={t.OnFoot} player={t.PlayerPosition} ground={CityTerrain.Ground(t.PlayerPosition)} hit={hit} wet={CityWater.Wet(hit.Point)}");Check(t.Inventory.Ammo(CityWeaponId.Pistol)==ammo&&t.PlayerShotsFired==shots,"reticle preview neither consumes ammo nor emits a shot");
   Check(CityWater.Wet(hit.Point),"downward aim resolves a visible water intersection");
   t.PlayerFireHeld=true;Call(t,"PlayerCombat");Check(t.Splashes.Count>0,"a bullet hitting water creates a splash");
  });
  CityLayout.Install(CityLayout.Generate(73921));
 }
}
