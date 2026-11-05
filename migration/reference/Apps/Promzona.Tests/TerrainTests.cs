using Promzona.Game;
using MakarovPhysics.RigidBodies2D;
static partial class Tests
{
    static void RunTerrain(){
        Test("terrain generation",()=>{
            var layout=CityLayout.Generate(73921);CityLayout.Install(layout);var h=layout.Terrain;
            Check(h.Maximum-h.Minimum>25,"city has substantial elevation range");
            var other=CityLayout.Generate(73921).Terrain;var different=CityLayout.Generate(83).Terrain;
            double error=0,variation=0,roadSlope=0,padError=0;int hills=0,hollows=0;
            for(int x=-500;x<=500;x+=17)for(int z=-500;z<=500;z+=19){error=Math.Max(error,Math.Abs(h.Height(x,z)-other.Height(x,z)));variation+=Math.Abs(h.Height(x,z)-different.Height(x,z));}
            Check(error==0,"same seed reproduces exact terrain");Check(variation>1000,"different seed changes landscape");
            foreach(var b in layout.Blocks){double centre=h.Height(b.X,b.Z);foreach(double dx in new[]{-b.HalfX,0,b.HalfX})foreach(double dz in new[]{-b.HalfZ,0,b.HalfZ})padError=Math.Max(padError,Math.Abs(h.Height(b.X+dx,b.Z+dz)-centre));}
            Check(padError<.001,"every building footprint is level and grounded");
            for(int j=0;j<CityTraffic.Nodes;j++)for(int i=0;i<CityTraffic.Nodes;i++){double x=CityTraffic.Coordinate(i),z=CityTraffic.Coordinate(j);var slope=h.Gradient(x,z);roadSlope=Math.Max(roadSlope,slope.Length);}
            Check(roadSlope<.28,"road intersection grades stay drivable");
            for(int x=-450;x<=450;x+=20)for(int z=-450;z<=450;z+=20){double d=h.Height(x+20,z)+h.Height(x-20,z)+h.Height(x,z+20)+h.Height(x,z-20)-4*h.Height(x,z);if(d>.2)hollows++;if(d<-.2)hills++;}
            Check(hills>20&&hollows>20,"landscape includes convex hills and concave hollows");
            double edge=CityTraffic.LandEdge;Check(Math.Abs(h.Height(edge,0))<.001&&Math.Abs(h.Height(-edge,0))<.001&&Math.Abs(h.Height(0,edge))<.001,"beach meets original island datum continuously");
            Check(double.IsFinite(h.Height(99999,-99999)),"outside-island lookup is bounded");
            var cancellation=new CancellationToken(true);bool cancelled=false;try{CityLayout.Generate(4,null,cancellation);}catch(OperationCanceledException){cancelled=true;}Check(cancelled,"cancelled world generation exits promptly");
            foreach(int seed in new[]{1,2,42,83,1234567}){
                var layout2=CityLayout.Generate(seed);double e=0,maxSlope=0;
                foreach(var b in layout2.Blocks)foreach(double dx in new[]{-b.HalfX,b.HalfX})foreach(double dz in new[]{-b.HalfZ,b.HalfZ})e=Math.Max(e,Math.Abs(layout2.Terrain.Height(b.X+dx,b.Z+dz)-layout2.Terrain.Height(b.X,b.Z)));
                for(int j=0;j<CityTraffic.Nodes;j++)for(int i=0;i<CityTraffic.Nodes;i++)for(int n=0;n<36;n+=3){double x=CityTraffic.Coordinate(i),z=CityTraffic.Coordinate(j);maxSlope=Math.Max(maxSlope,Math.Max(layout2.Terrain.Gradient(x+1.8,z+n).Length,layout2.Terrain.Gradient(x+n,z+1.8).Length));}
                Check(e<.001&&maxSlope<.35,$"seed {seed}: all foundations flat and traffic lanes graded ({maxSlope:F3})");
            }
            Console.WriteLine($"TERRAIN range={h.Minimum:F3}..{h.Maximum:F3} maxIntersectionSlope={roadSlope:F4} foundationError={padError:E2}");
        });
        Test("terrain chassis",()=>{
            var t=QuietCity();var c=t.Hero;Place(c,1.8,-70);t.UpdateTerrainSupport(c);var h=CityTerrain.Current;
            Check(c.TerrainActive&&c.SurfaceHeight>1,"local chassis is supported above terrain datum");
            double f=Math.Sin(c.Heading),z=Math.Cos(c.Heading);double expected=Math.Atan2(h.Height(c.Position.X+f*c.Wheelbase*.5,c.Position.Z+z*c.Wheelbase*.5)-h.Height(c.Position.X-f*c.Wheelbase*.5,c.Position.Z-z*c.Wheelbase*.5),c.Wheelbase);
            Check(Math.Abs(c.TerrainPitch-expected)<.01,"chassis pitch follows wheel contacts");
            double supportError=0;foreach(double x in new[]{-1d,1d})foreach(double zSide in new[]{-1d,1d}){
                double wx=c.Body.X+x*c.HalfWidth*.88,wz=c.Body.Y+zSide*c.Wheelbase*.5;
                double plane=c.SurfaceHeight+Math.Tan(c.TerrainPitch)*zSide*c.Wheelbase*.5-Math.Tan(c.TerrainRoll)*x*c.HalfWidth*.88;
                supportError=Math.Min(supportError,plane-h.Height(wx,wz));
            }
            Check(supportError>-.0001,"four-corner support never sinks a wheel into the height field");
            double absolute=c.SurfaceHeight+3;c.Lift=3;c.Body.Y+=2;t.UpdateTerrainSupport(c,true);Check(Math.Abs(c.SurfaceHeight+c.Lift-absolute)<1e-8,"airborne chassis preserves absolute height across slope");
            c.Lift=0;c.Body.Y=-70;t.UpdateTerrainSupport(c);var slope=h.Gradient(c.Body.X,c.Body.Y);c.Body.Vx=c.Body.Vy=0;VehicleDynamics.Step(c,default,.01);
            Check(c.Body.Vx*slope.X+c.Body.Vy*slope.Z<0,"gravity accelerates unbraked car downhill");
            c.Body.Vx=c.Body.Vy=0;for(int n=0;n<240;n++)VehicleDynamics.Step(c,new(0,0,true),1d/240);
            Check(Math.Sqrt(c.Body.Vx*c.Body.Vx+c.Body.Vy*c.Body.Vy)<.15,"brakes hold stopped car on a normal grade");
            var prop=new StreetProp{Kind=StreetKind.Bottle,Home=c.Position,Body=new Body2D{X=c.Body.X,Y=c.Body.Y}};CityTraffic.PropSlope(prop,.1);
            Check(prop.Body.Vx*slope.X+prop.Body.Vy*slope.Z<0,"local loose bottle rolls down slope");
            var bin=new StreetProp{Kind=StreetKind.Bin,Home=c.Position,Body=new Body2D{X=c.Body.X,Y=c.Body.Y}};CityTraffic.PropSlope(bin,.1);
            Check(bin.Body.Vx==0&&bin.Body.Vy==0,"static friction holds upright street furniture");
            Check(t.PhysicalCars<t.Cars.Count||t.Cars.Count==1,"terrain does not activate global collision simulation");
            var empty=Empty();var flat=Car(empty.World);empty.UpdateTerrainSupport(flat);Check(!flat.TerrainActive&&flat.SurfaceHeight==0,"isolated flat physics fixtures retain explicit flat mode");
        });
        Test("terrain visibility and effects",()=>{
            var h=CityTerrain.Current;var a=new CityPoint(-100,-100);var b=new CityPoint(100,100);
            Check(!h.ClearLine(a,-1,b,-1),"terrain blocks underground sightline");Check(h.ClearLine(a,100,b,100),"above-terrain sightline remains visible");
            double y=h.Height(1.8,-70)+1.35;double safe=ChaseCamera.VisibleFraction(1.8,y,-70,0,-30,0);Check(safe<.1,"camera retracts before going through terrain");
            var t=QuietCity();t.Emit(t.Hero.Position.X,.8,t.Hero.Position.Z,0,1,0);Check(Math.Abs(t.Particles[0].Y-CityTerrain.Ground(t.Hero.Position)-.8)<1e-8,"effects spawn in elevated world space");
            var model=new CityModel();var save=model.Traffic.CaptureWorld(model);model.Traffic.RestoreWorld(save,model);
            Check(model.Traffic.Cars.All(c=>c.TerrainActive&&double.IsFinite(c.SurfaceHeight+c.TerrainPitch+c.TerrainRoll)),"save restores terrain support for every vehicle");
            Check(model.Traffic.World.Bodies.Count<1500,"restored height field preserves local collision budget");
            var hero=model.Traffic.Hero;double height=hero.SurfaceHeight;model.Traffic.RestoreWorld(save,model);Check(Math.Abs(model.Traffic.Hero.SurfaceHeight-height)<1e-8,"save load does not accumulate ground offsets");
        });
    }
}
