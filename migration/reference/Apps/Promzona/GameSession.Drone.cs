using MakarovPhysics.UI;
using Promzona.Game;
namespace Promzona;

public sealed partial class GameSession
{
    private void DrawDroneHud()
    {
        var t=City.Model.Traffic;if(!t.DroneActive)return;
        var d=UiDraw;float w=ViewportWidth/Ui.Scale,h=ViewportHeight/Ui.Scale;
        UiColor ink=new(.45f,.95f,.78f,.9f);
        for(int sx=-1;sx<=1;sx+=2)for(int sy=-1;sy<=1;sy+=2){
            float x=w*.5f+sx*65,y=h*.5f+sy*45;
            d.Line(x,y,x-sx*20,y,ink,2);d.Line(x,y,x,y-sy*16,ink,2);
        }
        d.Line(w*.5f-7,h*.5f,w*.5f+7,h*.5f,ink,1);
        d.Line(w*.5f,h*.5f-7,w*.5f,h*.5f+7,ink,1);
        d.Rect(w*.5f-225,23,450,65,new(.02f,.045f,.04f,.9f));
        d.TextScaled(w*.5f-210,31,_ru?"РАЗВЕДКА КВАРТАЛА · БЕЗ ОРУЖИЯ":"DISTRICT RECON · UNARMED",ink,.95f);
        if(t.DroneScans<t.DroneTargets.Length){
            var b=t.DroneTargets[t.DroneScans];var delta=new CityPoint(b.X,b.Z)-t.DronePosition;
            double turn=Math.IEEERemainder(Math.Atan2(delta.X,delta.Z)-t.DroneHeading,Math.Tau)*180/Math.PI;
            string dir=Math.Abs(turn)<10?(_ru?"впереди":"ahead"):turn>0?(_ru?"справа":"right"):(_ru?"слева":"left");
            string txt=_ru?$"Цель {t.DroneScans+1}/3: {delta.Length:0} м · {dir} {Math.Abs(turn):0}° · поднимитесь над крышей":$"Target {t.DroneScans+1}/3: {delta.Length:0} m · {dir} {Math.Abs(turn):0}° · fly above roof";
            d.TextScaled(w*.5f-210,59,txt,new(.9f,.91f,.8f),.68f);
            var mark=City.DroneMarker(w,h);
            if(mark.Visible){
                float x=mark.X,y=mark.Y;d.Line(x-17,y-17,x+17,y-17,ink,2);d.Line(x+17,y-17,x+17,y+17,ink,2);
                d.Line(x+17,y+17,x-17,y+17,ink,2);d.Line(x-17,y+17,x-17,y-17,ink,2);
                d.TextScaled(x+23,y-8,(_ru?"КРЫША ":"ROOF ")+(t.DroneScans+1),ink,.8f);
            }
        }
        d.TextScaled(w*.5f-170,h-170,_ru?"Наведите рамку на цель и удерживайте ЛКМ":"Frame the roof and hold LMB",ink,.85f);
        if(t.DroneNotice=="range")d.TextScaled(w*.5f-180,100,_ru?"ПРЕДЕЛ СВЯЗИ — вернитесь ближе к оператору":"LINK LIMIT — move closer to the operator",new(1,.7f,.3f),.8f);
    }
}
