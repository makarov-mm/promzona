using MakarovPhysics.Renderer;
using static MakarovPhysics.Experiments.CosmosScene;
namespace MakarovPhysics.Experiments;

/// <summary>Immutable world geometry, uploaded once per roughness change.</summary>
public static class PlanetaryLandscape
{
    public const float Scale=.72f;
    public readonly record struct Rock(float X,float Z,float Radius,float Height,float Depth);
    public static IReadOnlyList<SurfaceVertex> Build(Func<double,double,double> vertexHeight,Func<double,double,double> height,IEnumerable<Rock> rocks,bool mars)
    {
        var g=new DioramaGeometry {CaptureSurface=true,Material=SurfaceMaterial.Stone};
        Vec3 At(double x,double z)=>P(x,vertexHeight(x,z),z)*Scale;
        Vec3 Normal(double x,double z)=>P(-(vertexHeight(x+.2,z)-vertexHeight(x-.2,z))/.4,1,-(vertexHeight(x,z+.2)-vertexHeight(x,z-.2))/.4).Normalized();
        // Cache the shared vertices/normals, rather than evaluating craters six
        // times per triangle. One-metre cells match the physical interpolant.
        var positions=new Vec3[257,257];var normals=new Vec3[257,257];
        for(int x=0;x<=256;x++)for(int z=0;z<=256;z++){positions[x,z]=At(x-128,z-128);normals[x,z]=Normal(x-128,z-128);}
        double Mottle(double x,double z)
        {
            int a=(int)Math.Floor(x),b=(int)Math.Floor(z);double u=x-a,v=z-b;u=u*u*(3-2*u);v=v*v*(3-2*v);
            double H(int dx,int dz)=>Noise((a+dx)*193+(b+dz)*911,18);
            return (H(0,0)*(1-u)+H(1,0)*u)*(1-v)+(H(0,1)*(1-u)+H(1,1)*u)*v;
        }
        Vec3 Color(double x,double z)=>(mars?new Vec3(.56f,.315f,.175f):new Vec3(.50f,.49f,.475f))*(float)(.76+.24*Mottle(x*.10,z*.10)+.13*Mottle(x*.37,z*.37)+.045*Math.Sin(vertexHeight(x,z)*3.2));
        void GroundTriangle(Vec3 a,Vec3 b,Vec3 c,Vec3 na,Vec3 nb,Vec3 nc)
        {
            g.SurfacePoints.Add(new(a,na,Color(a.X/Scale,a.Z/Scale),a,SurfaceMaterial.Stone));
            g.SurfacePoints.Add(new(b,nb,Color(b.X/Scale,b.Z/Scale),b,SurfaceMaterial.Stone));
            g.SurfacePoints.Add(new(c,nc,Color(c.X/Scale,c.Z/Scale),c,SurfaceMaterial.Stone));
        }
        for(int x=0;x<256;x++)for(int z=0;z<256;z++)
        {
            var a=positions[x,z];var b=positions[x,z+1];var c=positions[x+1,z+1];var d=positions[x+1,z];var color=Color(x-128,z-128);
            GroundTriangle(a,b,c,normals[x,z],normals[x,z+1],normals[x+1,z+1]);
            GroundTriangle(a,c,d,normals[x,z],normals[x+1,z+1],normals[x+1,z]);
        }
        // Distant scenery continues to 1.28 km. A transition strip stitches
        // every fine edge vertex to the coarse grid without T-junction cracks.
        for(int side=0;side<4;side++)for(int t=-128;t<128;t++)
        {
            Vec3 Edge(float along,float outwards)
            {
                Vec3 Exact(float t)=>side switch {0=>At(t,outwards),1=>At(t,-outwards),2=>At(outwards,t),_=>At(-outwards,t)};
                if(outwards==128)return Exact(along);
                float cell=MathF.Floor(along/8)*8,f=(along-cell)/8;
                return Exact(cell)*(1-f)+Exact(cell+8)*f;
            }
            var a=Edge(t,128);var b=Edge(t+1,128);var c=Edge(t+1,136);var d=Edge(t,136);
            if(Vec3.Cross(b-a,c-a).Y<0) { (b,d)=(d,b); }
            g.SurfaceTriangle(a,b,c,Vec3.UnitY,Vec3.UnitY,Vec3.UnitY,Color(t,136));
            g.SurfaceTriangle(a,c,d,Vec3.UnitY,Vec3.UnitY,Vec3.UnitY,Color(t,136));
        }
        for(int x=-640;x<640;x+=8)for(int z=-640;z<640;z+=8)
        {
            if(x>=-136&&x<136&&z>=-136&&z<136 && (x>=-128&&x<128 || z>=-128&&z<128))continue;
            // The small corner squares bridge the four transition strips.
            var a=At(x,z);var b=At(x,z+8);var c=At(x+8,z+8);var d=At(x+8,z);
            g.SurfaceTriangle(a,b,c,Normal(x,z),Normal(x,z+8),Normal(x+8,z+8),Color(x,z));
            g.SurfaceTriangle(a,c,d,Normal(x,z),Normal(x+8,z+8),Normal(x+8,z),Color(x,z));
        }
        foreach(var rock in rocks)
        {
            var center=P(rock.X,height(rock.X,rock.Z),rock.Z)*Scale;
            Vec3 Local(double u,double v) { double facet=.93+.07*Math.Cos(3*u)*Math.Cos(v)*Math.Cos(v); return P(rock.Radius*Math.Cos(u)*Math.Cos(v)*facet,rock.Height*Math.Sin(v),rock.Depth*Math.Sin(u)*Math.Cos(v)*facet)*Scale; }
            for(int lat=0;lat<8;lat++)for(int lon=0;lon<16;lon++)
            {
                double u=lon*Math.Tau/16,v=-Math.PI/2+lat*Math.PI/8;
                var a=Local(u,v);var b=Local(u+Math.Tau/16,v);var c=Local(u+Math.Tau/16,v+Math.PI/8);var d=Local(u,v+Math.PI/8);
                Vec3 NormalAt(Vec3 p)=>new Vec3(p.X/(rock.Radius*rock.Radius),p.Y/(rock.Height*rock.Height),p.Z/(rock.Depth*rock.Depth)).Normalized();
                var color=(mars?new Vec3(.35f,.22f,.15f):new Vec3(.30f,.30f,.295f))*(float)(.8+.28*Noise((int)(rock.X*17+rock.Z*31),42));
                g.SurfaceTriangle(center+a,center+b,center+c,NormalAt(a),NormalAt(b),NormalAt(c),color);
                g.SurfaceTriangle(center+a,center+c,center+d,NormalAt(a),NormalAt(c),NormalAt(d),color);
            }
        }
        return g.SurfacePoints.ToArray();
    }
}
