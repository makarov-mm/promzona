using Promzona.Game;
using System.Text.Json;
static partial class Tests
{
    static void RunRoadLevels()
    {
        foreach(int seed in new[]{73921,1,42,91837})Test("layered layout "+seed,()=>{
            var layout=CityLayout.Generate(seed,gridSize:49,wideRoads:true,roadRevision:1);CityLayout.Install(layout);
            Check(layout.Levels.Length==2&&layout.RoadRevision==1,"new city includes both road structures");
            var reached=new HashSet<(int,int)>{(24,24)};var queue=new Queue<(int,int)>();queue.Enqueue((24,24));
            while(queue.Count>0){var (i,j)=queue.Dequeue();for(int d=0;d<4;d++)if(layout.Road(i,j,d)){
                var f=CityTraffic.Forward(d);var next=(i+(int)f.X,j+(int)f.Z);if(reached.Add(next))queue.Enqueue(next);
            }}
            Check(reached.Count==49*49,"street graph remains connected after ramp reservations");
            foreach(var s in layout.Levels){
                double maxSlope=0,minClear=100;
                for(double x=s.X-144;x<s.X+144;x+=.5){
                    maxSlope=Math.Max(maxSlope,Math.Abs(s.CentreFloor(x+.5)-s.CentreFloor(x))*2);
                    if(Math.Abs(x-s.X)<53)minClear=Math.Min(minClear,s.Tunnel?CityTerrain.Ground(x,s.CentreZ(x))-s.CentreFloor(x)-4.9:s.CentreFloor(x)-CityTerrain.Ground(x,s.CentreZ(x))-.55);
                }
                Console.WriteLine($"LEVEL {seed}/{s.Id}: grade {maxSlope:F3}, clearance {minClear:F3}");
                Check(maxSlope<.4,"road grade stays below 40 percent including terrain");
                Check(minClear>(s.Tunnel?1:5),"roof cover / lower road clearance retained");
                Check(!CityRoadLevels.Solid(s.X,s.CentreFloor(s.X)+2,s.Z+18,.1),"driving volume has no terrain collision");
                Check(CityRoadLevels.Solid(s.X,s.CentreFloor(s.X)+.8,s.Z+23.75,.3),"side barrier is solid to camera and rays");
                Check(CityRoadLevels.Select(s.X,s.Z+18,s.Id,s.CentreFloor(s.X))==s.Id,"actor retains its layer below or above district");
                Check(CityRoadLevels.Select(s.X,s.Z+18,0,CityTerrain.Ground(s.X,s.Z+18))==0,"surface actor is not teleported onto another level");
                Check(Math.Abs(s.CentreFloor(s.X-144)-CityTerrain.Ground(s.X-144,s.Z))<.1,"ramp joins existing road height");
                foreach(int end in new[]{-1,1}){
                    double x=s.X+end*144,error=0;
                    for(double z=s.Z-5.8;z<=s.Z+5.8;z+=.2)error=Math.Max(error,Math.Abs(s.Floor(x,z)-CityTerrain.Ground(x,z)));
                    Check(error<.02,"full road width joins terrain without a transverse hole");
                }
                Check(s.Route(true).ExportPoints().Length<=300,"connector route fits bounded save schema");
                Check(CityModel.Blocks.All(b=>!s.Reserve(b.X,b.Z,Math.Max(b.HalfX,b.HalfZ))),"buildings avoid exposed ramps");
            }
        });
        Test("height-aware contacts and possession",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:1));
            var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();t.Cash.Clear();
            foreach(var s in CityRoadLevels.All){
                double x=s.X,z=s.Z+18;
                Place(t.Hero,x,z);t.Hero.RoadLevel=s.Id;t.UpdateTerrainSupport(t.Hero);
                var c=Npc(t,0,x,z);c.Id=900+c.Id;c.RoadLevel=0;t.UpdateTerrainSupport(c);c.Driverless=true;
                Call(t,"RefreshLevelBodies");
                Check(!(bool)Call(t,"HeightContact",t.Hero.Body,c.Body)!,"cars at same XZ on different roads do not collide");
                c.RoadLevel=s.Id;t.UpdateTerrainSupport(c);Call(t,"RefreshLevelBodies");
                Check((bool)Call(t,"HeightContact",t.Hero.Body,c.Body)!,"cars on the same level still collide");
                t.World.RemoveBody(t.World.Bodies.IndexOf(c.Body));t.Cars.Remove(c);
                t.Hero.Speed=0;t.Hero.Body.Vx=t.Hero.Body.Vy=0;
                Check(t.InteractVehicle()&&t.OnFoot,"exit vehicle on elevated / underground surface");
                Check(Math.Abs(CityRoadLevels.Ground(t.Player)-t.Hero.SurfaceHeight)<.2,"person stands beside car on same surface");
                Tick(t,.5);Check(t.NearbyEnterableCar()==t.Hero,"vehicle can be selected on the same layer");
                Check(t.InteractVehicle(),"start reentry on layered road");Tick(t,2);
                Check(!t.OnFoot,"complete reentry on layered road");Tick(t,.5);
            }
        });
        Test("connector traversal and saved layer",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:1));
            var m=new CityModel();var t=m.Traffic;
            foreach(var s in CityRoadLevels.All){
                Place(t.Hero,s.X,s.Z+20);t.Hero.RoadLevel=s.Id;t.UpdateTerrainSupport(t.Hero);
                var c=t.Cars[1];t.BeginLevelRoute(c,s.Id,true);c.Along=120;var pose=c.Path.Sample(120);Place(c,pose.Point.X,pose.Point.Z);c.Heading=pose.Heading;c.Body.Angle=-pose.Heading;t.UpdateTerrainSupport(c);
                var state=JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(t.CaptureWorld(m),CityState.Json),CityState.Json)!;
                CityTraffic.ValidateWorld(state);t.RestoreWorld(state,m);
                Check(t.Hero.RoadLevel==s.Id&&Math.Abs(t.Hero.SurfaceHeight-s.CentreFloor(s.X))<.1,"save restores road level and support height");
                Check(t.Cars[1].Phase==3&&t.Cars[1].Path.ExportPoints().Length==289,"save restores unfinished connector route");
            }
        });
        Test("driving both directions and lower road",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:1));
            var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();
            foreach(var s in CityRoadLevels.All)foreach(bool east in new[]{true,false}){
                var path=s.Route(east);var first=path.Sample(1);Place(t.Hero,first.Point.X,first.Point.Z);t.Hero.Heading=first.Heading;t.Hero.Body.Angle=-first.Heading;
                t.Hero.RoadLevel=0;t.Hero.Damage=0;t.Hero.Roll=t.Hero.Pitch=t.Hero.Lift=0;t.UpdateTerrainSupport(t.Hero);
                double progress=0;int frames=0;bool entered=false;
                for(;frames<4800&&progress<path.Length-3;frames++){
                    progress=path.ClosestDistance(t.Hero.Position);entered|=t.Hero.RoadLevel==s.Id;
                    var delta=path.Sample(progress+5).Point-t.Hero.Position;var f=new CityPoint(Math.Sin(t.Hero.Heading),Math.Cos(t.Hero.Heading));var right=new CityPoint(f.Z,-f.X);
                    double steer=Math.Atan(t.Hero.Wheelbase*2*CityPoint.Dot(delta,right)/Math.Max(1,delta.Length*delta.Length))/.68;
                    t.Step(1d/60,new(Math.Clamp((7-t.Hero.Speed)*.5,0,1),Math.Clamp(steer,-1,1),t.Hero.Speed>8));
                }
                Console.WriteLine($"DRIVE level {s.Id}/{east} progress {progress:F2}/{path.Length:F2}, damage {t.Hero.Damage:F3}, layer {t.Hero.RoadLevel}, pos {t.Hero.Position}");
                Check(entered&&progress>path.Length-3,"physical car traverses complete connector");
                Check(t.Hero.Damage<.15&&Math.Abs(t.Hero.Roll)<.3,"ramp has no collision step or rollover");
            }
            var bridge=CityRoadLevels.All[0];Place(t.Hero,bridge.X,bridge.Z-12);t.Hero.RoadLevel=0;t.Hero.Damage=0;t.UpdateTerrainSupport(t.Hero);
            for(int n=0;n<650;n++)t.Step(1d/60,new(t.Hero.Speed<6?1:0,0,t.Hero.Speed>7));
            Check(t.Hero.Position.Z>bridge.Z+35&&t.Hero.RoadLevel==0,"lower street passes beneath bridge without layer snapping");
            Check(t.Hero.Damage<.1,"bridge supports stay clear of lower carriageway");
        });
        Test("far traffic and supplies respect road levels",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:1));
            var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();t.Cash.Clear();
            foreach(var s in CityRoadLevels.All){
                Place(t.Hero,0,-500);t.Hero.RoadLevel=0;t.UpdateTerrainSupport(t.Hero);
                var first=s.Route(true).Sample(0);var c=Npc(t,0,first.Point.X,first.Point.Z);c.Id=90011;c.Cruise=7;
                c.Heading=first.Heading;c.Body.Angle=-first.Heading;t.BeginLevelRoute(c,s.Id,true);
                for(int n=0;n<800&&c.Phase==3;n++)t.Step(.1,new(0,0,true));
                Check(c.Phase!=3&&c.Position.X>s.X+140,"far rule traffic traverses connector and rejoins street graph");
                Check(!c.Physical,"distant connector traffic stays outside local physics");
                t.World.RemoveBody(t.World.Bodies.IndexOf(c.Body));t.Cars.Remove(c);
                Place(t.Hero,s.X,s.Z+20);t.Hero.RoadLevel=s.Id;t.UpdateTerrainSupport(t.Hero);t.Hero.Damage=.6;t.Player.GunDamage=.6;
                var at=t.Hero.Position;
                t.Pickups.Clear();t.Pickups.Add(new(){Id=888,Kind=CityPickupKind.Pills,Position=at,Created=-2,RoadLevel=0});
                Call(t,"PickupStep",.01);Check(t.Pickups.Any(p=>p.Id==888)&&t.Hero.Damage==.6,"surface supplies cannot be collected through a deck or tunnel roof");
                t.Pickups.First(p=>p.Id==888).RoadLevel=s.Id;Call(t,"PickupStep",.01);
                Check(t.Pickups.All(p=>p.Id!=888)&&t.Player.GunDamage<.6&&t.Hero.Damage==.6,"supplies on the actual road surface can be collected");
                Check(!(bool)Call(t,"ClearLevelSight",at,s.Id,at,0)!,"blast and NPC visibility do not cross solid road layers");
            }
            var tunnel=CityRoadLevels.All[1];double x=tunnel.X-90,z=tunnel.CentreZ(x);
            Check(!CityRoadLevels.GroundWalkEdge(new(x,z-10),new(x,z+10)),"ground pedestrian routes cannot cross an open excavation");
            Check(CityRoadLevels.GroundWalkEdge(new(tunnel.X,tunnel.Z+8),new(tunnel.X,tunnel.Z+28)),"surface pedestrians can cross the covered district");
        });
        CityLayout.Install(CityLayout.Generate(73921));
        Check(CityRoadLevels.All.Count==0&&CityLayout.Current.RoadRevision==0,"legacy layout does not acquire new structures");
    }
}

