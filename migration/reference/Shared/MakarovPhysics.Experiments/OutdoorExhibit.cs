using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
namespace MakarovPhysics.Experiments;

/// <summary>Outdoor counterpart of a tabletop lesson. Shares the apparatus'
/// shadow pass; weather advances in real time and freezes on pause.</summary>
public abstract class OutdoorExhibit : ExhibitModule
{
    private OutdoorSetting? _landscape;
    private readonly PresentationClock _presentation=new();
    protected override bool Table=>false;
    protected override bool StageVisible=>false;
    protected override bool DetailedSite=>true;
    protected override EnvironmentKind EnvironmentStyle=>EnvironmentKind.DaySky;
    protected override Vec3 LightDirection=>OutdoorSetting.Sun;
    protected override Vec3? SkySun=>LightDirection;
    protected override float SurfaceFill=>1.25f;
    protected override double EnvironmentTime=>_presentation.Seconds;
    protected override double SurfaceTime=>_presentation.Seconds;
    protected override (Vec3 Centre,float Radius) ShadowVolume=>(new(0,1,0),18);
    private OutdoorSetting Landscape=>_landscape??=new(g=>OutdoorLessonGeometry.Build(g,Info.Id));
    protected override void OnVisualFrame(double realDt){base.OnVisualFrame(realDt);_presentation.Advance(realDt,State==SimulationState.Running);}
    protected override void ResetCore(){_presentation.Reset();base.ResetCore();}
    protected override void DrawAdditionalDepth(Mat4 ls)=>Landscape.Surface.DrawDepth(ls,(float)SurfaceTime);
    protected override void DrawAdditionalOpaque(Mat4 view,Mat4 proj,Vec3 eye,Mat4 ls,uint shadow)=>
        Landscape.Surface.Draw(view,proj,eye,LightDirection,LightColor,ls,shadow,(float)SurfaceTime,environmentFill:SurfaceFill);
    public override void Dispose(){_landscape?.Dispose();_landscape=null;base.Dispose();}
}
