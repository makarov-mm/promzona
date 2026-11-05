using MakarovPhysics.Base;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.Experiments;

/// <summary>Opt-in surroundings for working mechanisms. Furniture is presentation geometry;
/// it never adds bodies or contacts to the experiment's PhysicsWorld. The static mesh is uploaded
/// once and participates in the same depth and shadow passes as the moving apparatus.</summary>
public abstract class EngineeringWorkshopExhibit : MechanismExhibit
{
    private StudioSurface? _surroundings;
    protected EngineeringWorkshopExhibit() => Camera.MinDistance = .55f;
    protected override float StudioFloorY => -1.06f;
    protected override EnvironmentKind EnvironmentStyle => Info.Id == "EngScissorLift" ? EnvironmentKind.DaySky : EnvironmentKind.Studio;
    protected override float DetailPitch => Info.Id switch
    {
        "EngPantograph" or "EngWattLinkage" or "EngPeaucellier" => 1.36f,
        "EngCvJoint" or "EngBarrelCam" => .5f,
        _ => .16f
    };
    protected override float DetailYaw => Info.Id == "EngCvJoint" ? .62f : .06f;
    protected override float ObliquePitch => Info.Id is "EngPantograph" or "EngWattLinkage" or "EngPeaucellier" ? 1.05f : .48f;
    protected override (Vec3 Low, Vec3 High) DetailBounds => Info.Id switch
    {
        "EngEscapement" => (new(-.37f,1.16f,-.16f),new(.37f,1.88f,.18f)),
        "EngCvJoint" => (new(-.4f,.24f,-.58f),new(.6f,1.04f,.55f)),
        "EngRatchet" => (new(-.6f,.65f,-.33f),new(.68f,1.65f,.23f)),
        "EngBarrelCam" => (new(-.6f,.25f,-.36f),new(.6f,1.2f,.4f)),
        "EngQuickReturn" => (new(-.8f,.25f,-.4f),new(.85f,1.8f,.4f)),
        "EngTogglePress" => (new(-.62f,.68f,-.32f),new(.76f,2.24f,.32f)),
        _ => (Lower, Upper)
    };

    public override void Initialize(IModuleContext context)
    {
        base.Initialize(context);
        var geometry = EngineeringWorkshopGeometry.Create(Info.Id, Lower, Upper);
        _surroundings = new StudioSurface(geometry.SurfacePoints.Count);
        _surroundings.Update(geometry.SurfacePoints);
    }

    protected override void DrawAdditionalDepth(Mat4 lightSpace) => _surroundings?.DrawDepth(lightSpace);
    protected override void DrawAdditionalOpaque(Mat4 view, Mat4 projection, Vec3 eye, Mat4 lightSpace, uint shadow) =>
        _surroundings?.Draw(view, projection, eye, LightDirection, LightColor, lightSpace, shadow, 0, environmentFill: SurfaceFill);
    public override void Dispose()
    {
        _surroundings?.Dispose();
        _surroundings = null;
        base.Dispose();
    }
}

/// <summary>Three small sets: a precision instrument room, a machine shop and a lift test yard.
/// No baked lettering, textures requiring network access, or per-frame furniture allocation.</summary>
public static class EngineeringWorkshopGeometry
{
    public static DioramaGeometry Create(string id, Vec3 lower, Vec3 upper)
    {
        var g = new DioramaGeometry { CaptureSurface = true };
        var steel = MechanismGeometry.Steel;
        var ink = new Vec3(.085f,.12f,.15f);
        var wood = new Vec3(.38f,.23f,.115f);
        var brass = MechanismGeometry.Brass;
        bool yard = id == "EngScissorLift";
        bool precision = id is "EngEscapement" or "EngPantograph" or "EngWattLinkage" or "EngPeaucellier";
        void Box(Vec3 p, Vec3 h, Vec3 c, SurfaceMaterial m = SurfaceMaterial.Enamel)
        { g.Material = m; g.Box(p,h,c); }
        void Tube(Vec3 a, Vec3 b, float r, Vec3 c, SurfaceMaterial m = SurfaceMaterial.MachinedMetal)
        { g.Material = m; g.Tube(a,b,r,c); }
        float cx = (lower.X+upper.X)/2;
        float hx = Math.Max(.85f,(upper.X-lower.X)/2+.16f);
        float hz = Math.Max(.65f,Math.Max(Math.Abs(lower.Z),Math.Abs(upper.Z))+.12f);
        if (yard)
        {
            // A ground-mounted test fixture, with no foreground fence obscuring the arms.
            Box(new(0,-.09f,0),new(14,.05f,14),new(.33f,.36f,.37f),SurfaceMaterial.Stone);
            for(int i=-5;i<=5;i++)
            {
                Box(new(i*2.5f,-.038f,0),new(.008f,.001f,14),ink);
                Box(new(0,-.038f,i*2.5f),new(14,.001f,.008f),ink);
            }
            foreach(float x in new[]{-.65f,1.55f}) Box(new(x,-.035f,0),new(.025f,.002f,1.05f),new(.82f,.61f,.12f));
            foreach(float z in new[]{-1.05f,1.05f}) Box(new(.45f,-.035f,z),new(1.125f,.002f,.025f),new(.82f,.61f,.12f));
            Box(new(-3,1.45f,-6),new(4,1.5f,.15f),new(.25f,.32f,.34f),SurfaceMaterial.CastMetal);
            for(int i=0;i<18;i++) Box(new(-6.6f+i*.43f,1.45f,-5.83f),new(.018f,1.5f,.025f),new(.36f,.43f,.45f),SurfaceMaterial.MachinedMetal);
            Box(new(-2,1,-5.79f),new(.9f,1,.035f),ink);
            for(int i=0;i<10;i++) Box(new(-2,.12f+i*.2f,-5.74f),new(.86f,.012f,.015f),steel*.65f,SurfaceMaterial.MachinedMetal);
            foreach(float x in new[]{-2.7f,2.7f})
            {
                Tube(new(x,-.04f,-2),new(x,.65f,-2),.045f,new(.82f,.61f,.12f));
                Box(new(x,.3f,-2),new(.048f,.06f,.048f),ink,SurfaceMaterial.Rubber);
            }
            for(int i=0;i<3;i++) Box(new(2.8f,.03f+i*.12f,-3.2f),new(.55f,.04f,.4f),wood,SurfaceMaterial.Wood);
            return g;
        }

        // The original mechanism datum stays unchanged. The table meets the bottom of its plate.
        float tableTop = id is "EngPantograph" or "EngWattLinkage" or "EngPeaucellier" ? -.04f : 0;
        Box(new(cx,tableTop-.075f,0),new(hx,.075f,hz),precision?wood:new(.21f,.25f,.27f),precision?SurfaceMaterial.Wood:SurfaceMaterial.Stone);
        Box(new(cx,tableTop-.16f,0),new(hx*.98f,.085f,hz*.98f),ink,SurfaceMaterial.CastMetal);
        foreach(float x in new[]{cx-hx*.85f,cx+hx*.85f}) foreach(float z in new[]{-hz*.8f,hz*.8f})
        {
            Box(new(x,-.64f,z),new(.055f,.4f,.055f),precision?wood:ink,precision?SurfaceMaterial.Wood:SurfaceMaterial.CastMetal);
            Box(new(x,-1.04f,z),new(.075f,.014f,.075f),ink,SurfaceMaterial.Rubber);
        }
        Box(new(cx,-.37f,0),new(hx*.82f,.14f,hz*.88f),precision?wood*.85f:ink);
        foreach(float x in new[]{cx-hx*.42f,cx+hx*.42f})
        {
            Box(new(x,-.37f,hz*.89f),new(hx*.38f,.115f,.018f),precision?wood*1.15f:new(.16f,.28f,.29f),precision?SurfaceMaterial.Wood:SurfaceMaterial.Enamel);
            Tube(new(x-.14f,-.37f,hz*.93f),new(x+.14f,-.37f,hz*.93f),.012f,precision?brass:steel);
        }
        // A real room corner, placed beyond the complete swept mechanism envelope.
        Box(new(0,-1.09f,0),new(8,.03f,8),precision?new(.25f,.18f,.12f):new(.19f,.23f,.25f),precision?SurfaceMaterial.Wood:SurfaceMaterial.Stone);
        Box(new(0,1.3f,-3.2f),new(6,2.36f,.06f),precision?new(.27f,.32f,.29f):new(.22f,.29f,.33f),SurfaceMaterial.Stone);
        Box(new(-5.3f,1.3f,0),new(.06f,2.36f,3.2f),precision?new(.23f,.28f,.25f):new(.19f,.25f,.29f),SurfaceMaterial.Stone);
        Box(new(0,-.8f,-3.11f),new(6,.2f,.025f),precision?wood:ink,precision?SurfaceMaterial.Wood:SurfaceMaterial.Enamel);
        for(int i=-5;i<=5;i++) Box(new(i*1.1f,-.1f,-3.11f),new(.018f,.48f,.028f),precision?wood*.85f:steel*.4f,precision?SurfaceMaterial.Wood:SurfaceMaterial.CastMetal);
        // Frosted daylight panels provide a quiet backdrop rather than a flat gray sweep.
        float windowX = precision?-2.5f:2.65f;
        Box(new(windowX,1.9f,-3.08f),new(1.1f,.85f,.035f),precision?wood:ink);
        Box(new(windowX,1.9f,-3.035f),new(1.04f,.79f,.01f),new(.38f,.53f,.59f),SurfaceMaterial.Emissive);
        for(int i=-1;i<=1;i++) Box(new(windowX+i*.68f,1.9f,-3.01f),new(.018f,.8f,.02f),ink);
        Box(new(windowX,1.9f,-3.01f),new(1.06f,.018f,.02f),ink);
        float cabinetX = precision?2.8f:-2.75f;
        Box(new(cabinetX,-.15f,-2.72f),new(.65f,.9f,.32f),precision?wood:ink,precision?SurfaceMaterial.Wood:SurfaceMaterial.Enamel);
        for(int i=0;i<4;i++)
        {
            float y=-.8f+i*.39f;
            Box(new(cabinetX,y,-2.37f),new(.6f,.16f,.025f),precision?wood*1.15f:new(.16f,.29f,.3f),precision?SurfaceMaterial.Wood:SurfaceMaterial.Enamel);
            Tube(new(cabinetX-.18f,y,-2.31f),new(cabinetX+.18f,y,-2.31f),.016f,precision?brass:steel);
        }
        if(precision)
        {
            Box(new(1.25f,1.5f,-3.05f),new(.65f,.75f,.035f),wood,SurfaceMaterial.Wood);
            Box(new(1.25f,1.5f,-3),new(.6f,.7f,.012f),new(.52f,.47f,.34f),SurfaceMaterial.Ceramic);
            g.Material=SurfaceMaterial.Brass;
            MachineGeometry.Annulus(g,new(1.25f,1.55f,-2.97f),Vec3.UnitZ,.32f,.33f,.01f,0,MathF.Tau,brass,64);
            Tube(new(.83f,1.12f,-2.97f),new(1.67f,1.93f,-2.97f),.007f,brass);
            for(int i=0;i<5;i++) Box(new(2.4f+i*.18f,.97f,-2.7f),new(.065f,.22f,.16f),new(.14f+i*.035f,.23f,.25f),SurfaceMaterial.Wood);
        }
        else
        {
            Box(new(-2.75f,1.6f,-3.04f),new(.72f,.65f,.035f),ink,SurfaceMaterial.CastMetal);
            // Tool silhouettes on a perforated board, clear of the experiment.
            for(int i=0;i<6;i++)
            {
                float x=-3.28f+i*.21f;
                Tube(new(x,1.15f,-2.96f),new(x,1.85f-i*.05f,-2.96f),.018f,steel);
                g.Material=SurfaceMaterial.MachinedMetal;
                MachineGeometry.Annulus(g,new(x,1.87f-i*.05f,-2.96f),Vec3.UnitZ,.035f,.055f,.025f,0,MathF.PI*1.65f,steel,20);
            }
        }
        return g;
    }
}
