using System.Reflection;
using Promzona.Game;
static partial class Tests
{
    // Guards for the 28.10.2026 review: every pruned query must equal its full scan.
    // 29.10.2026: this archive holds the review's tests but not its sources. The guards
    // whose subject exists in the tree (FurnitureNear, CarClearNear, nearest clinic,
    // abandoned cars, bed settling, stray bullets) are kept and wired to --review; the
    // two that need the layout version / eight clinics / the _detourBudgetTime field are
    // withdrawn until that delta lands (see Docs/PROMZONA-AUDIT-2026-10-29.md).
    static void RunReview(){
        Test("furniture buckets match full scans",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true));var layout=CityLayout.Current;
            var rng=new Random(5);int mismatches=0,hits=0;
            for(int n=0;n<40000;n++){
                // Half of the probes land beside an item, so both outcomes are exercised.
                double x,z;
                if(n%2==0){var f=layout.Furniture[rng.Next(layout.Furniture.Length)];x=f.X+rng.NextDouble()*3-1.5;z=f.Z+rng.NextDouble()*3-1.5;}
                else{x=(rng.NextDouble()*2-1)*CityTraffic.LandEdge;z=(rng.NextDouble()*2-1)*CityTraffic.LandEdge;}
                foreach(double margin in new[]{.4,.8}){
                    bool scan=layout.Furniture.Any(b=>Math.Abs(b.X-x)<b.HalfX+margin&&Math.Abs(b.Z-z)<b.HalfZ+margin);
                    if(scan)hits++;if(scan!=layout.FurnitureNear(x,z,margin))mismatches++;
                }
            }
            Check(mismatches==0&&hits>1000,$"bucketed furniture query equals brute force ({hits} hits)");
        });
        Test("grid right-of-way check equals full scan",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true));var t=new CityTraffic();
            for(int k=0;k<20;k++)t.Step(1d/60);
            var full=typeof(CityTraffic).GetMethod("CarClear",BindingFlags.Instance|BindingFlags.NonPublic)!;
            var near=typeof(CityTraffic).GetMethod("CarClearNear",BindingFlags.Instance|BindingFlags.NonPublic)!;
            int differ=0,blocked=0;
            for(int n=0;n<400;n++){var c=t.Cars[n];var other=t.Cars[(n+1)%t.Cars.Count];
                for(int k=0;k<4;k++){
                    object[] args=[c.Position+new CityPoint(Math.Sin(k*1.7),Math.Cos(k*1.7))*(k*2.5),c.Heading,other];
                    bool a=(bool)full.Invoke(t,args)!,b=(bool)near.Invoke(t,args)!;if(!a)blocked++;if(a!=b)differ++;
                }}
            Check(differ==0&&blocked>100,$"CarClearNear equals CarClear after stepping ({blocked} blocked probes)");
        });
        Test("respawn uses the nearest clinic",()=>{
            CityLayout.Install(CityLayout.Generate(73921));
            foreach(var target in CityTraffic.ClinicBlocks){
                var t=FootSandbox();t.InteractVehicle();var door=CityAmenities.ClinicEntrance(target.I,target.J);
                WalkPlace(t,door.X+6,door.Z+6);t.Player.Dead=true;t.Player.GunDamage=1;
                Check((bool)Call(t,"PlaceAtClinic")!&&(t.PlayerPosition-door).Length<1.6,$"death beside clinic {target} respawns at that clinic");
            }
        });
        Test("abandoned vehicles leave only out of view",()=>{
            CityLayout.Install(CityLayout.Generate(73921));
            var t=FootSandbox();t.SpawnDistance=245;
            var near=Npc(t,0,30,0);near.Driverless=true;
            var far=Npc(t,0,400,0);far.Driverless=true;
            var duty=Npc(t,3,-400,0);duty.Driverless=true;t.Services.Add(new ServiceMission{CarId=duty.Id,Kind=CityService.Police});
            var moving=Npc(t,0,0,400);
            Call(t,"VehicleLifeAfter",.01);
            Check(t.Cars.Contains(near)&&!t.Cars.Contains(far)&&t.Cars.Contains(duty)&&t.Cars.Contains(moving)&&t.Cars.Contains(t.Hero),"only distant ownerless parked cars are cleaned up");
        });
        Test("sunken car settles on the bed",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49));var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();t.Services.Clear();
            double river=CityTraffic.Coordinate(16)+18;Place(t.Hero,river,18);t.UpdateTerrainSupport(t.Hero);
            for(int k=0;k<600;k++)Call(t,"VehicleLifeBefore",1d/60);
            double bed=CityTerrain.Current.BedHeight(t.Hero.Body.X,t.Hero.Body.Y);
            Check(t.Hero.Falling&&Math.Abs(t.Hero.GroundHeight+t.Hero.Lift-bed)<1e-9&&t.Hero.VerticalVelocity==0,"car rests on the canal bed instead of sinking forever");
            CityLayout.Install(CityLayout.Generate(73921));
        });
        Test("stray NPC bullets hit bystanders",()=>{
            // Away from the parked car at the origin, which would stop the ray first.
            var t=FootSandbox();t.InteractVehicle();WalkPlace(t,20,20);
            var shooter=Person(t,20,0);shooter.Persona=5;var bystander=Person(t,20,10);Call(t,"RebuildActorGrid");
            var pistol=CityWeapons.Find(CityWeaponId.Pistol)!;
            for(int k=0;k<3;k++)typeof(CityTraffic).GetMethod("FireRay",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(t,[shooter.Position,new CityPoint(0,1),pistol,false,1.2,shooter,false]);
            Check(bystander.Dead&&t.PlayerHealth==1&&t.Wanted==0,"the first person in the line of fire is hit; no crime is charged to the player");
        });
    }
}
