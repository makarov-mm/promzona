using System.Numerics;
namespace MakarovPhysics.RigidBodies;

/// <summary>Quasi-steady, finite-span foil in a prescribed free surface. SI units. Smooth
/// lift saturation is an educational polar, not measured data or a cavitation model.</summary>
public static class HydroSurfaceElement
{
    public const double Density=1025;
    public readonly record struct Result(Vector3 Force,double Lift,double Drag,double WetFraction,double Alpha);
    public static double SubmergedVolume(double surface,double bottom,double height,double area)
    {
        if(!double.IsFinite(surface+bottom+height+area)||height<=0||area<0)throw new ArgumentOutOfRangeException(nameof(height));
        return area*Math.Clamp(surface-bottom,0,height);
    }
    // Clip a rotated rectangular section against a horizontal waterline. The area and
    // centroid are exact, including fully immersed and inverted rectangles.
    public static (double Volume, Vector3 Centre) Immersion(Vector3 centre,double pitch,double length,double height,double width,double surface)
    {
        if(!float.IsFinite(centre.LengthSquared())||!double.IsFinite(pitch+length+height+width+surface)||length<=0||height<=0||width<=0)throw new ArgumentOutOfRangeException(nameof(length));
        double c=Math.Cos(pitch),s=Math.Sin(pitch);
        var input=new List<(double X,double Y)>();
        foreach(var (x,y) in new[]{(-length/2,-height/2),(length/2,-height/2),(length/2,height/2),(-length/2,height/2)})
            input.Add((centre.X+c*x-s*y,centre.Y+s*x+c*y));
        var output=new List<(double X,double Y)>();
        var a=input[^1];
        foreach(var b in input){bool ai=a.Y<=surface,bi=b.Y<=surface;if(ai!=bi){double t=(surface-a.Y)/(b.Y-a.Y);output.Add((a.X+t*(b.X-a.X),surface));}if(bi)output.Add(b);a=b;}
        if(output.Count<3)return(0,centre);
        double twice=0,cx=0,cy=0; a=output[^1];
        foreach(var b in output){double cross=a.X*b.Y-b.X*a.Y;twice+=cross;cx+=(a.X+b.X)*cross;cy+=(a.Y+b.Y)*cross;a=b;}
        if(Math.Abs(twice)<1e-12)return(0,centre);
        return(Math.Abs(twice)*.5*width,new((float)(cx/(3*twice)),(float)(cy/(3*twice)),centre.Z));
    }
    public static Result Evaluate(Vector3 relativeVelocity,double chordAngle,double depth,double area,double span)
    {
        if(!float.IsFinite(relativeVelocity.LengthSquared())||!double.IsFinite(chordAngle+depth+area+span)||area<=0||span<=0)throw new ArgumentOutOfRangeException(nameof(area));
        double wet=Math.Clamp(depth/.12,0,1);wet=wet*wet*(3-2*wet);
        double u=relativeVelocity.X,w=relativeVelocity.Y,speed=Math.Sqrt(u*u+w*w);
        if(speed<1e-8||wet==0)return new(Vector3.Zero,0,0,wet,0);
        double alpha=chordAngle-Math.Atan2(w,u);
        alpha=Math.Atan2(Math.Sin(alpha),Math.Cos(alpha));
        double aspect=span*span/area,slope=2*Math.PI/(1+2/(.85*aspect));
        // At reversed flow the chord's other edge leads. The odd sin polar keeps a bounded,
        // reversible force; only the small positive-angle part is used for normal takeoff.
        double cl=1.05*Math.Tanh(slope*Math.Sin(2*alpha)/(2*1.05));
        double cd=.018+cl*cl/(Math.PI*.85*aspect)+1.1*Math.Pow(Math.Sin(alpha),2);
        double q=.5*Density*speed*speed*area*wet,lift=q*cl,drag=q*cd;
        return new(new((float)((-drag*u-lift*w)/speed),(float)((lift*u-drag*w)/speed),0),lift,drag,wet,alpha);
    }
}
