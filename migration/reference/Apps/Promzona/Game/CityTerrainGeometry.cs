using MakarovPhysics.Renderer;
using System.Runtime.CompilerServices;
namespace Promzona.Game;

/// <summary>Bakes the height field into streamed district meshes. Rigid objects
/// retain their dimensions/normals; terrain and pavement conform to the field.</summary>
internal static class CityTerrainGeometry
{
    private sealed class Detail { public int Lod; }
    private static readonly ConditionalWeakTable<DioramaGeometry, Detail> Details = new();
    public static void SetDetail(DioramaGeometry g, int lod) => Details.GetOrCreateValue(g).Lod=lod;
    private sealed record Anchor(int Start, int End, double Height);

    private static readonly ConditionalWeakTable<DioramaGeometry, List<Anchor>> Anchors = new();

    public static void Absolute(DioramaGeometry g,int start)=>Anchors.GetOrCreateValue(g).Add(new(start,g.SurfacePoints.Count,0));

    public static void Rigid(DioramaGeometry g, int start, double x, double z) => Anchors.GetOrCreateValue(g)
        .Add(new(start, g.SurfacePoints.Count, CityTerrain.Ground(x, z)));

    public static Vec3 Ground(Vec3 p) => new(p.X, p.Y + (float)CityTerrain.Ground(p.X, p.Z), p.Z);

    public static void Conform(DioramaGeometry g, int start = 0)
    {
        var terrain = CityTerrain.Current;
        // Smaller nested anchors (tree/sign) are superseded by enclosing buildings.
        var anchors = Anchors.GetOrCreateValue(g).OrderBy(a => a.Start).ThenByDescending(a => a.End).ToArray();
        var samples=new Dictionary<(float,float),(double Height,CityPoint Slope)>();
        int cursor = 0;
        for (int i = start; i < g.SurfacePoints.Count; i++)
        {
            while (cursor < anchors.Length && anchors[cursor].End <= i) cursor++;
            var v = g.SurfacePoints[i];
            Vec3 p = v.Position, n = v.Normal;
            if (cursor < anchors.Length && anchors[cursor].Start <= i)
                p = new(p.X, p.Y + (float)anchors[cursor].Height, p.Z);
            else
            {
                var key=(p.X,p.Z);
                if(!samples.TryGetValue(key,out var sample))
                {
                    sample=(terrain.Height(p.X,p.Z),terrain.Gradient(p.X,p.Z));samples[key]=sample;
                }
                var slope = sample.Slope;
                p = new(p.X,p.Y+(float)sample.Height,p.Z);
                n = new Vec3(n.X - (float)slope.X * n.Y, n.Y, n.Z - (float)slope.Z * n.Y).Normalized();
            }

            g.SurfacePoints[i] = new(p, n, v.Color, v.Local, v.Material);
        }

        Anchors.Remove(g);
    }

    public static void Top(DioramaGeometry g, float x, float z, float hx, float hz, float y, Vec3 color)
    {
        // Fine contact geometry stays exact near the player. Distant patches
        // share every 1 m boundary vertex with every other LOD (no terrain skirts).
        if(Details.TryGetValue(g,out var detail) && detail.Lod>=2 && !CityRoadLevels.All.Any(s=>s.Contains(x,z,Math.Max(hx,hz)+20)))
        {
            Patch(g,x-hx,x+hx,z-hz,z+hz,y,color,detail.Lod>=3?36:9);return;
        }
        FineTop(g,x-hx,x+hx,z-hz,z+hz,y,color);
    }

    private static void FineTop(DioramaGeometry g,float x0,float x1,float z0,float z1,float y,Vec3 color)
    {
        
        for (float a = x0; a < x1 - .0001f;)
        {
            float b = MathF.Min(x1, MathF.Floor(a + .0001f) + 1);

            for (float c = z0; c < z1 - .0001f;)
            {
                float d = MathF.Min(z1, MathF.Floor(c + .0001f) + 1);
                
                // The lateral candidate margin catches cells straddling a wall,
                // but must not extend the cut past the exact end of the deck.
                double midX=(a+b)*.5;
                var cut=CityRoadLevels.All.FirstOrDefault(s=>s.Tunnel&&Math.Abs(midX-s.X)<CityRoadLevel.Length&&!s.Roof(midX)&&s.Contains(midX,(c+d)*.5,1));
                if(cut is null)g.Quad(new(a,y,c),new(a,y,d),new(b,y,d),new(b,y,c),color);
                else{
                    // Clip each metre cell to the two sides of the excavation;
                    // no opaque terrain sheet remains across a tunnel entrance.
                    Vec3[] quad=[new(a,y,c),new(a,y,d),new(b,y,d),new(b,y,c)];
                    for(int side=-1;side<=1;side+=2){
                        List<Vec3> polygon=[];
                        double Dist(Vec3 p)=>side*(p.Z-cut.CentreZ(p.X))-CityRoadLevel.HalfWidth;
                        var prev=quad[^1];double dp=Dist(prev);
                        foreach(var next in quad){double dn=Dist(next);
                            if((dp>=0)!=(dn>=0))polygon.Add(prev+(next-prev)*(float)(dp/(dp-dn)));
                            if(dn>=0)polygon.Add(next);prev=next;dp=dn;
                        }
                        for(int k=1;k+1<polygon.Count;k++)g.Triangle(polygon[0],polygon[k],polygon[k+1],color);
                    }
                }

                c = d;
            }

            a = b;
        }
    }

    private static void Patch(DioramaGeometry g,float x0,float x1,float z0,float z1,float y,Vec3 color,int maxSpan)
    {
        float width=x1-x0,depth=z1-z0;
        if(width<=2 || depth<=2){FineTop(g,x0,x1,z0,z1,y,color);return;}
        float cx=(x0+x1)*.5f,cz=(z0+z1)*.5f;
        bool split=width>maxSpan || depth>maxSpan;
        double centre=CityTerrain.Ground(cx,cz);
        // Bound the sampled simplification error to 1 cm, below pavement offsets.
        // Keep curved road/foundation transitions fine instead of flattening hills.
        if(!split)
            for(float z=z0;z<=z1 && !split;z+=.5f)
            for(float x=x0;x<=x1;x+=.5f)
                if(Math.Abs(FanHeight(x,z,x0,x1,z0,z1,centre)-CityTerrain.Ground(x,z))>.01){split=true;break;}
        if(split)
        {
            // Integer splits agree with the original terrain lattice on shared edges.
            float mx=Math.Clamp(MathF.Floor(cx),x0+1,x1-1),mz=Math.Clamp(MathF.Floor(cz),z0+1,z1-1);
            Patch(g,x0,mx,z0,mz,y,color,maxSpan);Patch(g,x0,mx,mz,z1,y,color,maxSpan);
            Patch(g,mx,x1,z0,mz,y,color,maxSpan);Patch(g,mx,x1,mz,z1,y,color,maxSpan);return;
        }
        Vec3 c=new(cx,y,cz);
        void Edge(float a,float b,bool alongX,float fixedAt,bool reverse)
        {
            while(a<b-.0001f)
            {
                float end=Math.Min(b,MathF.Floor(a+.0001f)+1);
                Vec3 p=alongX?new(a,y,fixedAt):new(fixedAt,y,a),q=alongX?new(end,y,fixedAt):new(fixedAt,y,end);
                if(reverse)g.Triangle(c,q,p,color);else g.Triangle(c,p,q,color);
                a=end;
            }
        }
        Edge(z0,z1,false,x0,false);Edge(x0,x1,true,z1,false);
        Edge(z0,z1,false,x1,true);Edge(x0,x1,true,z0,true);
    }

    private static double FanHeight(double x,double z,double x0,double x1,double z0,double z1,double centre)
    {
        double cx=(x0+x1)*.5,cz=(z0+z1)*.5,dx=x-cx,dz=z-cz;
        double t=Math.Max(Math.Abs(dx)/((x1-x0)*.5),Math.Abs(dz)/((z1-z0)*.5));
        if(t<1e-9)return centre;
        double bx=cx+dx/t,bz=cz+dz/t,edge;
        if(Math.Abs(dx)/((x1-x0)*.5)>=Math.Abs(dz)/((z1-z0)*.5))
        {
            double a=Math.Max(z0,Math.Floor(bz)),b=Math.Min(z1,Math.Floor(bz)+1);
            edge=b>a?CityTerrain.Ground(bx,a)+(CityTerrain.Ground(bx,b)-CityTerrain.Ground(bx,a))*(bz-a)/(b-a):CityTerrain.Ground(bx,bz);
        }
        else
        {
            double a=Math.Max(x0,Math.Floor(bx)),b=Math.Min(x1,Math.Floor(bx)+1);
            edge=b>a?CityTerrain.Ground(a,bz)+(CityTerrain.Ground(b,bz)-CityTerrain.Ground(a,bz))*(bx-a)/(b-a):CityTerrain.Ground(bx,bz);
        }
        return centre+(edge-centre)*t;
    }

    public static void Pavement(DioramaGeometry g, float x, float z, float half, float height, Vec3 color)
    {
        Top(g, x, z, half, half, height, color);

        for (float t = -half; t < half - .0001f;)
        {
            float u = MathF.Min(half, t + 1);

            for (int side = -1; side <= 1; side += 2)
            {
                var a = new Vec3(x + t, 0, z + side * half);
                var b = new Vec3(x + u, 0, z + side * half);
                var c = new Vec3(x + side * half, 0, z + t);
                var d = new Vec3(x + side * half, 0, z + u);

                if (side > 0) 
                    g.Quad(a, b, b + Vec3.UnitY * height, a + Vec3.UnitY * height, color);
                else 
                    g.Quad(b, a, a + Vec3.UnitY * height, b + Vec3.UnitY * height, color);

                if (side < 0) 
                    g.Quad(c, d, d + Vec3.UnitY * height, c + Vec3.UnitY * height, color);
                else
                    g.Quad(d, c, c + Vec3.UnitY * height, d + Vec3.UnitY * height, color);
            }

            t = u;
        }
    }
}
