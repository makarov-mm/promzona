using System.Numerics;
using MakarovPhysics.Numerics;
using MakarovPhysics.Renderer;
using static MakarovPhysics.Experiments.CosmosScene;
using static MakarovPhysics.Experiments.SpaceHardware;
namespace MakarovPhysics.Experiments;

/// <summary>Two distinct illustrative exploration vehicles. Wheel centres come
/// from the six physical contacts; the linkage is a visual reduced suspension,
/// not an exact replica of a flight rover or a multibody rocker-bogie solver.</summary>
public static class RoverVisual
{
    public static Quaternion BodyOrientation(RoverDynamics m)=>Quaternion.CreateFromYawPitchRoll((float)-m.Heading,(float)-m.Roll,(float)m.Pitch);
    public static void Draw(CosmosScene scene,RoverDynamics m,bool mars)
    {
        const float scale=PlanetaryLandscape.Scale;
        var center=new Vector3(m.Position.X,m.Position.Y-.7f,m.Position.Z);
        Vec3 World(Vector3 p)=>P(p.X-center.X,p.Y-center.Y,p.Z-center.Z)*scale;
        var h=new SpaceHardware(scene,new(0,.7f*scale,0),BodyOrientation(m),scale);
        var g=scene.Geometry;g.BevelBoxes=true;g.PrecisionFinish=true;
        scene.HardwareWorkshop=true;
        Vec3 alloy=new(.57f,.60f,.62f), paint=new(.76f,.77f,.73f), charcoal=new(.075f,.083f,.09f);
        h.Box(new(0,-.02f,0),new(.61f,.20f,.40f),mars?paint:Gold,mars?SurfaceMaterial.Enamel:SurfaceMaterial.Foil);
        h.Box(new(0,.20f,0),new(.69f,.035f,.46f),paint,SurfaceMaterial.Enamel);
        h.Box(new(0,-.23f,0),new(.53f,.035f,.34f),alloy,SurfaceMaterial.BrushedAluminium);
        for(int side=-1;side<=1;side+=2)
        {
            // Bolted service panels and radiator louvres.
            for(int j=0;j<3;j++)
            {
                h.Box(new((j-1)*.39f,-.005f,side*.406f),new(.18f,.135f,.009f),mars?paint:Gold, mars?SurfaceMaterial.Enamel:SurfaceMaterial.Foil);
                for(int k=0;k<2;k++)h.Ball(new((j-1)*.39f+(k==0?-.15f:.15f),.10f,side*.42f),.011f,charcoal);
            }
            for(int k=0;k<10;k++)h.Box(new(-.66f,.04f,(k-4.5f)*.073f),new(.018f,.12f,.013f),alloy,SurfaceMaterial.BrushedAluminium);
            h.Tube(new(-.5f,.245f,side*.42f),new(.55f,.245f,side*.42f),.012f,charcoal,SurfaceMaterial.Rubber);
            h.Tube(new(.55f,.245f,side*.42f),new(.55f,-.12f,side*.42f),.012f,charcoal,SurfaceMaterial.Rubber);
        }
        // Stepped neck, pan/tilt head, separate stereo lenses and navigation cameras.
        h.Ring(new(.37f,.25f,0),Vec3.UnitY,.045f,.095f,.05f,alloy);
        h.Tube(new(.37f,.27f,0),new(.37f,1.04f,0),.042f,paint);
        h.Tube(new(.318f,.29f,.02f),new(.318f,1.01f,.02f),.010f,charcoal,SurfaceMaterial.Rubber);
        h.Box(new(.39f,1.03f,0),new(.14f,.085f,mars?.25f:.19f),paint);
        for(int s=-1;s<=1;s+=2)
        {
            float z=s*(mars?.17f:.12f);
            h.Tube(new(.51f,1.04f,z),new(.565f,1.04f,z),.049f,charcoal);
            h.Tube(new(.566f,1.04f,z),new(.570f,1.04f,z),.038f,Blue,SurfaceMaterial.OpticalMirror);
            h.Box(new(.675f,.03f,s*.32f),new(.026f,.045f,.052f),charcoal);
            h.Tube(new(.70f,.03f,s*.32f),new(.712f,.03f,s*.32f),.025f,Blue,SurfaceMaterial.OpticalMirror);
        }
        h.Box(new(.39f,1.137f,0),new(.17f,.012f,mars?.28f:.22f),paint);
        h.Tube(new(-.39f,.25f,-.30f),new(-.39f,.92f,-.30f),.008f,alloy);
        h.Tube(new(-.39f,.92f,-.30f),new(-.39f,.98f,-.30f),.016f,charcoal);
        h.Dish(new(-.27f,.30f,.22f),mars?.19f:.22f);
        if(mars)
        {
            // A rear finned thermal/electrical power unit, separated from avionics.
            h.Tube(new(-.65f,.02f,0),new(-1.01f,.12f,0),.14f,charcoal,SurfaceMaterial.CastMetal);
            for(int i=0;i<10;i++)
            {
                double a=i*Math.Tau/10;
                h.Tube(P(-.68,.02+.22*Math.Cos(a),.22*Math.Sin(a)),P(-1.00,.12+.22*Math.Cos(a),.22*Math.Sin(a)),.018f,alloy);
            }
            for(int j=0;j<7;j++)h.Ring(new(-.68f-j*.05f,.02f+j*.014f,0),Vec3.UnitX,.125f,.205f,.012f,alloy);
            h.Box(new(-.12f,.265f,-.12f),new(.13f,.035f,.12f),charcoal);
            h.Ring(new(.05f,.29f,.25f),Vec3.UnitY,.04f,.09f,.025f,alloy);
        }
        else
        {
            // A broad segmented solar deck. Cells use silicon, with thin busbars.
            for(int side=-1;side<=1;side+=2)
            {
                h.Tube(new(-.20f,.24f,side*.24f),new(-.20f,.32f,side*.87f),.022f,alloy);
                h.Box(new(-.18f,.32f,side*.73f),new(.56f,.018f,.24f),alloy,SurfaceMaterial.BrushedAluminium);
                for(int x=0;x<8;x++)for(int z=0;z<3;z++)
                {
                    var c=new Vec3(-.18f+(x-3.5f)*.133f,.345f,side*.73f+(z-1)*.15f);
                    h.Box(c,new(.061f,.003f,.067f),new(.055f,.075f,.16f),SurfaceMaterial.Silicon);
                    h.Tube(c+new Vec3(-.043f,.004f,-.065f),c+new Vec3(-.043f,.004f,.065f),.0015f,alloy);
                }
            }
        }
        // The sampling arm remains folded clear of the front wheel in transit.
        float e=m.Sampling?(float)Math.Clamp(m.SampleProgress/1.1,0,1):0;
        var shoulder=new Vec3(.54f,-.13f,.21f);
        var elbow=new Vec3(.76f+e*.24f,-.18f-e*.12f,.13f);
        var wrist=new Vec3(.32f+e*.83f,-.27f-e*.29f,-.12f);
        h.Tube(shoulder,elbow,.037f,alloy);h.Tube(elbow,wrist,.029f,paint);
        h.Ball(shoulder,.065f,charcoal);h.Ball(elbow,.052f,charcoal);h.Ball(wrist,.045f,charcoal);
        h.Box(wrist+new Vec3(.04f,-.035f,0),new(.075f,.035f,.055f),alloy,SurfaceMaterial.MachinedMetal);
        h.Tube(shoulder+new Vec3(0,.04f,0),elbow+new Vec3(0,.04f,0),.008f,charcoal,SurfaceMaterial.Rubber);
        // Articulated rocker/bogie arms connect the actual hubs on each side.
        for(int side=0;side<2;side++)
        {
            int j=side*3;float sign=side==0?-1:1;
            var rear=World(m.Wheels[j]);var middle=World(m.Wheels[j+1]);var front=World(m.Wheels[j+2]);
            var pivot=h.At(new(.12f,-.06f,sign*.47f));
            var bogie=(rear+middle)*.5f+new Vec3(0,.17f*scale,0);
            g.Material=SurfaceMaterial.BrushedAluminium;
            g.Tube(pivot,front,.033f*scale,alloy);g.Tube(pivot,bogie,.043f*scale,alloy);
            g.Tube(bogie,rear,.031f*scale,alloy);g.Tube(bogie,middle,.031f*scale,alloy);
            g.Material=SurfaceMaterial.MachinedMetal;
            g.Ellipsoid(pivot,new(.055f,.055f,.055f),charcoal);g.Ellipsoid(bogie,new(.045f,.045f,.045f),charcoal);
        }
        // Thin metal wheel skins, open spokes, hubs, rims and raised grousers.
        var axis=P(-Math.Sin(m.Heading),0,Math.Cos(m.Heading));
        for(int i=0;i<6;i++)
        {
            var wheel=World(m.Wheels[i]);float sign=i<3?-1:1;
            g.Material=SurfaceMaterial.BrushedAluminium;
            MachineGeometry.Annulus(g,wheel,axis,.218f*scale,.24f*scale,.15f*scale,0,MathF.Tau,alloy,48);
            g.Material=SurfaceMaterial.MachinedMetal;
            g.Tube(wheel-axis*.105f*scale,wheel+axis*.105f*scale,.058f*scale,charcoal);
            for(int rim=-1;rim<=1;rim+=2)MachineGeometry.Annulus(g,wheel+axis*(rim*.075f*scale),axis,.208f*scale,.24f*scale,.012f*scale,0,MathF.Tau,paint,48);
            for(int k=0;k<24;k++)
            {
                double a=k*Math.Tau/24-m.WheelAngle;
                Vec3 Rad(double angle)=>P(Math.Cos(m.Heading)*Math.Cos(angle),Math.Sin(angle),Math.Sin(m.Heading)*Math.Cos(angle));
                var r=Rad(a);var bent=Rad(a+.065);
                var mid=wheel+bent*.244f*scale;
                g.Tube(wheel+r*.241f*scale-axis*.077f*scale,mid,.007f*scale,paint);
                g.Tube(mid,wheel+r*.241f*scale+axis*.077f*scale,.007f*scale,paint);
                if(k%3==0)g.Tube(wheel+r*.065f*scale+axis*(sign*.082f*scale),wheel+r*.216f*scale+axis*(sign*.060f*scale),.012f*scale,alloy);
            }
        }
    }
}
