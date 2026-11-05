using Promzona.Game;
static partial class Tests
{
    static void RunAirPatrol()
    {
        Test("open tunnel has no phantom ground",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:4));
            foreach(var road in CityRoadLevels.All.Where(s=>s.Tunnel))foreach(int kind in new[]{0,2,5}){
                var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();
                var c=t.Hero;c.Kind=kind;double x=road.X-85,z=road.CentreZ(x);
                Place(c,x,z);c.TerrainActive=true;c.GroundHeight=CityTerrain.Ground(x,z);c.RoadLevel=0;c.Lift=0;c.VerticalVelocity=0;
                double worldY=c.GroundHeight;t.UpdateTerrainSupport(c);
                Check(c.RoadLevel==road.Id&&c.Lift>1,"open descent selects lower road for vehicle "+kind);
                Check(Math.Abs(c.SurfaceHeight+c.Lift-worldY)<.001,"support loss preserves absolute vehicle altitude");
                double once=c.Lift;t.UpdateTerrainSupport(c,true);Check(Math.Abs(c.Lift-once)<1e-8,"repeated support update cannot accumulate lift");
                for(int n=0;n<240;n++)t.Step(1d/60,new(0,0,true));
                Check(c.Lift<.08&&Math.Abs(c.SurfaceHeight-road.Floor(c.Position.X,c.Position.Z))<.4,"vehicle lands on tunnel ramp instead of hovering");
                double upper=CityTerrain.Ground(road.X,road.Z);
                Check(CityRoadLevels.Select(road.X,road.Z,0,upper)==0,"covered roof retains upper surface");
            }
        });
        Test("downward rays hit roofs",()=>{
            double d=CityTraffic.RayPrism(new(-20,0),new(1,0),30,-1,new(0,0),0,10,10,0,20,50);
            Check(Math.Abs(d-10)<1e-9,"3D ray enters roof at upper face");
            d=CityTraffic.RayPrism(new(-20,0),new(1,0),30,-.7,new(0,0),0,10,10,0,20,50);
            Check(Math.Abs(d-10/.7)<1e-9,"roof entry inside horizontal footprint is not missed");
            Check(CityTraffic.RayPrism(new(-20,0),new(1,0),30,0,new(0,0),0,10,10,0,20,50)==50,"ray above building remains unobstructed");
        });
        Test("sixth wanted level and single-shot aircraft",()=>{
            var t=FootSandbox();t.SpawnDistance=100;t.ConsoleWanted(6);Call(t,"DispatchAirPatrol");
            var c=t.Cars.Single(c=>c.AirPatrol);int id=c.Id;
            Check(CityTraffic.PoliceRoster(6).Length==9&&CityTraffic.PoliceRoster(6).Count(k=>k==12)==1,"sixth roster adds exactly one helicopter");
            Check(CityTraffic.WantedPoints(6)-CityTraffic.WantedPoints(5)>CityTraffic.WantedPoints(5)-CityTraffic.WantedPoints(4),"sixth wanted interval is more expensive");
            var initial=c.Position;for(int n=0;n<160;n++){Clock(t,t.Time+.1);Call(t,"AirPatrolStep",.1);}
            Check((c.Position-initial).Length>80&&(c.Position-t.PlayerPosition).Length<60,"aircraft closes distance and orbits");
            Check(!c.Physical&&!t.World.Bodies.Contains(c.Body)&&c.Lift>20,"aircraft avoids road collision proxies");
            Place(c,t.PlayerPosition.X+30,t.PlayerPosition.Z);c.Lift=32;c.AirSurveyAt=t.Time+100;c.AirSafeHeight=32;c.AirShotAt=t.Time;
            c.AirShots=0;t.Hero.Damage=0;t.Hero.Lift=0;Call(t,"RebuildActorGrid");
            int last=0;double lastAt=-100,min=100;
            for(int n=0;n<60;n++){
                Clock(t,t.Time+.1);Call(t,"AirPatrolStep",.1);
                if(c.AirShots>last){Check(c.AirShots==last+1,"one projectile per firing event");min=Math.Min(min,t.Time-lastAt);lastAt=t.Time;last=c.AirShots;}
            }
            Check(c.AirShots>=3&&c.AirShots<=4&&min>=1.59,"patrol fires spaced single shots");
            Check(t.Hero.Damage>0,"downward rounds damage vehicle");
            t.ConsoleWanted(5);Call(t,"ServicesStep",.1);Call(t,"AirPatrolStep",.1);
            Check(c.AirDeparting&&!t.Services.Any(m=>m.CarId==id),"lower wanted level retires aircraft");
            t.ConsoleWanted(6);Call(t,"DispatchAirPatrol");Check(t.Cars.Count(v=>v.AirPatrol)==1&&!c.AirDeparting,"renewed chase reuses returning patrol");
            t.GodMode=true;var before=c.Position;
            for(int n=0;n<120;n++)t.Step(1d/60);
            Check((c.Position-before).Length>1&&!c.Physical&&double.IsFinite(c.Lift+c.Position.X),"normal simulation keeps air pursuit moving without road solver");
            t.ConsoleWanted(0);Call(t,"ServicesStep",.1);
            Place(c,600,600);Call(t,"VehicleLifeAfter",.1);
            Check(!t.Cars.Contains(c),"retired distant helicopter is removed");
        });
        Test("expanded population saves and migration",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:4));
            var m=new CityModel();var t=m.Traffic;
            Console.WriteLine($"POP scooters {t.Scooters.Count}, animals {t.Animals.Count}, birds {t.InitialPigeonTarget}/{t.PigeonTarget}, other {t.OtherAnimalTarget}");
            Check(t.Scooters.Count==192&&t.Scooters.Count(s=>s.RiderId!=0)>=180,"new city has twice the scooter rider target");
            Check(t.AnimalPopulationRevision==2&&t.Animals.Count>900&&CityTraffic.AnimalCap==2000,"animal population doubled within new cap");
            t.ConsoleWanted(6);for(int n=0;n<25;n++)Call(t,"PoliceDispatchStep",1d);
            Check(t.Services.Count(s=>s.Kind==CityService.Police)==9&&t.Cars.Count(c=>c.AirPatrol)==1,"real city dispatch fills sixth-level roster");
            var save=t.CaptureWorld(m);CityTraffic.ValidateWorld(save);int target=t.PigeonTarget,other=t.OtherAnimalTarget;
            t.RestoreWorld(save,m);Check(t.PigeonTarget==target&&t.Cars.Count(c=>c.AirPatrol)==1,"current save retains targets and airborne actor");
            var patrol=t.Cars.Single(c=>c.AirPatrol);
            var tunnel=CityRoadLevels.All.First(r=>r.Tunnel);
            Place(t.Hero,tunnel.X,tunnel.CentreZ(tunnel.X));t.Hero.RoadLevel=tunnel.Id;t.Hero.GroundHeight=tunnel.Floor(t.Hero.Position.X,t.Hero.Position.Z);t.Hero.TerrainActive=true;t.Hero.Lift=0;
            Place(patrol,t.Hero.Position.X+20,t.Hero.Position.Z);patrol.GroundHeight=CityTerrain.Ground(patrol.Position);patrol.TerrainActive=true;patrol.Lift=35;
            Call(t,"RebuildActorGrid");
            Check(!(bool)Call(t,"AirPatrolSight",patrol)!,"tunnel roof blocks helicopter fire");
            var roof=CityLayout.Current.Blocks.First(b=>b.Height>15);
            double clearance=(double)Call(t,"AirClearance",new CityPoint(roof.X,roof.Z),new CityPoint(roof.X+1,roof.Z))!;
            Check(clearance>=CityTerrain.Ground(roof.X,roof.Z)+roof.Height+12,"air navigation maintains clearance above buildings");
            patrol.Destroyed=true;Call(t,"ServicesStep",.1);Call(t,"PoliceDispatchStep",3d);
            Check(t.Cars.Count(c=>c.AirPatrol&&!c.Destroyed)==1,"destroyed air patrol is replaced without duplicate live aircraft");
            save.Clock.Remove("AnimalPopulationRevision");save.Clock["<PigeonTarget>k__BackingField"]=target/2;save.Clock["<OtherAnimalTarget>k__BackingField"]=other/2;
            t.RestoreWorld(save,m);Check(t.PigeonTarget==target&&t.OtherAnimalTarget==other,"legacy save migrates animal targets once");
            var again=t.CaptureWorld(m);t.RestoreWorld(again,m);Check(t.PigeonTarget==target&&t.OtherAnimalTarget==other,"repeat save/load does not double population again");
        });
    }
}
