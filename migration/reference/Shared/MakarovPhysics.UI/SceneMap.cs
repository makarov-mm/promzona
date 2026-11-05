using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>Clickable XZ overview, positive Z up. Twin of MPUI.SceneMap.</summary>
public sealed class SceneMap : Widget
{
    public readonly float MinX, MaxX, MinZ, MaxZ;
    public Action<Draw2D,SceneMap>? Content;
    public Action<float,float>? Picked;
    public SceneMap(float minX,float maxX,float minZ,float maxZ)
    {
        if (!float.IsFinite(minX) || !float.IsFinite(maxX) || !float.IsFinite(minZ) || !float.IsFinite(maxZ)
            || maxX <= minX || maxZ <= minZ) throw new ArgumentOutOfRangeException(nameof(maxX));
        MinX = minX; MaxX = maxX; MinZ = minZ; MaxZ = maxZ;
    }
    public UiRect Plot => new(Rect.X+10,Rect.Y+10,MathF.Max(0,Rect.W-20),MathF.Max(0,Rect.H-20));
    public override float PreferredHeight(float w) => MathF.Max(80,(w-20)*(MaxZ-MinZ)/(MaxX-MinX)+20);
    public bool Contains(float x,float z) => float.IsFinite(x) && float.IsFinite(z) && x >= MinX && x <= MaxX && z >= MinZ && z <= MaxZ;
    public (float x,float y) Point(float x,float z)
    {
        UiRect p = Plot;
        return (p.X+Math.Clamp((x-MinX)/(MaxX-MinX),0,1)*p.W,p.Y+(1-Math.Clamp((z-MinZ)/(MaxZ-MinZ),0,1))*p.H);
    }
    public (float x,float z)? World(float x,float y)
    {
        UiRect p = Plot;
        if (!float.IsFinite(x) || !float.IsFinite(y) || p.W <= 0 || p.H <= 0
            || x < p.X || x > p.Right || y < p.Y || y > p.Bottom) return null;
        return (MinX+(x-p.X)/p.W*(MaxX-MinX),MaxZ-(y-p.Y)/p.H*(MaxZ-MinZ));
    }
    public void Dot(Draw2D d,float x,float z,float radius,UiColor color)
    {
        var p = Point(x,z); d.Rect(p.x-radius,p.y-radius,2*radius,2*radius,color);
    }
    public void Segment(Draw2D d,float x,float z,float x2,float z2,UiColor color)
    {
        var a = Point(x,z); var b = Point(x2,z2); d.Line(a.x,a.y,b.x,b.y,color);
    }
    public void Arrow(Draw2D d,float x,float z,float dx,float dz,UiColor color)
    {
        var p = Point(x,z); float length = MathF.Sqrt(dx*dx+dz*dz);
        if (!float.IsFinite(length) || length <= 0.0001f) return;
        float fx = dx/length, fy = -dz/length, sx = -fy, sy = fx;
        float tx = p.x+fx*6, ty = p.y+fy*6, ax = p.x-fx*4+sx*4, ay = p.y-fy*4+sy*4;
        float bx = p.x-fx*4-sx*4, by = p.y-fy*4-sy*4;
        d.Line(ax,ay,tx,ty,color,2); d.Line(bx,by,tx,ty,color,2); d.Line(ax,ay,bx,by,color);
    }
    public override void Draw(Draw2D d)
    {
        if (!Shown) return;
        d.Rect(Rect.X,Rect.Y,Rect.W,Rect.H,new(0.055f,0.085f,0.095f,1));
        if (Plot.W > 0 && Plot.H > 0) Content?.Invoke(d,this);
        d.RectOutline(Rect.X,Rect.Y,Rect.W,Rect.H,Hovered ? Theme.Accent : Theme.PanelBorder);
    }
    public override bool OnMouseDown(float x,float y,MouseButton b)
    {
        if (!Enabled || !Shown || b != MouseButton.Left || World(x,y) is not { } p) return false;
        Picked?.Invoke(p.x,p.z); return true;
    }
}
