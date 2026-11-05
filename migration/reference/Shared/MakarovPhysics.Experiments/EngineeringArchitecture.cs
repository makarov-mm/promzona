using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Reusable real-size architectural fittings. These meshes are cached
/// with a site. No window, handrail or fastener is a numerical-model input.</summary>
public static class EngineeringArchitecture
{
    private static readonly Vec3 Steel=new(.55f,.60f,.65f), Dark=new(.11f,.17f,.21f),
        Paint=new(.62f,.69f,.70f), Gold=new(.76f,.59f,.26f);

    public static void Window(DioramaGeometry g,Vec3 centre,float width,float height,Vec3 normal)
    {
        Vec3 right=Vec3.Cross(Vec3.UnitY,normal).Normalized();
        float trim=Math.Min(width,height)*.065f;
        g.Material=SurfaceMaterial.ArchitecturalGlass;
        g.Box(centre,new(width,height,trim*.18f),new(.18f,.31f,.40f),right,Vec3.UnitY,normal);
        g.Material=SurfaceMaterial.MachinedMetal;
        foreach(float s in new[]{-1f,1f})
        {
            g.Box(centre+right*(s*width),new(trim,height+trim,trim),Paint,right,Vec3.UnitY,normal);
            g.Box(centre+Vec3.UnitY*(s*height),new(width,trim,trim),Paint,right,Vec3.UnitY,normal);
        }
        g.Box(centre,new(trim*.55f,height,trim),Paint,right,Vec3.UnitY,normal);
        g.Box(centre-Vec3.UnitY*(height+trim),new(width+trim,trim,trim*2.2f),Steel,right,Vec3.UnitY,normal);
    }

    public static void Panel(DioramaGeometry g,Vec3 centre,float halfWidth,float halfHeight,Vec3 normal,Vec3 colour,int ribs=9)
    {
        Vec3 right=Vec3.Cross(Vec3.UnitY,normal).Normalized();
        float scale=Math.Min(halfWidth,halfHeight),t=scale*.012f;
        g.Material=SurfaceMaterial.Enamel;
        g.Box(centre,new(halfWidth,halfHeight,t),colour,right,Vec3.UnitY,normal);
        for(int k=0;k<=ribs;k++)
        {
            Vec3 p=centre+right*(halfWidth*(-.96f+1.92f*k/ribs))+normal*t;
            g.Box(p,new(t,halfHeight,t*1.4f),colour*.88f,right,Vec3.UnitY,normal);
        }
        g.Material=SurfaceMaterial.MachinedMetal;
        foreach(float x in new[]{-.9f,.9f})foreach(float y in new[]{-.9f,.9f})
        {
            Vec3 p=centre+right*(x*halfWidth)+Vec3.UnitY*(y*halfHeight)+normal*(t*2);
            g.Tube(p,p+normal*t,t*1.7f,Steel);
        }
    }

    public static void Flange(DioramaGeometry g,Vec3 centre,Vec3 normal,float radius)
    {
        g.Material=SurfaceMaterial.MachinedMetal;
        MachineGeometry.Annulus(g,centre,normal,radius*.63f,radius,radius*.16f,0,MathF.Tau,Steel,32);
        Vec3 u=Vec3.Cross(normal,MathF.Abs(normal.Y)<.8f?Vec3.UnitY:Vec3.UnitX).Normalized(),v=Vec3.Cross(normal,u);
        for(int k=0;k<8;k++)
        {
            float a=k*MathF.Tau/8;
            Vec3 p=centre+(u*MathF.Cos(a)+v*MathF.Sin(a))*(radius*.84f);
            g.Tube(p-normal*(radius*.11f),p+normal*(radius*.11f),radius*.065f,Steel*.75f);
        }
    }

    public static void Add(DioramaGeometry g,string id)
    {
        switch(id)
        {
            case "EngFlywheelStorage":
                // Epoxy working apron, construction joints, plinth bolts and
                // ductwork. Kept behind and around the open vacuum vessel.
                g.Material=SurfaceMaterial.Paving;
                g.Box(new(0,.003f,0),new(4.18f,.001f,2.57f),new(.28f,.34f,.36f));
                for(int k=0;k<6;k++)Panel(g,new(-3.45f+k*1.35f,1.65f,-2.586f),.64f,1.22f,Vec3.UnitZ,new(.50f,.57f,.59f),7);
                g.Material=SurfaceMaterial.MachinedMetal;
                LabGeometry.Sweep(g,[new(-3.65f,.3f,-2.2f),new(-3.65f,2.65f,-2.2f),new(3.65f,2.65f,-2.2f)],.105f,Steel,sides:24);
                for(int k=0;k<9;k++)Flange(g,new(-3.4f+k*.85f,2.65f,-2.2f),Vec3.UnitX,.14f);
                for(int k=0;k<16;k++)
                {
                    float a=k*MathF.Tau/16;
                    g.Material=SurfaceMaterial.MachinedMetal;
                    Vec3 p=new(.84f*MathF.Cos(a),.12f,.84f*MathF.Sin(a));
                    g.Tube(p,p+Vec3.UnitY*.025f,.025f,Steel);
                }
                Panel(g,new(2.75f,.55f,.892f),.34f,.49f,Vec3.UnitZ,new(.25f,.34f,.32f),12);
                break;
            case "EngWindFarm":
                Window(g,new(.55f,.235f,-4.104f),.19f,.075f,-Vec3.UnitZ);
                Panel(g,new(.55f,.18f,-3.493f),.30f,.16f,Vec3.UnitZ,new(.46f,.54f,.55f),8);
                break;
            case "EngPumpedStorage":
                Window(g,new(3.136f,.42f,-.20f),.18f,.10f,Vec3.UnitX);
                Panel(g,new(2.5f,.37f,-.414f),.54f,.23f,Vec3.UnitZ,new(.52f,.62f,.63f),12);
                break;
            case "EngTidalLagoon":
                for(int k=0;k<6;k++)foreach(float side in new[]{-1f,1f})
                    Window(g,new(side*.405f,.60f,-.9f+k*.38f),.10f,.06f,Vec3.UnitX*side);
                // Reinforced pier ribs continue along the sluice deck.
                g.Material=SurfaceMaterial.MachinedMetal;
                for(int k=0;k<4;k++)
                {
                    float z=-1.3f-k;
                    g.Box(new(.505f,.15f,z),new(.015f,.25f,.035f),Steel);
                }
                break;
            case "EngCoolingTower":
                for(int k=0;k<4;k++) Window(g,new(-7.25f+k*.7f,2.2f,1.24f),.22f,.4f,Vec3.UnitZ);
                Panel(g,new(-6.2f,.65f,1.23f),1.4f,.55f,Vec3.UnitZ,new(.53f,.61f,.62f),18);
                break;
            case "EngSolarFurnace":
                Window(g,new(2.6f,1.5f,3.435f),.9f,.4f,Vec3.UnitZ);
                Panel(g,new(4.21f,1.3f,2.2f),1.1f,1.2f,Vec3.UnitX,new(.56f,.62f,.60f),14);
                break;
            case "EngCompressedAirStorage":
                Panel(g,new(10.6f,.75f,3.36f),.57f,.67f,Vec3.UnitZ,new(.30f,.39f,.35f),16);
                break;
            case "EngDistrictHeating":
                Panel(g,new(-6.15f,.26f,.906f),.68f,.25f,Vec3.UnitZ,new(.48f,.38f,.30f),24);
                Window(g,new(-6.15f,.31f,.919f),.24f,.10f,Vec3.UnitZ);
                break;
            case "EngBaseIsolation":
                for(int k=0;k<5;k++)Window(g,new(-7+k*4,6.4f,-7.84f),1.3f,1.25f,Vec3.UnitZ);
                break;
            case "EngEscalator":
                for(int k=0;k<3;k++)Window(g,new(-13.4f+k*2.8f,4.8f,-3.94f),1.12f,2.6f,Vec3.UnitZ);
                break;
            case "EngPneumaticPost":
                foreach(float floor in new[]{0f,3.8f,7.6f,11.6f})
                    foreach(float x in new[]{-1f,3.5f,12f,16f})
                        Window(g,new(x,2.2f+floor,-1.62f),.7f,.7f,Vec3.UnitZ);
                break;
            case "EngWaterClock":
                Panel(g,new(-2.35f,1.55f,-2.40f),.38f,.65f,Vec3.UnitZ,new(.23f,.29f,.28f),6);
                break;
            case "EngTidePredictor":
                Window(g,new(-2.3f,2.05f,-2.91f),.48f,.64f,Vec3.UnitZ);
                break;
            case "EngFilmProjector":
                Panel(g,new(1.5f,2.7f,-4.43f),.36f,1.7f,Vec3.UnitZ,new(.12f,.17f,.21f),14);
                break;
            case "EngFerrisWheel":
                g.Material=SurfaceMaterial.Paving;
                g.Box(new(0,.105f,0),new(8,.002f,5.8f),new(.48f,.46f,.40f));
                break;
            case "EngTelegraph":
                // Metal feet and contrasting wooden aprons at the two desks.
                foreach(float x in new[]{-9.4f,9.4f})
                {
                    g.Material=SurfaceMaterial.Brass;
                    foreach(float dx in new[]{-1f,1f})
                        g.Box(new(x+dx,.045f,-.75f),new(.075f,.045f,.075f),Gold);
                }
                break;
        }
    }
}
