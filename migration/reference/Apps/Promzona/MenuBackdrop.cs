using MakarovPhysics.UI;
using MakarovPhysics.Renderer;
namespace Promzona;
internal static class MenuBackdrop
{
    // The fallback skyline used to carry a hand-written "0.10" long after that release.
    private static readonly string Version="PROMZONA / "+(typeof(MenuBackdrop).Assembly.GetName().Version?.ToString(3)??"dev");
    // Native game capture with a dark reading panel; code-drawn skyline is a fallback.
    public static void Draw(Draw2D d,float w,float h,float time,Texture? image=null){
        if(image is not null){d.DrawIcon(0,0,w,h,image,new UiColor(.57f,.60f,.62f,1));d.Rect(0,0,w,h,new(.01f,.016f,.02f,.25f));d.Rect(w*.5f-244,h*.14f,488,h*.74f,new(.012f,.018f,.018f,.78f));return;}
        for(int band=0;band<70;band++){float t=band/70f;d.Rect(0,h*t,w,h/70+1,new(.055f+t*.09f,.075f+t*.075f,.105f+t*.055f));}
        d.Circle(w*.78f,h*.22f,h*.065f,new(.58f,.48f,.30f,.55f));
        for(int layer=0;layer<2;layer++)for(int n=0;n<17;n++){
            float x=n*w/14-35-layer*23,bw=w/17+(n%3)*12,bh=h*(.18f+.035f*((n*7+layer*3)%9)),y=h*.80f-bh+layer*65;
            d.Rect(x,y,bw,bh,new(.055f+layer*.025f,.075f+layer*.022f,.083f+layer*.023f));
            d.Rect(x-3,y-4,bw+6,4,new(.10f,.12f,.115f));
            for(int row=0;row<bh/23-1;row++)for(int col=0;col<bw/17-1;col++){
                bool lit=(n*37+row*11+col*17+layer)%9<2;d.Rect(x+7+col*17,y+12+row*23,7,12,lit?new(.56f,.42f,.21f,.7f):new(.025f,.04f,.045f));
            }
            if(n%3==0){d.Line(x+bw*.7f,y,x+bw*.7f,y-18,new(.13f,.14f,.12f));d.Line(x+bw*.7f-12,y-12,x+bw*.7f+12,y-12,new(.13f,.14f,.12f));}
        }
        d.Rect(0,h*.86f,w,h*.14f,new(.035f,.044f,.042f));
        for(int n=0;n<4;n++){float x=n*w*.36f-30;d.Rect(x,h*.46f,7,h*.43f,new(.035f,.043f,.04f));d.Line(x-40,h*.49f,x+40,h*.49f,new(.035f,.043f,.04f),3);
            for(int line=0;line<3;line++)for(int k=0;k<25;k++){float u=k/25f,v=(k+1)/25f;d.Line(x+u*w*.36f,h*(.48f+line*.015f)+MathF.Sin(u*MathF.PI)*18,x+v*w*.36f,h*(.48f+line*.015f)+MathF.Sin(v*MathF.PI)*18,new(.035f,.04f,.04f));}}
        // Readable menu panel, retaining the original black initialization/loading screens.
        d.Rect(w*.5f-224,h*.14f,448,h*.74f,new(.012f,.018f,.018f,.88f));
        d.TextScaled(25,h-64,Version,new(.39f,.44f,.41f),.75f);
    }
}
