using MakarovPhysics.UI;
using MakarovPhysics.Renderer;
namespace Promzona;

// The adapter is deliberately game-local: shared Suite UI is untouched.
internal readonly struct GameUiDraw(Draw2D draw,string language)
{
    public string Translate(string text)=>Ukrainian.Translate(text,language);
    public float Text(float x,float y,string s,UiColor c)=>draw.Text(x,y,Translate(s),c);
    public float TextScaled(float x,float y,string s,UiColor c,float scale)=>draw.TextScaled(x,y,Translate(s),c,scale);
    public float MeasureText(string s)=>draw.MeasureText(Translate(s));
    public void Rect(float x,float y,float w,float h,UiColor c)=>draw.Rect(x,y,w,h,c);
    public void RectOutline(float x,float y,float w,float h,UiColor c,float t=1)=>draw.RectOutline(x,y,w,h,c,t);
    public void Circle(float x,float y,float r,UiColor c,int segments=16)=>draw.Circle(x,y,r,c,segments);
    public void CircleOutline(float x,float y,float r,UiColor c,float t=1,int segments=16)=>draw.CircleOutline(x,y,r,c,t,segments);
    public void Line(float x,float y,float x1,float y1,UiColor c,float t=1)=>draw.Line(x,y,x1,y1,c,t);
    public void DrawIcon(float x,float y,float w,float h,Texture? icon,UiColor tint=default)=>draw.DrawIcon(x,y,w,h,icon,tint);
    public void SetClip(UiRect? rect)=>draw.SetClip(rect);
}
