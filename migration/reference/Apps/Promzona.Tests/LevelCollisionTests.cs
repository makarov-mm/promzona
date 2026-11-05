using Promzona.Game;
using MakarovPhysics.RigidBodies2D;
using System.Reflection;
static partial class Tests
{
    static void RunLevelCollisions()
    {
        Test("surface above tunnel walls",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:1));
            var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();
            var s=CityRoadLevels.All.First(r=>r.Tunnel);double x=s.X,z=s.CentreZ(x)+5.68;
            Place(t.Hero,x,z);t.Hero.RoadLevel=0;t.UpdateTerrainSupport(t.Hero);Call(t,"RefreshLevelBodies");
            var walls=(Dictionary<Body2D,(double Low,double High)>)typeof(CityTraffic).GetField("_levelStatics",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(t)!;
            var wall=walls.Keys.OrderBy(b=>Math.Abs(b.X-x)+Math.Abs(b.Y-z)).First();
            Check(!(bool)Call(t,"HeightContact",t.Hero.Body,wall)!,"buried tunnel wall does not hit surface vehicle");
            Check(!CityRoadLevels.Solid(x,CityTerrain.Ground(x,z)+.5,z,.1),"covered tunnel wall does not block surface camera or bullets");
            t.Hero.RoadLevel=s.Id;t.UpdateTerrainSupport(t.Hero);Call(t,"RefreshLevelBodies");
            Check((bool)Call(t,"HeightContact",t.Hero.Body,wall)!,"same tunnel wall still blocks underground vehicle");
        });
        Test("short scenery below elevated car",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:1));
            var t=QuietCity();var road=CityRoadLevels.All.First(r=>!r.Tunnel);
            var scenery=(List<Body2D>)typeof(CityTraffic).GetField("_scenery",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(t)!;
            var lamp=scenery.Where(b=>b.Shape==Shape2D.Circle&&b.Radius<.19).OrderBy(b=>Math.Abs(b.X-road.X)+Math.Abs(b.Y-road.Z)).First();
            Place(t.Hero,lamp.X,lamp.Y);t.Hero.RoadLevel=road.Id;t.Hero.GroundHeight=CityTerrain.Ground(lamp.X,lamp.Y)+9;
            Call(t,"RefreshLevelBodies");
            Check(!(bool)Call(t,"HeightContact",t.Hero.Body,lamp)!,"short lamp does not become a hundred-metre invisible column");
        });
        foreach(int seed in new[]{73921,1,42,91837})Test("frequent structures "+seed,()=>{
            var layout=CityLayout.Generate(seed,gridSize:49,wideRoads:true,roadRevision:2);CityLayout.Install(layout);
            Check(layout.RoadRevision==2&&layout.Levels.Length==9,"new revision has nine separated structures");
            Check(layout.Levels.Count(s=>s.Tunnel)==4,"four tunnels and five overpasses");
            foreach(var road in layout.Levels){
                double slope=0,clear=100;
                for(double x=road.X-144;x<road.X+144;x+=.5){
                    slope=Math.Max(slope,Math.Abs(road.CentreFloor(x+.5)-road.CentreFloor(x))*2);
                    if(Math.Abs(x-road.X)<53)clear=Math.Min(clear,road.Tunnel?CityTerrain.Ground(x,road.CentreZ(x))-road.CentreFloor(x)-4.9:road.CentreFloor(x)-CityTerrain.Ground(x,road.CentreZ(x))-.55);
                }
                Console.WriteLine($"NEW LEVEL {seed}/{road.Id} slope {slope:F4} clearance {clear:F4}");
                Check(slope<.4&&clear>(road.Tunnel?1:5),"new structure retains drivable grade and overhead clearance");
                Check(layout.Levels.All(o=>o==road||Math.Abs(o.X-road.X)>290||Math.Abs(o.Z-road.Z)>40),"level footprints do not overlap");
            }
            var reached=new HashSet<(int,int)>{(24,24)};var queue=new Queue<(int,int)>();queue.Enqueue((24,24));
            while(queue.Count>0){var (i,j)=queue.Dequeue();for(int d=0;d<4;d++)if(layout.Road(i,j,d)){
                var f=CityTraffic.Forward(d);var next=(i+(int)f.X,j+(int)f.Z);if(reached.Add(next))queue.Enqueue(next);
            }}
            Check(reached.Count==49*49,"denser structures preserve connected city graph");
        });
        Test("traffic uses all structures and continuous entries",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:2));
            var model=new CityModel();var t=model.Traffic;
            Check(t.Cars.Count(c=>c.Phase==3)==72,"existing ambient population seeds eight vehicles per connector");
            foreach(var road in CityRoadLevels.All){
                Check(t.Cars.Count(c=>c.Phase==3&&c.RoadLevel==road.Id)==8,"each structure starts with two-way traffic");
                foreach(bool east in new[]{true,false})foreach(int from in new[]{0,1,2,3})foreach(int lane in new[]{0,1}){
                    int outgoing=east?1:3;if(from==(outgoing+2)%4)continue;
                    var c=t.Cars[1];c.I=road.I+(east?-4:4);c.J=road.J;c.Direction=from;c.Outgoing=outgoing;c.LaneIndex=lane;
                    var crossing=(CityPath)Call(t,"CrossingPath",c)!;var end=crossing.Sample(crossing.Length);c.Position=end.Point;
                    Check((bool)Call(t,"TryLevelRoute",c)!&&c.Phase==3,"cars can enter connector from straight or turning approach");
                    var joined=c.Path.Sample(c.Along);
                    Check((end.Point-joined.Point).Length<.03&&Math.Abs(Math.IEEERemainder(end.Heading-joined.Heading,Math.Tau))<.08,"outer lane joins curved ramp with continuous position and heading");
                }
            }
            var state=t.CaptureWorld(model);CityTraffic.ValidateWorld(state);
            Check(state.RoadRevision==2,"save retains new generation revision");
        });
        Test("physical surface crossing above covered tunnel",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:2));
            var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();t.Cash.Clear();
            foreach(var road in CityRoadLevels.All.Where(s=>s.Tunnel)){
                // The north/south street at the centre crosses both buried walls.
                Place(t.Hero,road.X+CityTraffic.Lane,road.Z+7);t.Hero.RoadLevel=0;t.Hero.Damage=0;t.Hero.Lift=t.Hero.Roll=t.Hero.Pitch=0;t.UpdateTerrainSupport(t.Hero);
                for(int n=0;n<450;n++)t.Step(1d/60,new(t.Hero.Speed<5?1:0,0,t.Hero.Speed>6));
                Console.WriteLine($"SURFACE {road.Id}: z {t.Hero.Position.Z-road.Z:F2}, damage {t.Hero.Damage:F3}, level {t.Hero.RoadLevel}");
                Check(t.Hero.Position.Z>road.Z+33&&t.Hero.RoadLevel==0,"surface vehicle drives across buried sidewalls without changing layer");
                Check(t.Hero.Damage<.1,"tunnel roof crossing causes no phantom impact");
            }
        });
        Test("barrier contact keeps wheel probes on their level",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:1));
            var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();t.Cash.Clear();
            foreach(var road in CityRoadLevels.All)foreach(int kind in Enumerable.Range(0,9))foreach(double along in new[]{0d,-120d}){
                var c=t.Hero;c.Kind=kind;c.Body.HalfW=c.HalfWidth;c.Body.HalfH=c.HalfLength;c.Body.InvMass=1/c.Mass;c.Body.InvI=3/(c.Mass*(c.HalfWidth*c.HalfWidth+c.HalfLength*c.HalfLength));
                double x=road.X+along,z=road.CentreZ(x)+5.65-c.HalfLength-1.6;
                Place(c,x,z,0,5);c.RoadLevel=road.Id;c.Damage=0;c.Destroyed=c.Burning=c.Falling=false;c.Lift=c.Roll=c.Pitch=c.VerticalVelocity=c.RollVelocity=c.PitchVelocity=0;t.UpdateTerrainSupport(c);
                for(int n=0;n<90;n++)t.Step(1d/60,new(0,0,true));
                Check(c.RoadLevel==road.Id&&Math.Abs(c.SurfaceHeight-road.Floor(c.Position.X,c.Position.Z))<.3,$"vehicle {kind}, road {road.Id}/{along}: support stays on selected layer");
                Check(Math.Abs(c.Position.Z-road.CentreZ(c.Position.X))<5.6,$"vehicle {kind}, road {road.Id}/{along}: barrier holds vehicle");
            }
        });
        Test("low bridge deck is solid from below",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:1));
            var t=QuietCity();var road=CityRoadLevels.All.First(s=>!s.Tunnel);
            var slabs=(Dictionary<Body2D,int>)typeof(CityTraffic).GetField("_supportStatics",BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(t)!;
            double x=road.X-126,z=road.CentreZ(x);var slab=slabs.Keys.OrderBy(b=>Math.Abs(b.X-x)+Math.Abs(b.Y-z)).First();
            Place(t.Hero,slab.X,slab.Y);t.Hero.RoadLevel=0;t.Hero.GroundHeight=CityTerrain.Ground(slab.X,slab.Y);Call(t,"RefreshLevelBodies");
            Check((bool)Call(t,"HeightContact",t.Hero.Body,slab)!,"vehicle cannot drive through low underside of approach");
            t.Hero.RoadLevel=road.Id;t.UpdateTerrainSupport(t.Hero);Call(t,"RefreshLevelBodies");
            Check(!(bool)Call(t,"HeightContact",t.Hero.Body,slab)!,"support slab does not push its own upper-road vehicle sideways");
        });
        RunLevelTraffic();
        CityLayout.Install(CityLayout.Generate(73921));
    }
    static void RunLevelTraffic(){
        Test("ordinary drivers merge into ramp in local physics",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true,roadRevision:2));
            var t=QuietCity();t.SpawnDistance=5000;t.Props.Clear();t.Pickups.Clear();t.Cash.Clear();
            foreach(var road in CityRoadLevels.All.Take(2))foreach(int kind in new[]{0,5})foreach(bool east in new[]{true,false})foreach(int direction in (east?new[]{0,1}:new[]{2,3})){
                var c=(CityCar)Call(t,"MakeCar",road.I+(east?-4:4),road.J,direction,15d,false)!;c.Kind=kind;c.LaneIndex=1;c.Outgoing=east?1:3;c.Cruise=6;
                var forward=CityTraffic.Forward(direction);var node=new CityPoint(CityTraffic.Coordinate(c.I),CityTraffic.Coordinate(c.J));
                c.Position=node-forward*15+CityTraffic.Right(forward)*CityTraffic.LanePosition(c);c.Path=new(c.Position,node-forward*CityTraffic.StopLine+CityTraffic.Right(forward)*CityTraffic.LanePosition(c));
                t.Cars.Add(c);typeof(CityTraffic).GetMethod("Register",BindingFlags.Instance|BindingFlags.NonPublic,null,new[]{typeof(CityCar)},null)!.Invoke(t,new object[]{c});Place(t.Hero,road.X+(east?-115:115),road.Z+45);t.Hero.RoadLevel=0;t.UpdateTerrainSupport(t.Hero);
                bool entered=false;double furthest=0;
                for(int n=0;n<2400;n++){
                    t.Step(1d/30,new(0,0,true));
                    if(c.Phase==3){entered=true;furthest=Math.Max(furthest,c.Along);if(furthest>105)break;}
                }
                Console.WriteLine($"NPC MERGE road {road.Id} kind {kind} from {direction}: entered {entered} progress {furthest:F2} damage {c.Damage:F3} phase {c.Phase} position {c.Position} speed {c.Speed} heading {c.Heading} along {c.Along}/{c.Path.Length} command {c.Command}");
                Check(entered&&furthest>100&&c.Damage<.15,"ordinary car or bus steers from outer lane into connector without phantom impact");
                Call(t,"FreePoliceReservation",c);t.World.RemoveBody(t.World.Bodies.IndexOf(c.Body));t.Cars.Remove(c);
            }
        });
        CityLayout.Install(CityLayout.Generate(73921));
    }
}
