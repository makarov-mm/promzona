using Promzona.Game;
using System.Text.Json;
static partial class Tests
{
    static void RunImportedAssets()
    {
        var choices=new (int Id,int Kind)[]{(7,0),(8,0),(2,0),(3,0),(4,0),(5,0),(2,1),(3,1),(2,2),(3,2)};
        var names=new HashSet<string>();
        foreach(var choice in choices)Test("imported vehicle possession "+choice,()=>{
            var t=FootSandbox();var c=new CityCar{Id=choice.Id,Kind=choice.Kind,Position=new(-12,0),Cruise=0};
            c.Path=new(c.Position,c.Position+new CityPoint(0,40));t.Cars.Add(c);typeof(CityTraffic).GetMethod("Register",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(CityCar)},null)!.Invoke(t,new object[]{c});Call(t,"RebuildActorGrid");
            var model=CityFleet.Model(c)!;names.Add(model);double width=c.HalfWidth,length=c.HalfLength,mass=c.Mass;
            Check(model!=null&&width>.7&&width<1.65&&length>1.5&&length<3.5,"physical bounds are in metres: "+model);
            Check(Math.Abs(1/c.Body.InvMass-mass)<1e-7,"registered rigid body carries the catalog mass: "+model);
            Check(t.InteractVehicle(),"exit original car: "+model);WalkPlace(t,c.Position.X-width-1.3,.5);Tick(t,.4);
            Check(t.NearbyEnterableCar()==c&&t.InteractVehicle(),"approach imported front door: "+model);Tick(t,2);
            Check(!t.OnFoot&&t.Hero==c&&CityFleet.Model(t.Hero)==model&&c.HalfWidth==width&&c.HalfLength==length&&c.Mass==mass,"carjacking keeps imported identity and dimensions: "+model);
            Tick(t,.4);Check(t.InteractVehicle()&&t.OnFoot,"exit imported vehicle: "+model);
            Check(!CityTraffic.CarPersonOverlap(c.Position,c.Heading,t.PlayerPosition,.3,length,width),"exit stands outside imported hull: "+model);
        });
        Check(names.Count==10,"ten distinct imported drivable vehicles exercised");
        foreach(int seed in new[]{73921,42,91837})Test("imported site placement "+seed,()=>{
            CityLayout.Install(CityLayout.Generate(seed,gridSize:49,wideRoads:true,roadRevision:3));var sites=CityImportedSites.All().ToArray();
            foreach(string model in new[]{"soviet_kiosk__3d_model","outdated_soviet_bus_stop","soviet_soda_machine","soviet_gasoline_pump_free","tires_decor","ziu_9","free_soviet_tram_x-series"})
                Check(sites.Any(s=>s.Model==model),"generated site exists: "+model);
            Check(sites.All(s=>CityWater.Depth(s.Position)<.01&&double.IsFinite(s.Ground)),"imported scenery stays on dry supported land");
            Check(sites.All(s=>s.Ground>=CityTerrain.Ground(s.Position.X-s.HalfX,s.Position.Z-s.HalfZ)&&s.Ground>=CityTerrain.Ground(s.Position.X+s.HalfX,s.Position.Z+s.HalfZ)),"rigid scenery foundations clear sloping ground");
            Check(sites.Where(s=>s.Extra).All(s=>!CityImportedSites.ExtraClear(s.Position,.3)&&!CityAmenities.TreeClear(s.Position)&&!CityAmenities.PropClear(s.Position,.3)),"extra footprints reserve collision, trees and loose props");
            var again=CityImportedSites.All();Check(again.SequenceEqual(sites),"layout cache returns stable placement");
            var wagons=sites.Where(s=>s.Model=="metro_2033_train").OrderBy(s=>s.Position.X).ToArray();
            Check(wagons.Length==3,"three imported wagons on industrial siding");
            foreach(var w in wagons)Check(Math.Abs(CityTerrain.Ground(w.Position.X-w.HalfX,w.Position.Z)-CityTerrain.Ground(w.Position.X+w.HalfX,w.Position.Z))<.05,"entire imported wagon has level rail support");
            for(int n=1;n<wagons.Length;n++)Check(wagons[n].Position.X-wagons[n-1].Position.X>wagons[n].HalfX+wagons[n-1].HalfX,"full-length wagons do not overlap");
            foreach(var stop in sites.Where(s=>s.Model=="outdated_soviet_bus_stop")){
                Check(CityImportedSites.StopOccupied(stop.Position+new CityPoint(-.48,0)),"stop rear wall is solid");
                Check(!CityImportedSites.StopOccupied(stop.Position+new CityPoint(.8,0)),"stop passenger approach remains open");
            }
        });
        Test("imported vehicle save identity",()=>{
            var m=new CityModel();var t=m.Traffic;var before=t.Cars.ToDictionary(c=>c.Id,c=>(CityFleet.Model(c),c.Mass,c.HalfLength,c.HalfWidth));
            var save=JsonSerializer.Deserialize<CityWorldSave>(JsonSerializer.Serialize(t.CaptureWorld(m),CityState.Json),CityState.Json)!;
            t.RestoreWorld(save,m);
            Check(t.Cars.All(c=>before.TryGetValue(c.Id,out var a)&&a==(CityFleet.Model(c),c.Mass,c.HalfLength,c.HalfWidth)),"save/load preserves model, mass and hull for the complete fleet");
        });
        Test("animal skin follows actual movement",()=>{
            var t=Empty();var a=new CityAnimal{Kind=AnimalKind.Dog,Body=t.World.Bodies[t.World.AddCircle(0,0,.3,20,.3,.1,0)],Distance=.3,Wait=12};
            float Pose()=>CityAnimalAnimation.Pose(a,.2,.9f,.85f,1.5f);
            Check(Pose()<64,"stationary animal uses idle");a.Body.Vx=1.3;
            Check(Pose()>=64&&Pose()<128,"route retarget timer does not suppress walking");a.Fear=.7;
            Check(Pose()>=128&&Pose()<192,"escaping animal uses run");a.Flying=true;
            Check(Pose()>=192&&Pose()<256,"flight has separate wing cycle");a.Dead=true;
            Check(Pose()==0,"dead animal never flies or walks");
            a.Dead=a.Flying=false;a.Physical=false;a.Target=a.Position;
            Check(Pose()<64,"distant animal at target idles despite stale velocity");
        });
        Test("animal transition continuity and ground orientation",()=>{
            var t=Empty();var a=new CityAnimal{Kind=AnimalKind.Dog,Body=t.World.Bodies[t.World.AddCircle(0,0,.3,20,.3,.1,0)],Distance=.2};
            var motion=new CityAnimalMotion();var idle=motion.Sample(a,0,1,.8f,1.3f);
            a.Body.Vx=1;a.Heading=Math.PI/2;
            var first=motion.Sample(a,.02,1,.8f,1.3f);
            Check(first.From<64&&first.To>=64&&first.To<128&&first.Blend>0&&first.Blend<.2,"idle and walk overlap briefly instead of snapping");
            var again=motion.Sample(a,.02,1,.8f,1.3f);
            Check(first==again,"shadow and colour passes share exactly the same pose");
            Check(motion.Heading>0&&motion.Heading<Math.PI/2,"facing follows a sudden route turn smoothly");
            var settled=motion.Sample(a,.27,1,.8f,1.3f);
            Check(settled.Blend==1,"transition finishes in bounded simulation time");
            a.Body.Vx=0;var stop=motion.Sample(a,.29,1,.8f,1.3f);
            a.Body.Vx=1;var reverse=motion.Sample(a,.31,1,.8f,1.3f);
            Check(stop.To<64&&reverse.To>=64&&reverse.Blend>.8,"reversing a transition reuses the complementary blend");
            a.Flying=true;a.Height=.5;motion.Sample(a,.33,1,.8f,1.3f);
            Check(motion.FlightPitch>0&&motion.FlightPitch<.48,"takeoff pitch rises smoothly");
            a.Flying=false;a.Height=0;a.Dead=true;var dead=motion.Sample(a,.35,1,.8f,1.3f);
            Check(dead.From==0&&dead.To==0&&dead.Blend==1,"death cancels movement immediately");
            a.Dead=false;a.Heading=0;motion.Sample(a,2,1,.8f,1.3f);motion.Ground(.2,.3,false);
            Check(Math.Abs(motion.Pitch+Math.Atan(.3))<1e-6&&Math.Abs(motion.Roll-Math.Atan(.2))<1e-6,"feet plane follows both terrain axes");
            motion.Ground(.2,.3,true);Check(motion.Pitch==0&&motion.Roll==0,"flying and knocked actors are not glued to slopes");
            a.Heading=Math.PI-.01;motion.Sample(a,3,1,.8f,1.3f);a.Heading=-Math.PI+.01;motion.Sample(a,3.02,1,.8f,1.3f);
            Check(Math.Abs(motion.Heading-Math.PI)<.03,"heading wraps across pi on the short arc");
        });
        Test("wheel travel independent of draw passes and actor IDs",()=>{
            var wheel=new CityWheelMotion();Check(wheel.Sample(10,2)==0,"first appearance establishes distance origin");
            Check(wheel.Sample(12,2)==2&&wheel.Sample(12,2)==2,"door and shadow renders never advance wheels twice");
            Check(wheel.Sample(13,-2)==1,"reverse travel spins wheels backwards");
            Check(wheel.Sample(13,0)==1,"stationary wheels remain fixed");
            Check(wheel.Sample(400,2)==1&&wheel.Sample(0,2)==1,"teleport and save distance reset do not create a huge spin");
            Check(wheel.Sample(double.NaN,2)==1,"invalid sample cannot poison future motion");
            Check(wheel.Sample(1,2)==2,"valid motion resumes after a reset");
            Check(new CityWheelMotion().Sample(12,2)==0,"new actor starts independently even when an ID is reused");
        });
        Test("local animal threat neighbours",()=>{
            var t=Empty();t.Animals.Clear();
            var near=new CityAnimal{Kind=AnimalKind.Dog,Position=new(-8.1,-.1),Physical=true};
            var dead=new CityAnimal{Kind=AnimalKind.Cat,Position=new(-7.9,.1),Physical=true,Dead=true};
            var distant=new CityAnimal{Kind=AnimalKind.Dog,Position=new(300,300),Physical=true};
            var farRule=new CityAnimal{Kind=AnimalKind.Cat,Position=new(-7.9,.1),Physical=false};
            t.Animals.AddRange([near,dead,distant,farRule]);Call(t,"RebuildAnimalThreatGrid");
            var method=typeof(CityTraffic).GetMethod("NearbyAnimalThreats",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!;
            CityAnimal[] Query()=>((IEnumerable<CityAnimal>)method.Invoke(t,new object[]{new CityPoint(-7.9,.1),7d})!).ToArray();
            Check(Query().SequenceEqual(new[]{near}),"negative grid boundary finds a nearby living physical predator only");
            near.Position=new(40,40);Call(t,"RebuildAnimalThreatGrid");
            Check(Query().Length==0,"moving predator leaves the old cell");
            near.Position=new(-8.1,-.1);near.Height=2;Call(t,"RebuildAnimalThreatGrid");
            Check(Query().Length==0,"airborne predator is excluded");
            t.Animals.Clear();Call(t,"RebuildAnimalThreatGrid");
            Check(Query().Length==0,"removed actors leave no stale threat references");
        });
        Test("vehicle LOD coverage",()=>{
            foreach(float distance in new[]{0f,34.9f,35,38,40.99f,41,86.99f,87,95,102.99f,103,420}){
                var lod=CityVehicleLod.Select(distance);
                Check(lod.First>=0&&lod.Second<=2&&lod.Second>=lod.First&&lod.Blend>=0&&lod.Blend<1,"valid adjacent LODs at "+distance);
                int count=0;for(int pixel=0;pixel<100;pixel++){
                    float mask=(pixel+.5f)/100;
                    bool first=lod.First==lod.Second||mask>=lod.Blend;
                    bool second=lod.First!=lod.Second&&lod.Blend>0&&mask<lod.Blend;
                    if(first!=second)count++;
                }
                Check(count==100,"each covered pixel belongs to exactly one LOD at "+distance);
            }
        });
    }
}
