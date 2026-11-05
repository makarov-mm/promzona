using System.Runtime.InteropServices;

namespace MakarovPhysics.Compute;

/// <summary>
/// Managed wrapper over the XPBD cloth exports of MakarovPhysics.Cuda.dll
/// (see Native/CudaNBody/cloth_cuda.cu - Jacobi XPBD with device-side aero
/// normals and the verbatim curl-noise wind). Resident pattern and the
/// never-throw contract as everywhere.
/// </summary>
public sealed class CudaCloth : IClothBackend
{
    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaClothCreate")]
    private static extern int MpCudaClothCreate(int maxParticles, int maxConstraints, out IntPtr handle);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaClothUpload")]
    private static extern int MpCudaClothUpload(IntPtr handle, int count, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz, byte[] pinned);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaClothUploadConstraints")]
    private static extern int MpCudaClothUploadConstraints(IntPtr handle, int count, int[] ci, int[] cj, float[] l0, byte[] ctype);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaClothStep")]
    private static extern int MpCudaClothStep(IntPtr handle, int gridN, int count, int constraintCount,
        float dt, float gravity, float damp,
        float windSpeed, float aeroCoeff, float aeroMaxRel, float time,
        float ndx, float ndy, float ndz, float noiseFreq, float scroll, float curlStrength,
        int grabbed, float grabX, float grabY, float grabZ,
        float aStruct, float aShear, float aBend, int iterations, float relax,
        int sphereOn, float scx, float scy, float scz, float srad, float floorY,
        float friction);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaClothDownload")]
    private static extern int MpCudaClothDownload(IntPtr handle, int count, float[] x, float[] y, float[] z, float[] vx, float[] vy, float[] vz);

    [DllImport("MakarovPhysics.Cuda.dll", EntryPoint = "MpCudaClothDestroy")]
    private static extern int MpCudaClothDestroy(IntPtr handle);

    private IntPtr _handle;

    /// <summary>Null when the backend cannot start - the caller keeps its CPU path.</summary>
    public static CudaCloth? TryCreate(int maxParticles, int maxConstraints)
    {
        if (!CudaNBody.Available) return null;

        try
        {
            if (MpCudaClothCreate(maxParticles, maxConstraints, out IntPtr handle) != 0 || handle == IntPtr.Zero)
            {
                return null;
            }

            return new CudaCloth
            {
                _handle = handle
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    private CudaCloth() { }

    public bool Upload(int count, float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz, byte[]? pinned)
    {
        if (_handle == IntPtr.Zero) return false;

        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (x is null || x.Length < count || y is null || y.Length < count || z is null || z.Length < count || vx is null || vx.Length < count || vy is null || vy.Length < count || vz is null || vz.Length < count || pinned is null || pinned.Length < count) return false;

        try
        {
            return MpCudaClothUpload(_handle, count, x, y, z, vx, vy, vz, pinned) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool UploadConstraints(int count, int[]? ci, int[]? cj, float[]? l0, byte[]? ctype)
    {
        if (_handle == IntPtr.Zero) return false;

        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (ci is null || ci.Length < count || cj is null || cj.Length < count || l0 is null || l0.Length < count || ctype is null || ctype.Length < count) return false;

        try
        {
            return MpCudaClothUploadConstraints(_handle, count, ci, cj, l0, ctype) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Step(int gridN, int count, int constraintCount,
        float dt, float gravity, float damp,
        float windSpeed, float aeroCoeff, float aeroMaxRel, float time,
        float ndx, float ndy, float ndz, float noiseFreq, float scroll, float curlStrength,
        int grabbed, float grabX, float grabY, float grabZ,
        float aStruct, float aShear, float aBend, int iterations, float relax,
        bool sphereOn, float scx, float scy, float scz, float srad, float floorY,
        float friction)
    {
        if (_handle == IntPtr.Zero) return false;

        try
        {
            return MpCudaClothStep(_handle, gridN, count, constraintCount,
                dt, gravity, damp, windSpeed, aeroCoeff, aeroMaxRel, time,
                ndx, ndy, ndz, noiseFreq, scroll, curlStrength,
                grabbed, grabX, grabY, grabZ,
                aStruct, aShear, aBend, iterations, relax,
                sphereOn ? 1 : 0, scx, scy, scz, srad, floorY, friction) == 0;
        }
        catch
        {
            return false;
        }
    }

    public bool Download(int count, float[]? x, float[]? y, float[]? z, float[]? vx, float[]? vy, float[]? vz)
    {
        if (_handle == IntPtr.Zero) return false;

        // a short array would let the native copy run past
        // the managed buffer (heap corruption) - refuse instead
        if (x is null || x.Length < count || y is null || y.Length < count || z is null || z.Length < count || vx is null || vx.Length < count || vy is null || vy.Length < count || vz is null || vz.Length < count) return false;
       
        try
        {
            return MpCudaClothDownload(_handle, count, x, y, z, vx, vy, vz) == 0;
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
            MpCudaClothDestroy(_handle);
        }
        catch
        {
             /* teardown is best-effort */
        }

        _handle = IntPtr.Zero;
    }
}
