using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
namespace MakarovPhysics.Experiments;
/// <summary>Small CPU presentation meshes. Sampling never advances the physical model.</summary>
public static class InstrumentSurface
{
    public static void HeightField(DioramaGeometry g,float xmin,float xmax,float zmin,float zmax,int cells,Func<float,float,float> height,Func<float,float,Vec3> color)
    {
        float dx=(xmax-xmin)/cells,dz=(zmax-zmin)/cells;
        Vec3 P(int x,int z)=>new(xmin+x*dx,height(xmin+x*dx,zmin+z*dz),zmin+z*dz);
        Vec3 N(Vec3 p)=>new Vec3(-(height(p.X+dx*.25f,p.Z)-height(p.X-dx*.25f,p.Z))/(dx*.5f),1,-(height(p.X,p.Z+dz*.25f)-height(p.X,p.Z-dz*.25f))/(dz*.5f)).Normalized();
        for(int z=0;z<cells;z++)for(int x=0;x<cells;x++)
        {Vec3 a=P(x,z),b=P(x+1,z),c=P(x+1,z+1),d=P(x,z+1),na=N(a),nb=N(b),nc=N(c),nd=N(d),ink=color((a.X+c.X)/2,(a.Z+c.Z)/2);
         g.SurfaceTriangle(a,c,b,na,nc,nb,ink);g.SurfaceTriangle(a,d,c,na,nd,nc,ink);}
    }
    public static void IsoSurface(DioramaGeometry g,Vec3 low,Vec3 high,int nx,int ny,int nz,Func<Vec3,float> field,Vec3 color)
    {
        int sx=nx+1,sy=ny+1;float dx=(high.X-low.X)/nx,dy=(high.Y-low.Y)/ny,dz=(high.Z-low.Z)/nz;
        int Index(int x,int y,int z)=>(z*sy+y)*sx+x;
        var values=new float[sx*sy*(nz+1)];var normals=new Vec3[values.Length];
        Vec3 P(int x,int y,int z)=>low+new Vec3(x*dx,y*dy,z*dz);
        for(int z=0;z<=nz;z++)for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)values[Index(x,y,z)]=field(P(x,y,z));
        for(int z=0;z<=nz;z++)for(int y=0;y<=ny;y++)for(int x=0;x<=nx;x++)normals[Index(x,y,z)]=new Vec3(
            -(values[Index(Math.Min(nx,x+1),y,z)]-values[Index(Math.Max(0,x-1),y,z)])/dx,
            -(values[Index(x,Math.Min(ny,y+1),z)]-values[Index(x,Math.Max(0,y-1),z)])/dy,
            -(values[Index(x,y,Math.Min(nz,z+1))]-values[Index(x,y,Math.Max(0,z-1))])/dz).Normalized();
        int[,] corners={{0,0,0},{1,0,0},{1,1,0},{0,1,0},{0,0,1},{1,0,1},{1,1,1},{0,1,1}};
        int[,] tetra={{0,5,1,6},{0,1,2,6},{0,2,3,6},{0,3,7,6},{0,7,4,6},{0,4,5,6}};
        int[,] edges={{0,1},{0,2},{0,3},{1,2},{1,3},{2,3}};
        var pos=new Vec3[8];var ns=new Vec3[8];var fs=new float[8];var hits=new Vec3[4];var hn=new Vec3[4];
        for(int z=0;z<nz;z++)for(int y=0;y<ny;y++)for(int x=0;x<nx;x++)
        {
            bool positive=false,negative=false;
            for(int c=0;c<8;c++){int xx=x+corners[c,0],yy=y+corners[c,1],zz=z+corners[c,2],index=Index(xx,yy,zz);pos[c]=P(xx,yy,zz);ns[c]=normals[index];fs[c]=values[index];positive|=fs[c]>=0;negative|=fs[c]<0;}
            if(!positive||!negative)continue;
            for(int t=0;t<6;t++)
            {
                int count=0;
                for(int e=0;e<6;e++){int a=tetra[t,edges[e,0]],b=tetra[t,edges[e,1]];if((fs[a]>=0)==(fs[b]>=0))continue;float f=fs[a]/(fs[a]-fs[b]);hits[count]=Vec3.Lerp(pos[a],pos[b],f);hn[count]=Vec3.Lerp(ns[a],ns[b],f).Normalized();count++;}
                if(count<3)continue;
                if(count==4){ // Order the quadrilateral around its centroid, avoiding crossed triangles.
                    Vec3 center=(hits[0]+hits[1]+hits[2]+hits[3])*.25f,normal=(hn[0]+hn[1]+hn[2]+hn[3]).Normalized(),u=(hits[0]-center).Normalized(),v=Vec3.Cross(normal,u);
                    for(int i=0;i<4;i++)for(int j=i+1;j<4;j++)if(MathF.Atan2(Vec3.Dot(hits[i]-center,v),Vec3.Dot(hits[i]-center,u))>MathF.Atan2(Vec3.Dot(hits[j]-center,v),Vec3.Dot(hits[j]-center,u))){(hits[i],hits[j])=(hits[j],hits[i]);(hn[i],hn[j])=(hn[j],hn[i]);}
                }
                void Emit(int a,int b,int c){if(Vec3.Dot(Vec3.Cross(hits[b]-hits[a],hits[c]-hits[a]),hn[a]+hn[b]+hn[c])<0)(b,c)=(c,b);g.SurfaceTriangle(hits[a],hits[b],hits[c],hn[a],hn[b],hn[c],color);}
                Emit(0,1,2);
                if(count==4)Emit(0,2,3);
            }
        }
    }
}
