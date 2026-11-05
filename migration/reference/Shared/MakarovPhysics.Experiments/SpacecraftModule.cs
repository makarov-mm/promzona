using System.Numerics;
using MakarovPhysics.Base;
using MakarovPhysics.Numerics;
using MakarovPhysics.Renderer;
using MakarovPhysics.UI;
using MakarovPhysics.UI.Overlays;
namespace MakarovPhysics.Experiments;
/// <summary>Common mission controls. Every rendered pose is read from the model.
/// Input capture is cleared when UI owns focus, preventing stuck thrust commands.</summary>
public abstract class SpacecraftModule : CosmosModule
{
    protected abstract SpaceExperiment Experiment {get;}
    public SpaceExperiment Mission=>Experiment;
    private KeyboardGuide? _keys;
    private UiLabel? _status;
    private Checkbox? _auto;
    // Masks keep inactive axes from stealing input or changing autopilot state.
    protected virtual Vector3 MoveAxes=>Vector3.One;
    protected virtual Vector3 TurnAxes=>Vector3.Zero;
    protected override bool ShowTimeline=>false;
    protected override bool InstrumentsEnabled=>false;
    protected override int GraphCount=>2;
    protected override float InitialPitch=>.32f;
    protected override Vec3[] ExplicitFraming=>[new(-3.6f,-1.6f,-2.7f),new(3.6f,2.4f,2.7f)];
    protected override float ExtraBottomInset(float h)=>Context is { } c&&c.ViewportWidth/(c.GetService<UiContext>()?.Scale??1)<1000?Math.Min(.30f,(_keys?.LastBounds.H??90)/h):0;
    public override ModuleCapabilities Capabilities=>ModuleCapabilities.RunPause | ModuleCapabilities.AutoStart | ModuleCapabilities.SimulationTime;
    protected SpacecraftModule(){Dt=1d/120;MaxSubSteps=32;}
    protected override void ResetParameters()=>Experiment.Reset();
    protected override void StepFixed(double dt){Experiment.Step(dt);Epoch=Experiment.Time;}
    protected override (string,double,string)[] Readings()=>Experiment.Readings.Select(r=>(r.Key,r.Value,r.Unit)).ToArray();
    protected override void Controls(UiPanel p)
    {
        _status=p.Add(new UiLabel(""){Wrap=true});
        _auto=p.Add(new Checkbox(T("Automatic"),Experiment.Automatic));_auto.Changed+=v=>Experiment.Automatic=v;
        var scenario=p.Add(new Dropdown(T("Scenario"),Experiment.Scenarios.Select(T).ToArray()));scenario.Changed+=v=>{Experiment.Reset(v);InvalidateFraming();};
        Action(p,"Primary",()=>Experiment.Primary());Action(p,"Secondary",()=>Experiment.Secondary());
        foreach(var v in Experiment.Parameters)Parameter(p,v.Key,v.Get,v.Set,v.Min,v.Max,v.Unit);
        foreach(var sw in Experiment.Switches){var box=p.Add(new Checkbox(T(sw.Key),sw.Get()));box.Changed+=v=>sw.Set(v);}
        Action(p,"Restart",()=>{Experiment.Reset(Experiment.Scenario);InvalidateFraming();});
        p.Add(new UiLabel(T("TimeMeaning")){Wrap=true,Dim=true});
    }
    public override void Initialize(IModuleContext c)
    {
        base.Initialize(c);
        if(c.GetService<UiContext>() is { } ui)
        {
            List<KeyboardHint> hints=[];
            if(MoveAxes.X>0&&MoveAxes.Z>0)hints.Add(new(T("MoveKeys"),new GuideKey("W",0x57),new GuideKey("A",0x41),new GuideKey("S",0x53),new GuideKey("D",0x44)));
            else if(MoveAxes.X>0)hints.Add(new(T("MoveKeys"),new GuideKey("A",0x41),new GuideKey("D",0x44)));
            else if(MoveAxes.Z>0)hints.Add(new(T("MoveKeys"),new GuideKey("W",0x57),new GuideKey("S",0x53)));
            if(MoveAxes.Y>0)hints.Add(new(T("VerticalKeys"),new GuideKey("R",0x52),new GuideKey("F",0x46)));
            if(TurnAxes.X>0||TurnAxes.Y>0)hints.Add(new(T("RotateKeys"),new GuideKey("←",0x25),new GuideKey("↑",0x26),new GuideKey("↓",0x28),new GuideKey("→",0x27)));
            if(TurnAxes.Z>0)hints.Add(new(T("RollKeys"),new GuideKey("Q",0x51),new GuideKey("E",0x45)));
            hints.Add(new(T("PrimaryKey"),new GuideKey("G",0x47)));
            hints.Add(new(T("SecondaryKey"),new GuideKey("B",0x42)));
            hints.Add(new(T("AutoKey"),new GuideKey("M",0x4D)));
            _keys=ui.Root.Add(new KeyboardGuide(c,T("ManualControls"),hints.ToArray()));
        }
    }

    protected override void OnInput(InputState input)
    {
        base.OnInput(input);_keys?.Update(input);Experiment.Move=Experiment.Turn=Vector3.Zero;
        if(input.SceneKeyboardBlocked)return;
        float Pair(int positive,int negative)=>(input.IsKeyDown(positive)?1:0)-(input.IsKeyDown(negative)?1:0);
        Experiment.Move=new Vector3(Pair(0x44,0x41),Pair(0x52,0x46),Pair(0x57,0x53))*MoveAxes;
        Experiment.Turn=new Vector3(Pair(0x26,0x28),Pair(0x27,0x25),Pair(0x45,0x51))*TurnAxes;
        if(Experiment.Move.LengthSquared()+Experiment.Turn.LengthSquared()>0)Experiment.Automatic=false;
        if(input.WasKeyPressed(0x4D))Experiment.Automatic=!Experiment.Automatic;
        if(input.WasKeyPressed(0x47))Experiment.Primary();
        if(input.WasKeyPressed(0x42))Experiment.Secondary();
    }
    protected override void OnUiFrame(){base.OnUiFrame();if(_auto!=null)_auto.Value=Experiment.Automatic;if(_status!=null)_status.Text=T(Experiment.Automatic?"Automatic":"Manual")+" · "+T(Experiment.Status);}
    protected override void DrawOverlay(Draw2D d,UiRect a)
    {
        var data=Experiment.Readings;int count=Math.Min(3,data.Length);float w=(a.W-16)/count;
        for(int i=0;i<count;i++){var rect=new UiRect(a.X+i*(w+8),a.Y+3,w,49);SceneDashboard.Card(d,rect,i==1?SceneDashboard.Gold:SceneDashboard.Blue);string label=T(data[i].Key);float f=Math.Min(.82f,(w-18)/Math.Max(1,d.MeasureText(label)));d.TextScaled(rect.X+9,rect.Y+7,label,SceneDashboard.Muted,f);d.TextScaled(rect.X+9,rect.Y+27,$"{data[i].Value:G4} {data[i].Unit}",SceneDashboard.Ink,.87f);}
    }
    protected static Vec3 V(Vector3 v)=>new(v.X,v.Y,v.Z);
}
