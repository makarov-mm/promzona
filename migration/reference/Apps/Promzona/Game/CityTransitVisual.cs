using MakarovPhysics.Renderer;
namespace Promzona.Game;

internal static class CityTransitVisual
{
    private readonly record struct Piece(CityTransitRoute Route,double Along,double End);
    private static CityLayout? _layout;
    private static readonly Dictionary<(int,int),List<Piece>> _tiles=[];
    private static readonly Dictionary<(int,int),List<(CityPoint Left,CityPoint Right,CityPoint Wire)>> _supports=[];
    private static void Prepare(){
        if(ReferenceEquals(_layout,CityLayout.Current))return;_layout=CityLayout.Current;_tiles.Clear();_supports.Clear();
        HashSet<(int,long,long,long,long)> drawn=[];
        foreach(var route in CityTransit.Routes)for(int segment=0;segment<route.Segments.Length;segment++)for(double d=0;d<route.Segments[segment].Length;d+=2){
            double s=route.Starts[segment]+d,end=route.Starts[segment]+Math.Min(route.Segments[segment].Length,d+2);
            var a=route.Sample(s).Point;var b=route.Sample(end).Point;
            // Lines can share a street. Bake common rails/wires only once, using
            // segment-local samples so long straight sections line up exactly.
            var key=(route.Kind,(long)Math.Round(a.X*1000),(long)Math.Round(a.Z*1000),(long)Math.Round(b.X*1000),(long)Math.Round(b.Z*1000));
            if(!drawn.Add(key))continue;
            var p=route.Sample((s+end)*.5).Point;
            int i=CityTraffic.BlockIndex(p.X),j=CityTraffic.BlockIndex(p.Z);
            if(!_tiles.TryGetValue((i,j),out var list))_tiles[(i,j)]=list=[];
            list.Add(new(route,s,end));
        }
        foreach(var support in CityTransit.Supports){var p=(support.Left+support.Right)*.5;var cell=(CityTraffic.BlockIndex(p.X),CityTraffic.BlockIndex(p.Z));if(!_tiles.ContainsKey(cell))_tiles[cell]=[];if(!_supports.TryGetValue(cell,out var poles))_supports[cell]=poles=[];poles.Add(support);}
    }
    public static void Block(DioramaGeometry g,int i,int j,int lod){
        Prepare();if(!_tiles.TryGetValue((i,j),out var pieces))return;
        int start=g.SurfacePoints.Count;
        foreach(var piece in pieces){var route=piece.Route;double s=piece.Along;
            var a=route.Sample(s);var b=route.Sample(piece.End);var ra=CityTraffic.Right(new(Math.Sin(a.Heading),Math.Cos(a.Heading)));var rb=CityTraffic.Right(new(Math.Sin(b.Heading),Math.Cos(b.Heading)));
            Vec3 At(CityPoint p,double up)=>new((float)p.X,(float)(CityTerrain.Ground(p)+up),(float)p.Z);
            if(route.Kind==9){
                g.Material=SurfaceMaterial.Steel;
                foreach(double side in new[]{-.762,.762}){
                    var p=a.Point+ra*side;var q=b.Point+rb*side;
                    // Embedded grooved rail: a dark groove and a narrow steel running head.
                    g.Quad(At(p-ra*.085,.035),At(p+ra*.085,.035),At(q+rb*.085,.035),At(q-rb*.085,.035),new(.075f,.08f,.075f));
                    g.Quad(At(p-ra*.035,.045),At(p+ra*.035,.045),At(q+rb*.035,.045),At(q-rb*.035,.045),new(.53f,.56f,.57f));
                }
            }
            g.Material=SurfaceMaterial.Copper;
            foreach(double side in route.Kind==9?new[]{0d}:new[]{-.32,.32}){
                double sag=.10*Math.Sin((s%24)/24*Math.PI),sagEnd=.10*Math.Sin((piece.End%24)/24*Math.PI);
                g.Rod(At(a.Point+ra*side,5.7-sag),At(b.Point+rb*side,5.7-sagEnd),lod>0?.022f:.016f,new(.20f,.19f,.13f));
            }
            foreach(double stop in route.Stops)if(stop>=s&&stop<piece.End&&lod<2){
                var p=route.Sample(stop);var r=CityTraffic.Right(new(Math.Sin(p.Heading),Math.Cos(p.Heading)));
                var at=p.Point+r*(CityTraffic.RoadHalf-(route.Kind==9?1.9:CityLayout.WideRoads?5.2:1.9)+1.1);
                g.Material=SurfaceMaterial.Steel;g.Rod(At(at,.1),At(at,2.7),.045f,new(.55f,.55f,.50f));
                g.Material=SurfaceMaterial.Enamel;g.Box(At(at,2.6),new(.25f,.29f,.045f),route.Kind==9?new(.75f,.27f,.12f):new(.15f,.50f,.63f));
            }
        }
        if(_supports.TryGetValue((i,j),out var supports))foreach(var support in supports){
            var mid=(support.Left+support.Right)*.5;
            if(CityTraffic.BlockIndex(mid.X)!=i||CityTraffic.BlockIndex(mid.Z)!=j)continue;
            Vec3 At(CityPoint p,double up)=>new((float)p.X,(float)(CityTerrain.Ground(p)+up),(float)p.Z);
            g.Material=SurfaceMaterial.Steel;
            foreach(var pole in new[]{support.Left,support.Right}){g.Rod(At(pole,.05),At(pole,6.15),.08f,new(.30f,.32f,.29f));g.Rod(At(pole,.05),At(pole,.65),.14f,new(.38f,.39f,.35f));}
            g.Rod(At(support.Left,6.1),At(support.Right,6.1),.022f,new(.23f,.24f,.23f));
            g.Rod(At(support.Wire,5.6),At(support.Wire,6.1),.02f,new(.22f,.23f,.19f));
        }
        CityTerrainGeometry.Absolute(g,start);g.Material=SurfaceMaterial.Automatic;
    }
    public static void Collector(DioramaGeometry g,CityCar c){
        if(c.Destroyed||c.Falling||CityTransit.ById(c.TransitRoute) is not {} route)return;
        var pose=route.Sample(route.Closest(c.Position));if((pose.Point-c.Position).Length>3)return;
        int start=g.SurfacePoints.Count;var f=new CityPoint(Math.Sin(c.Heading),Math.Cos(c.Heading));var r=CityTraffic.Right(f);
        Vec3 At(CityPoint p,double height)=>new((float)p.X,(float)height,(float)p.Z);
        double roof=c.SurfaceHeight+c.Height;
        g.Material=SurfaceMaterial.Steel;
        if(c.Kind==9){
            var bottom=At(c.Position,roof);var top=At(pose.Point,CityTerrain.Ground(pose.Point)+5.7);
            var middle=(bottom+top)*.5f;
            foreach(int side in new[]{-1,1}){var elbow=middle+new Vec3((float)(f.X*.7*side),0,(float)(f.Z*.7*side));g.Rod(bottom,elbow,.035f,new(.3f,.3f,.25f));g.Rod(elbow,top,.035f,new(.3f,.3f,.25f));}
            g.Rod(top+new Vec3((float)r.X*.5f,0,(float)r.Z*.5f),top-new Vec3((float)r.X*.5f,0,(float)r.Z*.5f),.04f,new(.18f,.18f,.15f));
        }else foreach(double side in new[]{-.32,.32}){
            var contact=route.Sample(c.TransitAlong-3);var cr=CityTraffic.Right(new(Math.Sin(contact.Heading),Math.Cos(contact.Heading)));
            g.Rod(At(c.Position+f*.5+r*side,roof),At(contact.Point+cr*side,CityTerrain.Ground(contact.Point)+5.7),.035f,new(.28f,.3f,.22f));
        }
        CityTerrainGeometry.Absolute(g,start);g.Material=SurfaceMaterial.Automatic;
    }
}
