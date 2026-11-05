using MakarovPhysics.Renderer;
namespace Promzona.Game;
internal static class CityScooterVisual
{
    public static void Draw(DioramaGeometry g,CityModel m,CityVisibility? visibility,CityActorModels? actors){
        foreach(var s in m.Traffic.Scooters){
            // The side of the deck/wheels, rather than the upright tyres, supports a fallen scooter.
            Vec3 origin=new((float)s.Position.X,(float)(CityRoadLevels.Surface(s.Position.X,s.Position.Z,s.RoadLevel)+.05+s.Height+.14*s.Fallen),(float)s.Position.Z);
            if((origin-g.Eye).Length()>110||!(visibility?.Sphere(origin+Vec3.UnitY*.6f,1.5f)??true))continue;
            int start=g.SurfacePoints.Count;float yaw=(float)s.Heading,roll=(float)s.Fallen*1.35f;
            var rider=m.Traffic.ScooterRider(s);
            if(rider is not null&&actors is not null&&actors.HeadSocket(rider,m.Traffic,g.Eye,out _,out float animatedHeading))yaw=animatedHeading;
            float handleY=.237f+(float)(rider?.Height??1.72)/1.72f*.99f;
            float handleZ=.45f*(rider is null?1:.94f+rider.Id%5*.035f);
            Vec3 f=new(MathF.Sin(yaw),0,MathF.Cos(yaw)),right=new(f.Z,0,-f.X);
            Vec3 up=Vec3.UnitY*MathF.Cos(roll)+right*MathF.Sin(roll),r=right*MathF.Cos(roll)-Vec3.UnitY*MathF.Sin(roll);
            Vec3 P(float x,float y,float z)=>origin+r*x+up*y+f*z;
            bool near=(origin-g.Eye).Length()<50;
            g.Material=SurfaceMaterial.BrushedAluminium;
            g.Box(P(0,.19f,-.06f),new(.13f,.035f,.49f),new(.27f,.29f,.28f),r,up,f);
            g.Tube(P(0,.19f,.43f),P(0,handleY,handleZ),.027f,new(.34f,.36f,.35f));
            g.Tube(P(-.25f,handleY,handleZ),P(.25f,handleY,handleZ),.024f,new(.32f,.34f,.33f));
            g.Material=SurfaceMaterial.Rubber;
            g.Box(P(0,.229f,-.08f),new(.115f,.008f,.37f),new(.07f,.08f,.075f),r,up,f);
            for(int wheel=-1;wheel<=1;wheel+=2){
                g.Tube(P(-.035f,.13f,wheel*.49f),P(.035f,.13f,wheel*.49f),.12f,new(.055f,.06f,.055f));
                if(near){g.Material=SurfaceMaterial.Steel;g.Tube(P(-.04f,.13f,wheel*.49f),P(.04f,.13f,wheel*.49f),.045f,new(.44f,.45f,.43f));g.Material=SurfaceMaterial.Rubber;}
            }
            for(int side=-1;side<=1;side+=2)g.Tube(P(side*.10f,handleY,handleZ),P(side*.25f,handleY,handleZ),.031f,new(.06f,.07f,.065f));
            if(near){g.Material=SurfaceMaterial.Enamel;g.Box(P(0,.63f,.365f),new(.034f,.12f,.018f),new(.50f,.35f,.12f),r,up,f);}
            g.Material=SurfaceMaterial.Automatic;CityTerrainGeometry.Absolute(g,start);
        }
    }
}
