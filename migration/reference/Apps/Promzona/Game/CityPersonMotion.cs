namespace Promzona.Game;

/// <summary>Presentation only; shadow and colour samples at the same time are identical.</summary>
internal sealed class CityPersonMotion
{
    private double _time=double.NaN,_damage;
    private int _from,_to;
    private float _blend=1,_flinch;
    internal float Heading {get;private set;}
    internal float Lean {get;private set;}
    internal (float From,float To,float Blend) Sample(CityPedestrian p,double time,double punch=0,double entry=0)
    {
        bool running = p.Speed > (p.IsPlayer ? 3.8 : 2.1);
        int clip = p switch
        {
            { Dead: true } or { KnockedFor: > 0 } => 0,
            { ScooterId: not 0 } => 5,
            _ when punch > 0 => 4,
            { Aiming: true, Speed: < .12 } => 3,
            { Aiming: true } => running ? 7 : 6,
            { Speed: < .1 } => 0,
            _ => running ? 2 : 1
        };
        double dt=time-_time;
        if(!double.IsFinite(_time)||dt<0||dt>.5){_from=_to=clip;_blend=1;Heading=(float)p.Heading;_flinch=0;}
        else if(dt>0)
        {
            if(clip!=_to){if(clip==_from){(_from,_to)=(_to,_from);_blend=1-_blend;}else{_from=_blend<.5?_from:_to;_to=clip;_blend=0;}}
            _blend=Math.Min(1,_blend+(float)(dt/(clip is 3 or 4?.10:.18)));
            Heading+=(float)(Math.IEEERemainder(p.Heading-Heading,Math.Tau)*(1-Math.Exp(-dt*18)));
            _flinch*=MathF.Exp((float)-dt*9);
            if(p.GunDamage>_damage+.001)_flinch=.23f;
        }
        if(p.Dead||p.KnockedFor>0){_from=_to=0;_blend=1;_flinch=0;}
        Lean=_flinch+(float)(.30*Math.Sin(Math.Clamp(entry,0,1)*Math.PI));
        _damage=p.GunDamage;_time=time;
        float Phase(int c)=>c switch{0=>(float)((time/4.1666665+p.Id*.137)%1)*64,1=>64+(float)(p.Distance*.6875%1)*64,2=>128+(float)(p.Distance*.5%1)*64,3=>192,5=>384,6=>448+(float)(p.Distance*.6875%1)*64,7=>512+(float)(p.Distance*.5%1)*64,_=>256+(float)Math.Clamp(punch*63,0,63)};
        return(Phase(_from),Phase(_to),_blend);
    }
}
