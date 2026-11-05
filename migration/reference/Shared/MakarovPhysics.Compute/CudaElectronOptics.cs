using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over eopt_cuda.cu (see Native/CudaNBody): the electron
/// tube's frame on the device. Same never-throw contract as
/// <see cref="CudaNBody"/>: availability is <see cref="CudaNBody.Available"/>,
/// every failure is a false, the caller keeps its CPU path.
/// </summary>
public sealed class CudaElectronOptics : IElectronOpticsBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaEoptCreate")]
    private static extern int MpCudaEoptCreate(int maxParticles, int gridCells, int deflCells, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaEoptUploadField")]
    private static extern int MpCudaEoptUploadField(IntPtr handle, int gridCells, float[] ez, float[] er, byte[] group, int deflCells, float[] dz, float[] dt);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaEoptUpload")]
    private static extern int MpCudaEoptUpload(IntPtr handle, int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz, int[] birth);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaEoptStep")]
    private static extern int MpCudaEoptStep(IntPtr handle, int n, int substeps, float dt, float tubeLength, float tubeRadius, float screenZ, int nz, int nr, float deflWindow, int dnz, int dnt, float plateYz0, float plateZz0, float deflY, float deflZ, float eOverM);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaEoptDownload")]
    private static extern int MpCudaEoptDownload(IntPtr handle, int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz, byte[] dead);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaEoptDestroy")]
    private static extern int MpCudaEoptDestroy(IntPtr handle);

    private IntPtr _handle;
    private int _capacity, _gridCap, _deflCap;

    /// <summary>Null when CUDA is absent, disabled, or the device refuses.</summary>
    public static CudaElectronOptics? TryCreate(int maxParticles, int gridCells, int deflCells)
    {
        if (maxParticles <= 0 || gridCells <= 0 || deflCells <= 0 || !CudaNBody.Available) return null;

        try
        {
            if (MpCudaEoptCreate(maxParticles, gridCells, deflCells, out IntPtr h) != 0 || h == IntPtr.Zero)
            {
                return null;
            }

            return new CudaElectronOptics
            {
                _handle = h,
                _capacity = maxParticles,
                _gridCap = gridCells, 
                _deflCap = deflCells
            };
        }
        catch
        {
            return null;
        }
    }

    private CudaElectronOptics() { }

    public bool UploadField(int gridCells, float[] ez, float[] er, byte[] group, int deflCells, float[] dz, float[] dt)
    {
        if (_handle == IntPtr.Zero || gridCells <= 0 || gridCells > _gridCap || deflCells <= 0 || deflCells > _deflCap) return false;
        if (ez.Length < gridCells || er.Length < gridCells || group.Length < gridCells || dz.Length < deflCells || dt.Length < deflCells) return false;

        try
        {
            return MpCudaEoptUploadField(_handle, gridCells, ez, er, group, deflCells, dz, dt) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Upload(int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz, int[] birth)
    {
        if (_handle == IntPtr.Zero || n <= 0 || n > _capacity) return false;
        if (x.Length < n || y.Length < n || z.Length < n || vx.Length < n || vy.Length < n || vz.Length < n || birth.Length < n) return false;

        try
        {
            return MpCudaEoptUpload(_handle, n, x, y, z, vx, vy, vz, birth) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int n, int substeps, float dt, float tubeLength, float tubeRadius, float screenZ, int nz, int nr, float deflWindow, int dnz, int dnt, float plateYz0, float plateZz0, float deflY, float deflZ, float eOverM)
    {
        if (_handle == IntPtr.Zero || n <= 0 || n > _capacity || substeps <= 0) return false;

        try
        {
            return MpCudaEoptStep(_handle, n, substeps, dt, tubeLength, tubeRadius, screenZ, nz, nr, deflWindow, dnz, dnt, plateYz0, plateZz0, deflY, deflZ, eOverM) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int n, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz, byte[] dead)
    {
        if (_handle == IntPtr.Zero || n <= 0 || n > _capacity) return false;
        if (x.Length < n || y.Length < n || z.Length < n || vx.Length < n || vy.Length < n || vz.Length < n || dead.Length < n) return false;

        try
        {
            return MpCudaEoptDownload(_handle, n, x, y, z, vx, vy, vz, dead) == 0;
        }
        catch
        {
            return false;
        }
    }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;

        try
        {
            MpCudaEoptDestroy(_handle);
        }
        catch
        {
            // ignored
        }

        _handle = IntPtr.Zero;
    }
}
