using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
namespace MakarovPhysics.Experiments;
/// <summary>Opt-in framing for detailed space hardware; other missions retain their cameras.</summary>
public abstract class EngineeringModule : SpacecraftModule
{
    private int _view;
    protected void Label(Vec3 at,string key,Vec3 color)=>base.Label(at,key);
    protected override float InitialStars=>.60f;
    protected override float NebulaStrength=>0;
    protected virtual Vec3[] WideFrame=>[new(-3.2f,-1.2f,-2),new(3.2f,1.8f,2)];
    protected virtual Vec3[] DetailFrame=>[new(-1.2f,-.8f,-1),new(1.2f,1.4f,1)];
    protected virtual Vec3[] ContextFrame=>WideFrame.Select(p=>p*1.6f).ToArray();
    protected override float ExtraTopInset(float h)=>54/Math.Max(1,h);
    protected override Vec3[] ExplicitFraming
    {
        get
        {
            var limits=_view==1?DetailFrame:_view==2?ContextFrame:WideFrame;
            var lo=limits[0];var hi=limits[1];var corners=new Vec3[8];
            for(int i=0;i<8;i++)corners[i]=new((i&1)==0?lo.X:hi.X,(i&2)==0?lo.Y:hi.Y,(i&4)==0?lo.Z:hi.Z);
            return corners;
        }
    }
    public override void Initialize(IModuleContext context){base.Initialize(context);Camera.Yaw=.46f;Camera.Near=.035f;}
    protected override void AppearanceControls(UiPanel panel)
    {
        var view=panel.Add(new Dropdown(T("HardwareCamera"),[T("HardwareOverview"),T("HardwareDetail"),T("HardwareSurroundings")]));
        view.Changed+=i=>{_view=i;Camera.Yaw=.46f;Camera.Pitch=InitialPitch;Camera.ResetFrame();InvalidateFraming();};
    }
    protected void Backdrop(bool earth=true,bool lunar=false,bool eclipse=false)=>SpaceExhibit.Backdrop(Scene,Camera.Position(),Mission.Time,StarFieldStrength,earth,lunar,eclipse);
}
