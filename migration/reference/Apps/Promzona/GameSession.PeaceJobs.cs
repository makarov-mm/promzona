using Promzona.Game;
using MakarovPhysics.UI;
namespace Promzona;

public sealed partial class GameSession
{
    private string PeacePrompt(CityTraffic t) => t.JobKind == JobKind.Leaflets
        ? (_ru ? "E — оставить листовки (стойте 3 секунды)" : "E — leave leaflets (stand still for 3 seconds)")
        : t.PeaceEnabled
            ? (_ru ? "E — выключить громкоговоритель" : "E — switch loudspeaker off")
            : (_ru ? "E — включить громкоговоритель" : "E — switch loudspeaker on");

    private void DrawPeaceJob()
    {
        var t=City.Model.Traffic;var d=UiDraw;const float x=22,y=190,width=340;
        bool leaf=t.JobKind==JobKind.Leaflets;
        d.Rect(x,y,width,119,new(.02f,.035f,.03f,.92f));
        d.Rect(x,y,3,119,new(.5f,.82f,.64f));
        d.TextScaled(x+12,y+9,leaf?(_ru?"ЛИСТОВКИ ЗА МИР":"LEAFLETS FOR PEACE"):(_ru?"ГРОМКОГОВОРИТЕЛЬ":"LOUDSPEAKER RUN"),new(.8f,.96f,.84f),.84f);
        string phase=t.PeaceEscape?(_ru?"УЙДИТЕ ОТ РОЗЫСКА":"LOSE THE WANTED LEVEL"):
            (_ru?$"Точка {t.PeaceSitesDone+1}/{t.PeaceSiteCount} · {t.JobDistance:0} м":$"Site {t.PeaceSitesDone+1}/{t.PeaceSiteCount} · {t.JobDistance:0} m");
        d.TextScaled(x+12,y+31,phase,new(.97f,.81f,.43f),.8f);
        string instruction=t.PeaceEscape?(_ru?"Награда после завершения погони":"Reward after the pursuit ends"):
            leaf?(_ru?"В круге: E, затем не двигайтесь":"In the circle: E, then stand still"):
            t.BroadcastAudible?(_ru?"В эфире: «Нет войне!»":"Broadcasting: 'No to war!'"):
            (_ru?"В своей машине: E — включить":"In your mission car: E to switch on");
        d.TextScaled(x+12,y+51,instruction,new(.85f,.88f,.83f),.74f);
        double duration=leaf?3:30;
        d.Rect(x+12,y+74,width-24,5,new(.15f,.22f,.18f));
        d.Rect(x+12,y+74,(width-24)*(float)(t.PeaceEscape?1:Math.Clamp(t.PeaceProgress/duration,0,1)),5,new(.47f,.85f,.62f));
        double left=Math.Max(0,t.JobTimeLeft);
        string progress=t.PeaceEscape?"":$" · {t.PeaceProgress:0.0}/{duration:0} "+(_ru?"с":"s");
        d.TextScaled(x+12,y+90,$"${t.JobFare} · {(int)(left/60)}:{(int)(left%60):00}"+progress,new(.78f,.85f,.76f),.74f);
    }
}
