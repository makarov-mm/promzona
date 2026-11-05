namespace MakarovPhysics.Numerics;
/// <summary>Small analytic building blocks for astronomical teaching models; SI unless named otherwise.</summary>
public static class AstronomyScience
{
    public const double G=6.67430e-11, C=299792458, Au=149597870700, Pc=3.0856775814913673e16, SolarMass=1.98847e30, SolarRadius=6.957e8, Year=31557600, Sigma=5.670374419e-8;
    public static double Rad(double degrees)=>degrees*Math.PI/180;
    public static double Overlap(double r,double s,double d)
    {
        d=Math.Abs(d);
        if(d>=r+s)return 0;
        if(d<=Math.Abs(r-s))return Math.PI*Math.Min(r,s)*Math.Min(r,s);
        return r*r*Math.Acos(Math.Clamp((d*d+r*r-s*s)/(2*d*r),-1,1))+s*s*Math.Acos(Math.Clamp((d*d+s*s-r*r)/(2*d*s),-1,1))-.5*Math.Sqrt(Math.Max(0,(-d+r+s)*(d+r-s)*(d-r+s)*(d+r+s)));
    }
    public static double Kepler(double mean,double eccentricity)
    {
        double e=mean;for(int i=0;i<16;i++)e-=(e-eccentricity*Math.Sin(e)-mean)/(1-eccentricity*Math.Cos(e));return e;
    }
    public static double Hash(int i,int salt=0)
    {
        uint x=unchecked((uint)(i*747796405+salt*2891336453L+277803737));x=((x>>(int)((x>>28)+4))^x)*277803737;x=(x>>22)^x;return x/4294967296d;
    }
    public static double Gaussian(int i,int salt=0)=>Math.Sqrt(-2*Math.Log(Math.Max(1e-12,Hash(i,salt))))*Math.Cos(Math.Tau*Hash(i,salt+1));
    public static double J1(double x)
    {
        // Integral representation: smooth and stable over the plotted first six lobes.
        if(Math.Abs(x)<1e-8)return x/2;
        double sum=0;const int n=160;
        for(int i=0;i<=n;i++){double t=i*Math.PI/n;sum+=(i==0||i==n?1:i%2==0?2:4)*Math.Cos(t-x*Math.Sin(t));}
        return sum/(3*n);
    }
    public static double Airy(double x)=>Math.Abs(x)<1e-8?1:Math.Pow(2*J1(x)/x,2);
}

