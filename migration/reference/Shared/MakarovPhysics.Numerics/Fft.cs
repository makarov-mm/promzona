namespace MakarovPhysics.Numerics;

public static class Fft
{
    /// <summary>
    /// Radix-2 FFT, in-place. re/im of length N = 2^k.
    /// inverse: inverse transform (normalized by 1/N).
    /// For split-step Schrödinger, signal spectra and dispersion curves.
    /// </summary>
    public static void Transform(double[] re, double[] im, bool inverse)
    {
        int n = re.Length;

        if (n == 0 || (n & (n - 1)) != 0) throw new ArgumentException("FFT length must be a power of two.");
        if (im.Length != n) throw new ArgumentException("FFT: re and im must have the same length.");

        // bit-reversal permutation
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;

            for (; (j & bit) != 0; bit >>= 1)
            {
                j ^= bit;
            }

            j |= bit;

            if (i < j)
            {
                (re[i], re[j]) = (re[j], re[i]);
                (im[i], im[j]) = (im[j], im[i]);
            }
        }

        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = 2 * Math.PI / len * (inverse ? 1 : -1);
            double wr = Math.Cos(ang), wi = Math.Sin(ang);

            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;

                for (int j = 0; j < len / 2; j++)
                {
                    int u = i + j, v = i + j + len / 2;

                    double vr = re[v] * cr - im[v] * ci;
                    double vi = re[v] * ci + im[v] * cr;

                    re[v] = re[u] - vr;
                    im[v] = im[u] - vi;

                    re[u] += vr;
                    im[u] += vi;

                    (cr, ci) = (cr * wr - ci * wi, cr * wi + ci * wr);
                }
            }
        }

        if (inverse)
        {
            for (int i = 0; i < n; i++)
            {
                re[i] /= n;
                im[i] /= n;
            }
        }
    }
}
