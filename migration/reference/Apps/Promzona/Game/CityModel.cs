namespace Promzona.Game;

/// <summary>Independent game city, lighting clock and physical actors.</summary>
public sealed class CityModel
{
    public readonly record struct Obstacle(double X,double Z,double HalfX,double HalfZ,double Height);
    public readonly record struct Window(double X,double Y,double Z,bool AlongX,int Facing);
    public static Obstacle[] Blocks=>CityLayout.Current.Blocks;
    public static Obstacle[] Furniture=>CityLayout.Current.Furniture;
    public static bool ParkBlock(int i,int j)=>CityLayout.Current.Park(i,j);
    public CityTraffic Traffic {get;}
    public int Seed=>CityLayout.Current.Seed;
    public double Rain {get;private set;}
    public double Wetness {get;private set;}
    private double _weatherClock,_weatherNext=90;
    private Random _weather=new(73921);
    public bool RainTarget {get;private set;}
    public void SetRain(bool enabled){RainTarget=enabled;_weatherNext=_weatherClock+90;}
    // Accelerated illustrative solar day; independent of the traffic signal clock.
    public bool DayNightCycle=true;
    public double CycleMinutes=20,StartHour=7;
    public double Hour {get;private set;}=7;
    private double _dayPhase;
    public double SunHeight=>Math.Sin((Hour-6)*Math.PI/12);
    public double Daylight {get {double t=Math.Clamp((SunHeight+.12)/.38,0,1);return t*t*(3-2*t);}}
    // Phase advances uniformly. Its mapping to local time slows smoothly around
    // 06:00 and 18:00 and still completes exactly 24 hours per configured cycle.
    private static double SolarHour(double phase) => phase + 1.2 * Math.Sin(phase * Math.PI / 6);

    public void SetHour(double hour)
    {
        if (!double.IsFinite(hour)) return;
        Hour = ((hour % 24) + 24) % 24;
        // Saves, console commands and skips specify the displayed time.
        double lo = 0, hi = 24;
        for (int i = 0; i < 48; i++)
        {
            double mid = (lo + hi) * .5;
            if (SolarHour(mid) < Hour) lo = mid;
            else hi = mid;
        }
        _dayPhase = (lo + hi) * .5;
    }

    internal void AdvanceClock(double seconds)
    {
        double minutes = Math.Clamp(double.IsFinite(CycleMinutes) ? CycleMinutes : 10, 2, 30);
        _dayPhase = (_dayPhase + seconds * 24 / (minutes * 60)) % 24;
        Hour = SolarHour(_dayPhase);
    }
    public double Distance=>Traffic.Hero.Distance;
    public double Time=>Traffic.Time;
    public double X=>Traffic.PlayerPosition.X;
    public double Z=>Traffic.PlayerPosition.Z;
    public double Heading=>Traffic.PlayerHeading;
    public double Speed=>Traffic.OnFoot?Traffic.Player.Speed:Traffic.Hero.Speed;
    public DriveInput Input {get;set;}

    public CityModel(Action<double, string>? progress = null, CancellationToken cancel = default)
    {
        Traffic = new CityTraffic(true, progress, cancel);
        SetHour(StartHour);
        _weather = new Random(Seed);
        _weatherNext = 40 + _weather.NextDouble() * 160;
    }

    public void Reset()
    {
        Traffic.Reset();
        SetHour(StartHour);
        Input = default;
        Rain = Wetness = _weatherClock = 0;
        RainTarget = false;
        _weather = new Random(Seed);
        _weatherNext = 40 + _weather.NextDouble() * 160;
        Traffic.Grip = 1;
    }

    public void Step(double dt)
    {
        if(!double.IsFinite(dt)||dt<=0)return;dt=Math.Min(dt,.1);
        Traffic.Night=Hour>=21||Hour<5;Traffic.RolesStep(dt,Hour);Traffic.Step(dt,Input);if(Traffic.TakeHourSkip() is >0 and var skipped)SetHour((Hour+skipped)%24);
        if (DayNightCycle) AdvanceClock(dt);
        _weatherClock+=dt;
        if(_weatherClock>=_weatherNext){RainTarget=!RainTarget;_weatherNext=_weatherClock+(RainTarget?45+_weather.NextDouble()*90:120+_weather.NextDouble()*240);}
        Rain+=( (RainTarget?1:0)-Rain)*(1-Math.Exp(-dt*.16));
        Wetness+=((RainTarget?1:0)-Wetness)*(1-Math.Exp(-dt*(RainTarget?.05:.006)));
        Traffic.Grip=1-.20*Wetness;
    }
}
