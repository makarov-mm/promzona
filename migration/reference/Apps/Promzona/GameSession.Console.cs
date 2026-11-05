using Promzona.Game;
using MakarovPhysics.UI;
namespace Promzona;

public sealed partial class GameSession
{
    public CityConsole Console {get;}=new();
    private float _consoleSlide;
    public bool ConsoleKey(Keys key)
    {
        if(Screen!=GameScreen.Playing)return false;
        if(key is Keys.F1 or Keys.Oemtilde){Console.Open=!Console.Open;Input.ResetAll();return true;}
        if(!Console.Open)return false;
        switch(key){
            case Keys.Escape:Console.Open=false;Input.ResetAll();break;
            case Keys.Enter:
                string line=Console.Submit();
                if(line=="clear")Console.Lines.Clear();else if(line.Length>0)Console.Write(CityConsole.Execute(City.Model,line,v=>City.TimeScale=v));break;
            case Keys.Back:if(Console.Caret>0){Console.Text=Console.Text.Remove(--Console.Caret,1);}break;
            case Keys.Delete:if(Console.Caret<Console.Text.Length)Console.Text=Console.Text.Remove(Console.Caret,1);break;
            case Keys.Left:Console.Caret=Math.Max(0,Console.Caret-1);break;
            case Keys.Right:Console.Caret=Math.Min(Console.Text.Length,Console.Caret+1);break;
            case Keys.Home:Console.Caret=0;break;
            case Keys.End:Console.Caret=Console.Text.Length;break;
            case Keys.Up:Console.History(-1);break;
            case Keys.Down:Console.History(1);break;
            case Keys.Tab:Console.Complete();break;
        }
        return true;
    }
    private void DrawConsole(float width,float height,double dt)
    {
        if(Screen!=GameScreen.Playing){Console.Open=false;_consoleSlide=0;return;}
        _consoleSlide+=(float)((Console.Open?1:0)-_consoleSlide)*(float)(1-Math.Exp(-Math.Clamp(dt,0,.1)*18));
        if(_consoleSlide<.005)return;
        float h=Math.Min(390,height*.48f),y=-h*(1-_consoleSlide);
        var d=UiDraw;d.Rect(0,y,width,h,new UiColor(.015f,.024f,.025f,.97f));d.Rect(0,y+h-2,width,2,new UiColor(.38f,.72f,.49f));
        d.TextScaled(22,y+15,_ru?"КОНСОЛЬ  /  F1 — закрыть  /  help — команды":"CONSOLE  /  F1 — close  /  help — commands",new UiColor(.55f,.87f,.63f),1);
        int count=(int)((h-100)/23);var lines=Console.Lines.TakeLast(count).ToArray();
        for(int i=0;i<lines.Length;i++)d.TextScaled(22,y+50+i*23,lines[i],new UiColor(.8f,.85f,.8f),.88f);
        d.Rect(16,y+h-41,width-32,30,new UiColor(.055f,.075f,.07f));
        string input=Console.Text.Insert(Console.Caret,(Environment.TickCount64/500)%2==0?"|":" ");
        d.TextScaled(24,y+h-36,"> "+input,new UiColor(.95f,.96f,.88f),1);
    }
}
