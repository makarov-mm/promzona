using System.Text.Json;
using Promzona.Game;
using MakarovPhysics.RigidBodies2D;
static partial class Tests
{
    // Format 3 saves, the batched body removal and the prop index (29.10.2026).
    static void RunSaveFormat(){
        Test("bezier routes survive the compact save",()=>{
            var a=new CityPoint(0,0);var b=new CityPoint(3,1);var c=new CityPoint(7,-2);var d=new CityPoint(12,4);
            var path=new CityPath(a,b,c,d);var handles=path.ExportCompact();
            Check(path.IsBezier&&handles.Length==4&&handles[3]==d,"a bezier path exports its four handles");
            var back=CityPath.Restore(handles,true);var p=path.ExportPoints();var q=back.ExportPoints();
            bool same=p.Length==q.Length;for(int i=0;same&&i<p.Length;i++)same=(p[i]-q[i]).Length<1e-12;
            Check(same&&Math.Abs(back.Length-path.Length)<1e-12,"restored handles reproduce every sampled point");
            var line=new CityPath(a,d);Check(line.IsBezier&&line.ExportCompact().Length==4,"a straight route is a bezier with collinear handles");
            var poly=CityPath.Restore(path.ExportPoints());Check(!poly.IsBezier&&poly.ExportCompact().Length==65,"a polyline keeps its points");
        });
        Test("compact capture omits fresh values and restore keeps them",()=>{
            var car=new CityCar{Id=7,Damage=.3,Kind=2};var state=CityState.Capture(car,compact:true);
            Check(state.Count==3&&state.ContainsKey("Damage")&&!state.ContainsKey("Lift")&&!state.ContainsKey("Physical"),"only fields away from their fresh value are captured");
            var fresh=new CityCar();CityState.Restore(fresh,state);
            Check(fresh.Id==7&&fresh.Damage==.3&&fresh.Kind==2&&fresh.Physical&&fresh.Occupants==-1&&fresh.LastDamageAt==-100,"restore keeps every constructor default the capture left out");
            var full=CityState.Capture(car);Check(full.Count>state.Count&&full.ContainsKey("Lift"),"a full capture still lists every field");
            string json=JsonSerializer.Serialize(state,CityState.Json);var read=JsonSerializer.Deserialize<Dictionary<string,object?>>(json,CityState.Json)!;
            var again=new CityCar();CityState.Restore(again,read);Check(again.Id==7&&again.Damage==.3&&again.Kind==2,"boxed values and file values restore the same actor");
            var prop=new StreetProp{Id=41};var ps=JsonSerializer.Deserialize<Dictionary<string,object?>>(JsonSerializer.Serialize(CityState.Capture(prop,compact:true),CityState.Json),CityState.Json)!;
            Check(CityState.Int(ps,nameof(StreetProp.Id))==41&&CityState.Int(ps,"Nothing") is null,"a prop id reads back from a parsed state");
        });
        Test("format 3 round trip is small and exact",()=>{
            CityLayout.Install(CityLayout.Generate(73921));var m=new CityModel();for(int k=0;k<6;k++)m.Step(1d/60);
            var world=m.Traffic.CaptureWorld(m);CityTraffic.ValidateWorld(world);
            string json=JsonSerializer.Serialize(world,CityState.Json);Console.WriteLine($"format 3 save {json.Length/1024} KB for {m.Traffic.Cars.Count} cars and {m.Traffic.Pedestrians.Count} people");
            Check(json.Length<12_000_000,"compact states and bezier handles keep a 33-node city under 12 MB");
            using(var doc=JsonDocument.Parse(json))CityState.ValidateJson(doc.RootElement);
            var read=JsonSerializer.Deserialize<CityWorldSave>(json,CityState.Json)!;CityTraffic.ValidateWorld(read);
            var second=new CityModel();second.Traffic.RestoreWorld(read,second);var t=second.Traffic;
            bool same=t.Cars.Count==m.Traffic.Cars.Count&&t.Pedestrians.Count==m.Traffic.Pedestrians.Count;
            for(int i=0;same&&i<t.Cars.Count;i++)same=t.Cars[i].Id==m.Traffic.Cars[i].Id&&(t.Cars[i].Position-m.Traffic.Cars[i].Position).Length<1e-9&&Math.Abs(t.Cars[i].Path.Length-m.Traffic.Cars[i].Path.Length)<1e-9&&t.Cars[i].Kind==m.Traffic.Cars[i].Kind;
            Check(same,"every car returns with its identity, pose and route length");
            Check(Math.Abs(t.Time-m.Traffic.Time)<1e-12&&t.Money==m.Traffic.Money,"clock and wallet survive the round trip");
            var p=m.Traffic.Pedestrians.First(x=>x.Path is not null);var q=t.Pedestrians.First(x=>x.Id==p.Id);
            Check(q.Path is not null&&Math.Abs(q.Path.Length-p.Path!.Length)<1e-9&&q.Persona==p.Persona,"a walker keeps its route and persona");
        });
        Test("batched body removal keeps joints and survivors in order",()=>{
            var w=new CityContactWorld{GravityY=0};
            for(int i=0;i<6;i++)w.AddCircle(i*3,0,.5,1,.3,0,0);
            w.AddRevolute(3,4,10.5,0);
            var gone=new List<Body2D>{w.Bodies[1],w.Bodies[2]};var third=w.Bodies[3];var fifth=w.Bodies[5];
            Check(w.RemoveBodies(gone)==2&&w.Bodies.Count==4&&w.Bodies[1]==third&&w.Bodies[3]==fifth,"survivors keep their order after a compaction");
            Check(w.Joints.Count==1&&w.Joints[0].A==1&&w.Joints[0].B==2,"joint indices are remapped to the compacted list");
            Check(w.RemoveBodies(new List<Body2D>{w.Bodies[1]})==1&&w.Joints.Count==0,"a joint dies with either of its bodies");
            Check(w.RemoveBodies(new List<Body2D>{new Body2D()})==0&&w.Bodies.Count==3,"a body that is not in the world removes nothing");
            w.Step(1d/120);Check(w.Bodies.Count==3,"the world steps after compaction");
        });
        Test("prop index equals a full scan",()=>{
            CityLayout.Install(CityLayout.Generate(73921,gridSize:49,wideRoads:true));var t=new CityTraffic();t.Step(1d/60);
            var rng=new Random(11);int mismatches=0,hits=0;
            for(int n=0;n<4000;n++){
                double x=(rng.NextDouble()*2-1)*CityTraffic.LandEdge,z=(rng.NextDouble()*2-1)*CityTraffic.LandEdge,r=1+rng.NextDouble()*6;
                var scan=t.Props.Where(p=>Math.Abs(p.Position.X-x)<=r&&Math.Abs(p.Position.Z-z)<=r).ToHashSet();
                var near=t.PropsInBox(x-r,z-r,x+r,z+r).Where(p=>Math.Abs(p.Position.X-x)<=r&&Math.Abs(p.Position.Z-z)<=r).ToHashSet();
                hits+=scan.Count;if(!scan.SetEquals(near))mismatches++;
            }
            Check(mismatches==0&&hits>500,$"box queries equal brute force ({hits} props found)");
            int live=t.LiveProps.Count;Check(live>0&&live<t.Props.Count&&t.LiveProps.All(p=>p.Body is not null),"the live list holds exactly the props with a body");
        });
    }
}
