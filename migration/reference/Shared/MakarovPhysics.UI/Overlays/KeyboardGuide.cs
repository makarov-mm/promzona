using MakarovPhysics.Base;
using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Overlays;

public readonly record struct GuideKey(string Caption, int VirtualKey);
public readonly record struct KeyboardHint(string Action, params GuideKey[] Keys);

/// <summary>Non-interactive, DPI-aware keycaps anchored inside the scene, above
/// the host's bottom caption area. Labels belong to the module's localization.</summary>
public sealed class KeyboardGuide(IModuleContext context, string title, KeyboardHint[] hints) : Widget
{
    private readonly HashSet<int> _pressed = [];
    public UiRect LastBounds { get; private set; }
    public SceneInsets FrameInsets(SceneInsets? original)
    {
        float scale=context.GetService<UiContext>()?.Scale??1;
        float h=context.ViewportHeight/scale;
        bool compact=context.ViewportWidth/scale<1000;
        return new SceneInsets{Top=original?.Top??0,Bottom=MathF.Min(.55f,(original?.Bottom??0)+(compact?(LastBounds.H+12)/h:0))};
    }

    public void Update(InputState input)
    {
        _pressed.Clear();
        if (input.SceneKeyboardBlocked) return;
        foreach(var hint in hints)
            foreach(var key in hint.Keys)
                if(input.IsKeyDown(key.VirtualKey)) _pressed.Add(key.VirtualKey);
    }

    public override Widget? HitTest(float x, float y) => null;

    public override void Draw(Draw2D d)
    {
        if(!Shown || hints.Length==0) return;
        float scale=context.GetService<UiContext>()?.Scale ?? 1;
        float width=context.ViewportWidth/scale,height=context.ViewportHeight/scale;
        var insets=context.GetService<SceneInsets>();
        float top=MathF.Max(62,height*(insets?.Top??0)+8);
        float bottom=height*(1-(insets?.Bottom??0))-10;
        if(width<1000)
        {
            const float font=.78f;
            float maxWidth=width-24,rowHeight=28;
            var placed=new List<(KeyboardHint Hint,float X,float Y)>();
            float cursor=10,row=0;
            foreach(var hint in hints)
            {
                float keySpan=hint.Keys.Sum(k=>MathF.Max(20,d.MeasureText(k.Caption)*font+8)+4);
                float span=keySpan+d.MeasureText(hint.Action)*font+18;
                if(cursor+span>maxWidth-10&&cursor>10){cursor=10;row+=rowHeight;}
                placed.Add((hint,cursor,row));cursor+=span;
            }
            float compactHeight=30+row+rowHeight;
            float cardX=12,cardY=MathF.Max(top,bottom-compactHeight);
            LastBounds=new(cardX,cardY,maxWidth,compactHeight);
            SceneDashboard.Card(d,LastBounds,SceneDashboard.Blue);
            d.TextScaled(cardX+10,cardY+7,title,SceneDashboard.Muted,font);
            foreach(var item in placed)
            {
                float kx=cardX+item.X,ky=cardY+26+item.Y;
                foreach(var key in item.Hint.Keys)
                {
                    float kw=MathF.Max(20,d.MeasureText(key.Caption)*font+8);
                    bool down=_pressed.Contains(key.VirtualKey);
                    d.Rect(kx,ky,kw,20,down?UiColor.FromBytes(46,116,164):UiColor.FromBytes(31,43,57));
                    d.RectOutline(kx,ky,kw,20,UiColor.FromBytes(85,107,130));
                    d.TextScaled(kx+4,ky+3,key.Caption,SceneDashboard.Ink,font);kx+=kw+4;
                }
                d.TextScaled(kx+3,ky+3,item.Hint.Action,SceneDashboard.Ink,font);
            }
            return;
        }
        float cardWidth=MathF.Min(350,MathF.Max(180,width-24));
        float keyWidth=hints.Max(h=>h.Keys.Sum(k=>MathF.Max(25,d.MeasureText(k.Caption)+12)+5));
        keyWidth=MathF.Min(keyWidth,cardWidth*.48f);
        var lines=hints.Select(h=>SceneDashboard.Wrap(h.Action,cardWidth-keyWidth-27,d.MeasureText)).ToArray();
        float[] rows=lines.Select(l=>MathF.Max(29,l.Count*18+6)).ToArray();
        float cardHeight=34+rows.Sum();
        // Keep the full instruction block reachable even on compact views.
        float y=MathF.Max(top,bottom-cardHeight),x=MathF.Max(8,width-cardWidth-12);
        LastBounds=new(x,y,cardWidth,cardHeight);
        SceneDashboard.Card(d,LastBounds,SceneDashboard.Blue);
        d.Text(x+12,y+9,title,SceneDashboard.Muted);
        y+=33;
        for(int i=0;i<hints.Length;i++)
        {
            float keyX=x+10;
            foreach(var key in hints[i].Keys)
            {
                float w=MathF.Max(25,d.MeasureText(key.Caption)+12);
                bool down=_pressed.Contains(key.VirtualKey);
                d.Rect(keyX,y,w,23,down?UiColor.FromBytes(46,116,164):UiColor.FromBytes(31,43,57));
                d.RectOutline(keyX,y,w,23,down?SceneDashboard.Blue:UiColor.FromBytes(85,107,130));
                d.Text(keyX+(w-d.MeasureText(key.Caption))*.5f,y+3,key.Caption,SceneDashboard.Ink);
                keyX+=w+5;
            }
            for(int j=0;j<lines[i].Count;j++)d.Text(x+keyWidth+15,y+3+j*18,lines[i][j],SceneDashboard.Ink);
            y+=rows[i];
        }
    }
}
