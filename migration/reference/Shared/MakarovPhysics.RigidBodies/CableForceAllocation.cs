using System.Numerics;
namespace MakarovPhysics.RigidBodies;

/// <summary>Exact three-force/four-cable equilibrium with a one-dimensional nullspace.
/// Bounds are intersected analytically. A false result is never a reachable pose.</summary>
public static class CableForceAllocation
{
    public static bool TrySolve(Vector3[] directions,Vector3 force,double minimum,double maximum,out double[] tension)
    {
        if(directions.Length!=4||!double.IsFinite(minimum)||!double.IsFinite(maximum)||minimum<0||maximum<minimum||!float.IsFinite(force.LengthSquared()))throw new ArgumentException("Four finite directions and ordered nonnegative limits required.");
        tension=new double[4];double[,] a=new double[3,4];
        for(int j=0;j<4;j++)
        {
            var v=directions[j];if(!float.IsFinite(v.LengthSquared())||v.LengthSquared()<1e-12f)throw new ArgumentException("Invalid direction.");
            v=Vector3.Normalize(v);a[0,j]=v.X;a[1,j]=v.Y;a[2,j]=v.Z;
        }
        double Det(int i,int j,int k)=>a[0,i]*(a[1,j]*a[2,k]-a[2,j]*a[1,k])-a[0,j]*(a[1,i]*a[2,k]-a[2,i]*a[1,k])+a[0,k]*(a[1,i]*a[2,j]-a[2,i]*a[1,j]);
        double[] n=[-Det(1,2,3),Det(0,2,3),-Det(0,1,3),Det(0,1,2)];
        int omit=0;for(int j=1;j<4;j++)if(Math.Abs(n[j])>Math.Abs(n[omit]))omit=j;
        if(Math.Abs(n[omit])<1e-10)return false;
        int[] columns=Enumerable.Range(0,4).Where(j=>j!=omit).ToArray();
        double[,] m=new double[3,4];double[] f=[force.X,force.Y,force.Z];
        for(int i=0;i<3;i++){for(int j=0;j<3;j++)m[i,j]=a[i,columns[j]];m[i,3]=f[i];}
        for(int j=0;j<3;j++)
        {
            int pivot=j;for(int i=j+1;i<3;i++)if(Math.Abs(m[i,j])>Math.Abs(m[pivot,j]))pivot=i;
            if(Math.Abs(m[pivot,j])<1e-12)return false;
            for(int k=j;k<4;k++)(m[j,k],m[pivot,k])=(m[pivot,k],m[j,k]);
            double div=m[j,j];for(int k=j;k<4;k++)m[j,k]/=div;
            for(int i=0;i<3;i++)if(i!=j){double mult=m[i,j];for(int k=j;k<4;k++)m[i,k]-=mult*m[j,k];}
        }
        for(int j=0;j<3;j++)tension[columns[j]]=m[j,3];
        double lo=double.NegativeInfinity,hi=double.PositiveInfinity,nn=0,nt=0;
        for(int j=0;j<4;j++)
        {
            nn+=n[j]*n[j];nt+=n[j]*tension[j];
            if(Math.Abs(n[j])<1e-12){if(tension[j]<minimum-1e-7||tension[j]>maximum+1e-7)return false;continue;}
            double p=(minimum-tension[j])/n[j],q=(maximum-tension[j])/n[j];
            lo=Math.Max(lo,Math.Min(p,q));hi=Math.Min(hi,Math.Max(p,q));
        }
        if(lo>hi+1e-8)return false;
        double lambda=Math.Clamp(-nt/nn,lo,Math.Max(lo,hi));
        for(int j=0;j<4;j++)tension[j]+=lambda*n[j];
        return true;
    }
}
