namespace MakarovPhysics.Compute;
/// <summary>Planar finite-volume diffusion with harmonic interface conductance, closed outer
/// faces, optional fixed reservoirs, a distributed source and linear exchange. Identical CPU/GPU stencil.</summary>
public sealed class EnvironmentalField : IDisposable
{
    public const int Stride=8;
    public int Width {get;} public int Height {get;}
    public float Cell {get;}
    public ThermalGridDispatch Dispatch {get;}=new();
    public float[] Data=>Dispatch.Input;
    public double Time {get;private set;}
    public long Revision {get;private set;}
    public double Milliseconds {get;private set;}
    public EnvironmentalField(int width,int height,float cell)
    {
        if(width<4||height<4||!float.IsFinite(cell)||cell<=0)throw new ArgumentException("Invalid field dimensions");
        Width=width;Height=height;Cell=cell;Dispatch.Resize(width*height);
        var p=Dispatch.Parameters;p[0]=width;p[1]=height;p[2]=1;p[3]=cell;
    }
    public int Index(int x,int y)=>(x+Width*y)*Stride;
    public double Stored {get{double sum=0;for(int i=0;i<Width*Height;i++)sum+=Data[i*8]*Data[i*8+3]*Cell*Cell;return sum;}}
    public void ResetClock(){Time=0;Revision++;}
    public void Step(double duration)
    {
        if(!double.IsFinite(duration)||duration<=0)return;
        double maxRatio=0;
        for(int i=0;i<Width*Height;i++){
            int k=i*8;for(int q=0;q<8;q++)if(!float.IsFinite(Data[k+q]))throw new ArgumentException("Non-finite field state");if(Data[k+6]<0)throw new ArgumentException("Negative exchange coefficient");if(!float.IsFinite(Data[k])||!float.IsFinite(Data[k+1])||Data[k+1]<0||!float.IsFinite(Data[k+3])||Data[k+3]<=0)throw new ArgumentException("Invalid field coefficient");
            maxRatio=Math.Max(maxRatio,Data[k+1]/Data[k+3]);
        }
        double limit=.22*Cell*Cell/Math.Max(1e-12,maxRatio),needed=Math.Ceiling(duration/limit);
        if(!double.IsFinite(needed)||needed>2048)throw new ArgumentOutOfRangeException(nameof(duration),"Substep budget exceeded");
        int count=Math.Max(1,(int)needed);Dispatch.Parameters[4]=(float)(duration/count);
        Dispatch.EvaluateBatch(Enumerable.Repeat(7,count).ToArray());Dispatch.Commit();
        Milliseconds=Dispatch.Milliseconds;Time+=duration;Revision++;
    }
    public void Dispose()=>Dispatch.Dispose();
}
