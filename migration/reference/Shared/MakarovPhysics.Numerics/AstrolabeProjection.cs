namespace MakarovPhysics.Numerics;

/// <summary>South-pole stereographic projection onto the equator. The unit circle is the
/// equator; x points east, y lies on the upper meridian. No atmospheric refraction.</summary>
public static class AstrolabeProjection
{
    private const double Radians=Math.PI/180;
    public static double WrapDegrees(double a)=>((a+180)%360+360)%360-180;
    public static double WrapHours(double h)=>(h%24+24)%24;
    public static (double Altitude,double Azimuth) Horizontal(double declination,double hourAngle,double latitude)
    {
        Check(declination,-90,90);Check(latitude,-90,90);Check(hourAngle,-1e9,1e9);
        double d=declination*Radians,h=hourAngle*Radians,p=latitude*Radians;
        double east=-Math.Cos(d)*Math.Sin(h),north=Math.Cos(p)*Math.Sin(d)-Math.Sin(p)*Math.Cos(d)*Math.Cos(h);
        double up=Math.Sin(p)*Math.Sin(d)+Math.Cos(p)*Math.Cos(d)*Math.Cos(h);
        return(Math.Asin(Math.Clamp(up,-1,1))/Radians,(Math.Atan2(east,north)/Radians+360)%360);
    }
    public static bool TryProject(double declination,double hourAngle,out (double X,double Y) point)
    {
        Check(declination,-90,90);Check(hourAngle,-1e9,1e9);
        double d=declination*Radians,den=1+Math.Sin(d),h=hourAngle*Radians;
        point=default;if(den<1e-12)return false;
        double r=Math.Cos(d)/den;point=(-r*Math.Sin(h),r*Math.Cos(h));return true;
    }
    public static (double Declination,double HourAngle) Inverse(double x,double y)
    {
        Check(x,-1e6,1e6);Check(y,-1e6,1e6);double r2=x*x+y*y;
        return(Math.Asin(Math.Clamp((1-r2)/(1+r2),-1,1))/Radians,Math.Atan2(-x,y)/Radians);
    }
    public static bool TryHorizontalPoint(double altitude,double azimuth,double latitude,out (double X,double Y) point)
    {
        Check(altitude,-90,90);Check(azimuth,-1e9,1e9);Check(latitude,-90,90);
        double a=altitude*Radians,z=azimuth*Radians,p=latitude*Radians;
        double east=Math.Cos(a)*Math.Sin(z),north=Math.Cos(a)*Math.Cos(z),up=Math.Sin(a);
        double sd=north*Math.Cos(p)+up*Math.Sin(p), meridian=up*Math.Cos(p)-north*Math.Sin(p);
        double den=1+sd;point=default;if(den<1e-12)return false;
        point=(east/den,meridian/den);return true;
    }
    public static bool TryHourAngle(double altitude,double declination,double latitude,bool rising,out double hourAngle)
    {
        Check(altitude,-90,90);Check(declination,-90,90);Check(latitude,-90,90);
        double d=declination*Radians,p=latitude*Radians,den=Math.Cos(p)*Math.Cos(d);
        hourAngle=double.NaN;if(Math.Abs(den)<1e-12)return false;
        double c=(Math.Sin(altitude*Radians)-Math.Sin(p)*Math.Sin(d))/den;
        if(c< -1-1e-12||c>1+1e-12)return false;
        hourAngle=Math.Acos(Math.Clamp(c,-1,1))/Radians*(rising?-1:1);return true;
    }
    private static void Check(double v,double lo,double hi){if(!double.IsFinite(v)||v<lo||v>hi)throw new ArgumentOutOfRangeException(nameof(v));}
}
