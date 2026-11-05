using Promzona.Game;
using MakarovPhysics.Base;
using MakarovPhysics.UI;
using System.Text.RegularExpressions;
namespace Promzona;

public sealed partial class GameSession
{
    private readonly record struct ControlHint(string[] Keys, string Text);
    // "F / W — text": compiled once by the regex source generator instead of
    // going through the static Regex cache for every hint item of every frame.
    [GeneratedRegex(@"^(?<key>[A-Z0-9]+(?: / [A-Z0-9]+)*)\s*—\s*(?<text>.*)$")]
    private static partial Regex HintPattern();
    private void DrawControlStrip(float width,float height,string interaction,string? speech)
    {
        var t=City.Model.Traffic;
        var d=UiDraw;
        var actions=new List<ControlHint>();
        void Add(string keys,string ru,string en)=>actions.Add(new(keys.Split('/'),_ru?ru:en));
        if (!t.PlayerUnavailable)
        {
            if(t.DroneActive){
                Add("W/A/S/D","Дрон: движение","Drone: move");Add("Q/Z","Высота","Altitude");
                Add(_ru?"Мышь":"Mouse","Навести камеру","Aim camera");Add(_ru?"ЛКМ":"LMB","Снять крышу (2 с)","Scan roof (2 s)");Add("F/K","Вернуться","Disconnect");
            }
            else if(t.EnteringCarId>=0) Add("F/W/A/S/D","Отменить посадку","Cancel entry");
            else if(t.PassengerMode)
            {
                Add("B/F",Math.Abs(t.Hero.Speed)<.8?"Выйти":"Выйти после остановки",Math.Abs(t.Hero.Speed)<.8?"Leave":"Leave when stopped");
            }
            else if(t.OnScooter){
                Add("W/A/S/D","Самокат: ехать","Scooter: ride");Add("Space","Тормозить","Brake");Add("F","Сойти","Dismount");Add(_ru?"Мышь":"Mouse","Осмотреться","Look");
            }
            else if(t.OnFoot)
            {
                Add("K","Разведывательный дрон","Recon drone");
                Add("W/A/S/D","Идти","Walk"); Add("Shift","Бежать","Run");
                Add(_ru?"Мышь":"Mouse","Осмотреться","Look");
                Add(_ru?"ЛКМ":"LMB",t.Inventory.Active is null?"Ударить":"Стрелять",t.Inventory.Active is null?"Punch":"Fire");
            }
            else
            {
                int kind=t.Hero.Kind;
                if(kind==12)
                {
                    Add("W/S","Вперёд / назад","Forward / reverse"); Add("A/D","Поворот","Turn");
                    Add("L",t.HelicopterLanding?"Отменить посадку":"Автопосадка",t.HelicopterLanding?"Cancel landing":"Auto-land");
                    Add("Q","Подняться","Ascend"); Add("Z","Снизиться","Descend");
                    Add("Space","Тормозить","Brake");if(Math.Abs(t.Hero.Speed)<1)Add("R","Эвакуация","Recover");
                    Add("F",t.Hero.Lift>.4?"Выход после посадки":"Выйти",t.Hero.Lift>.4?"Land before exit":"Exit");
                }
                else
                {
                    Add("W/S",kind==9?"Вперёд / назад":"Газ / тормоз / назад",kind==9?"Forward / reverse":"Throttle / brake / reverse");
                    if(kind!=9)Add("A/D","Повернуть","Steer");
                    Add("Space",kind==9?"Тормоз":"Ручной тормоз",kind==9?"Brake":"Handbrake");
                    if(kind is 11 or 14){Add(_ru?"Мышь":"Mouse","Башня","Turret");Add(_ru?"ЛКМ":"LMB",kind==14?"Пулемёт":"Выстрелить",kind==14?"Machine gun":"Fire cannon");}
                    else if(t.Inventory.Active is not null) Add(_ru?"ЛКМ":"LMB","Стрелять вперёд","Fire forward");
                    Add("F",Math.Abs(t.Hero.Speed)>2.5?"Выйти после остановки":"Выйти",Math.Abs(t.Hero.Speed)>2.5?"Stop before exit":"Exit");
                    if(Math.Abs(t.Hero.Speed)<1)Add("R","Эвакуация","Recover");
                }
            }
        }
        if (!t.DroneActive && !t.PlayerUnavailable && !t.OnScooter && !t.PassengerMode &&
            t.Inventory.HasWeapons && (t.OnFoot || t.Hero.Kind is not (11 or 14)))
            Add("[/]","Сменить оружие","Switch weapon");
        if(!t.DroneActive)Add("V","Камера","Camera"); Add("Esc","Меню","Menu"); Add("F1","Консоль","Console");
        if(!t.DroneActive&&!t.PlayerUnavailable && t.EnteringCarId<0)Add("J","Задания","Jobs");
        // Interaction hints remain separate from movement: a shop must not hide driving keys.
        var context=new List<ControlHint>();
        if(t.DroneActive){
            string status=_ru?$"РАЗВЕДКА · заряд {t.DroneBattery:0}% · корпус {t.DroneHealth*100:0}% · крыши {t.DroneScans}/{t.DroneTargets.Length} · съёмка {Math.Min(100,t.DroneScanProgress*50):0}% · связь {(t.DronePosition-t.PlayerPosition).Length:0}/170 м":$"RECON · battery {t.DroneBattery:0}% · hull {t.DroneHealth*100:0}% · roofs {t.DroneScans}/{t.DroneTargets.Length} · scan {Math.Min(100,t.DroneScanProgress*50):0}% · link {(t.DronePosition-t.PlayerPosition).Length:0}/170 m";
            context.Add(new([],status));interaction="";
        }else if(t.DroneNotice.Length>0){
            string notice=t.DroneNotice switch{"complete"=>_ru?"Разведка завершена: +$200":"Recon complete: +$200","lost"=>_ru?"Дрон потерян":"Drone lost","battery"=>_ru?"Аккумулятор разряжен":"Battery empty","interrupted"=>_ru?"Связь прервана: оператор ранен":"Operator injured: link lost",_=>""};
            if(notice.Length>0)context.Add(new([],notice));
        }
        if(!t.OnFoot&&t.Hero.Kind==12)context.Add(new([],_ru?$"ВЕРТОЛЁТ · высота {t.Hero.Lift:0.0} м · {(t.HelicopterLanding?"ПОСАДКА":"УДЕРЖАНИЕ / РУЧНОЕ УПРАВЛЕНИЕ")}":$"HELICOPTER · altitude {t.Hero.Lift:0.0} m · {(t.HelicopterLanding?"LANDING":"HOVER / MANUAL")}"));
        foreach(string item in interaction.Split('·',StringSplitOptions.RemoveEmptyEntries|StringSplitOptions.TrimEntries))
        {
            var match=HintPattern().Match(item);
            context.Add(match.Success?new(match.Groups["key"].Value.Replace(" ","").Split('/'),match.Groups["text"].Value):new([],item));
        }
        float left=width>=800?270:12, right=width-12, available=Math.Max(200,right-left);
        const float scale=.84f, keySize=27, gap=5, rowHeight=35;
        float KeyWidth(string key)=>Math.Max(keySize,d.MeasureText(key)*.77f+12);
        float ItemWidth(ControlHint h)=>h.Keys.Sum(k=>KeyWidth(k)+gap)+d.MeasureText(h.Text)*scale+19;
        List<List<ControlHint>> Wrap(List<ControlHint> items)
        {
            List<List<ControlHint>> rows=[];List<ControlHint> row=[];float used=0;
            foreach(var item in items){float w=ItemWidth(item);if(row.Count>0&&used+w>available-20){rows.Add(row);row=[];used=0;}row.Add(item);used+=w;}
            if(row.Count>0)rows.Add(row);return rows;
        }
        var rows=Wrap(actions);var ctxRows=Wrap(context);
        float total=(rows.Count+ctxRows.Count)*rowHeight+16+(speech is null?0:30);
        float top=height-total-12;
        d.Rect(left,top,available,total,new UiColor(.018f,.028f,.025f,.91f));
        d.Rect(left,top,available,2,new UiColor(.25f,.43f,.32f,.85f));
        float y=top+9;
        void Row(List<ControlHint> row,bool highlighted)
        {
            float x=left+10;
            foreach(var item in row)
            {
                foreach(string key in item.Keys)
                {
                    float kw=KeyWidth(key);
                    d.Rect(x,y,kw,keySize,new UiColor(.42f,.52f,.45f));
                    d.Rect(x+1,y+1,kw-2,keySize-2,new UiColor(.105f,.14f,.12f));
                    d.Rect(x+2,y+keySize-4,kw-4,2,new UiColor(.06f,.08f,.07f));
                    float tw=d.MeasureText(key)*.77f;
                    d.TextScaled(x+(kw-tw)*.5f,y+4,key,new UiColor(.97f,.96f,.88f),.77f);x+=kw+gap;
                }
                string label=TextLayout.Ellipsize(d.Translate(item.Text),Math.Max(24,right-x-10),s=>d.MeasureText(s)*scale);
                d.TextScaled(x,y+4,label,highlighted?new UiColor(.97f,.83f,.52f):new UiColor(.83f,.88f,.83f),scale);
                x+=d.MeasureText(item.Text)*scale+19;
            }
            y+=rowHeight;
        }
        foreach(var row in ctxRows)Row(row,true);
        foreach(var row in rows)Row(row,false);
        if(speech is not null)d.TextScaled(left+10,y+4,TextLayout.Ellipsize(d.Translate(speech),available-20,s=>d.MeasureText(s)*scale),new UiColor(.98f,.9f,.72f),scale);
    }
}
