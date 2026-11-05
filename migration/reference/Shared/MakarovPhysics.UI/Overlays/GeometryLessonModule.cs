using MakarovPhysics.Base;
using MakarovPhysics.Numerics;
using MakarovPhysics.Renderer;
using System.Globalization;

namespace MakarovPhysics.UI.Overlays;

/// <summary>A planar construction canvas with model-owned geometry, real
/// measurements and point dragging. It never rotates a plane in 3D.</summary>
public abstract class GeometryLessonModule : PhysicsModuleBase
{
    protected virtual bool ShowDiagram => true;
    public GeometryLesson Lesson { get; }
    public GeometryFrame Drawing { get; private set; }=new();
    public UiRect Diagram { get; private set; }
    public bool Animate { get; private set; }=true;
    private SceneDashboard? _dashboard;
    private Checkbox? _animation;
    private UiLabel? _status;
    private readonly List<(Dropdown Pick,GeometryParameter Parameter)> _choices=[];
    private int _revision=-1,_drag=-1;
    private float _unit,_cx,_cy;
    protected GeometryLessonModule(GeometryLesson lesson) { Lesson=lesson;Dt=1.0/60;MaxSubSteps=4; }
    public override SceneBackground Background=>SceneBackground.Diagram;
    public override ModuleCapabilities Capabilities=>ModuleCapabilities.RunPause|ModuleCapabilities.SimulationTime|ModuleCapabilities.AutoStart;
    public override void Initialize(IModuleContext context)
    {
        base.Initialize(context);
        if(context.GetService<UiContext>() is { } ui)
            _dashboard=ui.Root.Add(new SceneDashboard(context,DrawCanvas){InputBounds=()=>ShowDiagram?Diagram:default,Stroke=DragPoint});
    }
    protected override void ResetCore()
    { Lesson.Reset();foreach(var choice in _choices)choice.Pick.Selected=(int)choice.Parameter.Value;Animate=true;if(_animation is not null)_animation.Value=true;_drag=-1;Refresh(); }
    protected override void StepFixed(double dt) { if(Animate)Lesson.Advance(dt); }
    protected override void OnUiFrame()
    {
        Refresh();
        if(_status is not null)_status.Text=Tr(Lesson.Edited&&Drawing.Goal?"Module.Ui.Achieved":"Module.Ui.Task")+"\n"+Tr("Module.Task");
    }
    private void Refresh() { if(_revision==Lesson.Revision)return;Drawing=Lesson.Build();_revision=Lesson.Revision; }
    private void StopAnimation() { Animate=false;if(_animation is not null)_animation.Value=false; }
    public override void BuildUi(object uiRoot)
    {
        if(uiRoot is not UiPanel panel)return;
        for(int i=0;i<Lesson.Parameters.Length;i++)
        {
            int index=i;var parameter=Lesson.Parameters[i];
            if(parameter.Options is { } options)
            {
                var pick=panel.Add(new Dropdown(Tr("Module.Params."+parameter.Key),options.Select(x=>Tr("Module.Modes."+x)).ToArray(),(int)parameter.Value));
                _choices.Add((pick,parameter));
                pick.Changed+=v=>{StopAnimation();Lesson.Set(index,v);Refresh();};
            }
            else panel.Add(new Slider(Tr("Module.Params."+parameter.Key),()=>parameter.Value,v=>{StopAnimation();Lesson.Set(index,v);Refresh();},parameter.Min,parameter.Max,""){Format=parameter.Key=="N"?"0":"0.00"});
        }
        _animation=panel.Add(new Checkbox(Tr("Module.Ui.Animate"),Animate));
        _animation.Changed+=v=>Animate=v;
        panel.Add(new UiButton(Tr("Module.Ui.Reset"),NewExperiment));
        panel.Add(new UiLabel(Tr("Module.Try")){Wrap=true});
        panel.Add(new UiLabel(Tr("Module.Why")){Wrap=true,Dim=true});
        panel.Add(new Separator());_status=panel.Add(new UiLabel(""){Wrap=true});
        panel.Add(new UiLabel(Tr("Module.Note")){Wrap=true,Dim=true});
    }
    public override void Render() { Refresh();Layout(); }
    private void Layout()
    {
        if(_dashboard is null)return;
        var a=_dashboard.Bounds;
        float footer=92;
        Diagram=new UiRect(a.X,a.Y+51,a.W,Math.Max(60,a.H-51-footer));
        // Infinite construction lines are clipped; finite figures determine the fit.
        double left=-2,right=2,bottom=-1.5,top=1.5;
        void Include(GeoPoint p){left=Math.Min(left,p.X);right=Math.Max(right,p.X);bottom=Math.Min(bottom,p.Y);top=Math.Max(top,p.Y);}
        foreach(var m in Drawing.Marks)Include(m.Point);
        foreach(var f in Drawing.Faces)foreach(var p in f.Points)Include(p);
        foreach(var c in Drawing.Circles){Include(c.Centre-new GeoPoint(c.Radius,c.Radius));Include(c.Centre+new GeoPoint(c.Radius,c.Radius));}
        _unit=MathF.Min((Diagram.W-56)/(float)(right-left),(Diagram.H-56)/(float)(top-bottom));
        _cx=Diagram.X+Diagram.W/2-(float)((left+right)/2)*_unit;
        _cy=Diagram.Y+Diagram.H/2+(float)((bottom+top)/2)*_unit;
    }
    public (float X,float Y) ToCanvas(GeoPoint p)=>(_cx+(float)p.X*_unit,_cy-(float)p.Y*_unit);
    public GeoPoint FromCanvas(float x,float y)=>new((x-_cx)/Math.Max(1,_unit),(_cy-y)/Math.Max(1,_unit));
    private void DragPoint(float x,float y,bool first)
    {
        if(first)
        {
            _drag=-1;float best=18*18;
            foreach(var m in Drawing.Marks.Where(m=>m.Handle>=0))
            { var p=ToCanvas(m.Point);float d=(p.X-x)*(p.X-x)+(p.Y-y)*(p.Y-y);if(d<best){best=d;_drag=m.Handle;} }
        }
        if(_drag<0)return;
        StopAnimation();Lesson.Drag(_drag,FromCanvas(x,y));Refresh();
    }
    private static UiColor Ink(int i)=>i switch {1=>SceneDashboard.Gold,2=>SceneDashboard.Green,3=>SceneDashboard.Red,4=>UiColor.FromBytes(184,156,245),_=>SceneDashboard.Blue};
    private void DrawCanvas(Draw2D d,UiRect a)
    {
        Layout();
        d.Text(a.X,a.Y,Tr("Module.Title"),SceneDashboard.Ink);
        string formula=Drawing.Formula;
        while(formula.Length>0&&d.MeasureText(formula)>a.W-12)formula=formula[..^1];
        if(formula.Length<Drawing.Formula.Length&&formula.Length>3)formula=formula[..^3]+"...";
        d.Text(a.X,a.Y+23,formula,SceneDashboard.Muted);
        if(ShowDiagram)
        {
        d.SetClip(Diagram);
        // A light reference grid makes lengths and areas comparable while dragging.
        var lower=FromCanvas(Diagram.X,Diagram.Bottom);var upper=FromCanvas(Diagram.Right,Diagram.Y);
        for(double x=Math.Ceiling(lower.X*2)/2;x<=upper.X;x+=.5)
        {
            var lo=ToCanvas(new GeoPoint(x,lower.Y));var hi=ToCanvas(new GeoPoint(x,upper.Y));
            d.Line(lo.X,lo.Y,hi.X,hi.Y,UiColor.FromBytes(28,39,52));
        }
        for(double y=Math.Ceiling(lower.Y*2)/2;y<=upper.Y;y+=.5)
        {
            var lo=ToCanvas(new GeoPoint(lower.X,y));var hi=ToCanvas(new GeoPoint(upper.X,y));
            d.Line(lo.X,lo.Y,hi.X,hi.Y,UiColor.FromBytes(28,39,52));
        }
        foreach(var face in Drawing.Faces)Fill(d,face);
        foreach(var circle in Drawing.Circles)
        {
            var c=ToCanvas(circle.Centre);
            d.CircleOutline(c.X,c.Y,(float)circle.Radius*_unit,Ink(circle.Ink),1.7f,Math.Clamp((int)(circle.Radius*_unit*1.5),24,144));
        }
        foreach(var line in Drawing.Lines)
        {
            var p=ToCanvas(line.A);var q=ToCanvas(line.B);
            if(line.Dashed)
            {
                float length=MathF.Sqrt((q.X-p.X)*(q.X-p.X)+(q.Y-p.Y)*(q.Y-p.Y));
                int parts=Math.Min(400,Math.Max(1,(int)(length/9)));
                for(int i=0;i<parts;i++)
                {float t=i/(float)parts,u=(i+.5f)/parts;d.Line(p.X+(q.X-p.X)*t,p.Y+(q.Y-p.Y)*t,p.X+(q.X-p.X)*u,p.Y+(q.Y-p.Y)*u,Ink(line.Ink),1);}
            }
            else d.Line(p.X,p.Y,q.X,q.Y,Ink(line.Ink),2);
        }
        var labels=new List<UiRect>();
        foreach(var mark in Drawing.Marks)
        {
            var p=ToCanvas(mark.Point);d.Circle(p.X,p.Y,mark.Handle>=0?5:3,Ink(mark.Ink),20);
            if(mark.Handle>=0)d.CircleOutline(p.X,p.Y,9,Ink(mark.Ink),1,24);
            if(mark.Label.Length>0)
            {
                float labelWidth=d.MeasureText(mark.Label)+4;
                UiRect label=default;
                (float X,float Y)[] offsets=[(10,-20),(-labelWidth-10,-20),(10,7),(-labelWidth-10,7),(10,-42),(-labelWidth-10,-42),(20,29),(-labelWidth-20,29),(20,51),(-labelWidth-20,51)];
                foreach(var offset in offsets)
                {
                    label=new UiRect(Math.Clamp(p.X+offset.X,Diagram.X+2,Math.Max(Diagram.X+2,Diagram.Right-labelWidth)),Math.Clamp(p.Y+offset.Y,Diagram.Y+2,Diagram.Bottom-20),labelWidth,18);
                    if(!labels.Any(r=>label.X<r.Right+3&&label.Right+3>r.X&&label.Y<r.Bottom+2&&label.Bottom+2>r.Y))break;
                }
                float endX=Math.Clamp(p.X,label.X,label.Right),endY=Math.Clamp(p.Y,label.Y,label.Bottom);
                if(MathF.Abs(endY-p.Y)>22||MathF.Abs(endX-p.X)>14)d.Line(p.X,p.Y,endX,endY,SceneDashboard.Muted,1);
                labels.Add(label);d.Text(label.X,label.Y,mark.Label,SceneDashboard.Ink);
            }
        }
        d.SetClip(null);
        }
        float cardY=Diagram.Bottom+9,width=(a.W-16)/3;
        for(int i=0;i<Math.Min(3,Drawing.Readings.Count);i++)
        {
            var value=Drawing.Readings[i];var r=new UiRect(a.X+i*(width+8),cardY,width,68);
            SceneDashboard.Card(d,r,Ink(i));d.Text(r.X+10,r.Y+10,value.Label,SceneDashboard.Muted);
            string text=double.IsFinite(value.Value)?value.Value.ToString("0.#####",CultureInfo.InvariantCulture):"—";
            d.Text(r.X+10,r.Y+37,text,Ink(i));
        }
    }
    private void Fill(Draw2D d,GeometryFace face)
    {
        var points=face.Points.Select(ToCanvas).ToArray();if(points.Length<3)return;
        float lo=MathF.Max(Diagram.Y,points.Min(p=>p.Y)),hi=MathF.Min(Diagram.Bottom,points.Max(p=>p.Y));
        var ink=Ink(face.Ink);var color=new UiColor(.055f+ink.R*.15f,.07f+ink.G*.15f,.09f+ink.B*.15f,1);
        var crossings=new List<float>(points.Length);
        for(float y=lo;y<hi;y+=2)
        {
            crossings.Clear();
            for(int i=0;i<points.Length;i++)
            {
                var p=points[i];var q=points[(i+1)%points.Length];
                if((p.Y<=y&&q.Y>y)||(q.Y<=y&&p.Y>y))crossings.Add(p.X+(q.X-p.X)*(y-p.Y)/(q.Y-p.Y));
            }
            crossings.Sort();for(int i=1;i<crossings.Count;i+=2)d.Line(crossings[i-1],y,crossings[i],y,color,2.1f);
        }
    }
}
