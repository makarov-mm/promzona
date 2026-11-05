namespace MakarovPhysics.Numerics;

/// <summary>
/// Planck's law and the colour a blackbody actually shows: a real CIE 1931
/// integration over the visible band (compact Wyman-Sloan-Shirley fits of the
/// colour matching functions), XYZ → linear sRGB. A 2000 K coal comes out red
/// and a 15000 K star blue because the spectrum says so, not because someone
/// picked a gradient.
///
/// This lived inside BlackbodyModel until 12.08, when the relativistic
/// flythrough needed the same path: a blackbody Doppler-shifted by D is a
/// blackbody at D·T exactly (B_ν/ν³ is a Lorentz invariant), so colouring a
/// fast observer's view is a temperature lookup and nothing more. There are no
/// module-to-module references in this tree - the honest move was to lift the
/// physics into the shared library rather than copy it. BlackbodyModel keeps
/// its public API and forwards here, so its pinned tests still test this code.
/// </summary>
public static class PlanckColor
{
    /// <summary>Spectral radiance B_λ, W·sr⁻¹·m⁻³.</summary>
    public static double Planck(double lambdaM, double t)
    {
        double x = Constants.H * Constants.C / (lambdaM * Constants.Kb * t);
        if (x > 700) return 0;
        return 2 * Constants.H * Constants.C * Constants.C / Math.Pow(lambdaM, 5) / (Math.Exp(x) - 1);
    }

    private static double Lobe(double x, double mu, double s1, double s2)
    {
        double s = x < mu ? s1 : s2;
        return Math.Exp(-(x - mu) * (x - mu) / (2 * s * s));
    }

    public static double CieX(double nm) => 1.056 * Lobe(nm, 599.8, 37.9, 31.0) + 0.362 * Lobe(nm, 442.0, 16.0, 26.7) - 0.065 * Lobe(nm, 501.1, 20.4, 26.2);
    public static double CieY(double nm) => 0.821 * Lobe(nm, 568.8, 46.9, 40.5) + 0.286 * Lobe(nm, 530.9, 16.3, 31.1);
    public static double CieZ(double nm) => 1.217 * Lobe(nm, 437.0, 11.8, 36.0) + 0.681 * Lobe(nm, 459.0, 26.0, 13.8);

    /// <summary>CIE 1931 chromaticity (x, y) of the blackbody at temperature t -
    /// a point on the Planck locus, integrated over the visible band.</summary>
    public static (double x, double y) Chromaticity(double t)
    {
        (double x, double y, double z) = Tristimulus(t);
        double sum = x + y + z;
        return (x / sum, y / sum);
    }

    /// <summary>Raw CIE XYZ of the Planck spectrum over 380-780 nm.</summary>
    public static (double X, double Y, double Z) Tristimulus(double t)
    {
        double x = 0, y = 0, z = 0;

        for (int nm = 380; nm <= 780; nm++)
        {
            double b = Planck(nm * 1e-9, t);
            x += b * CieX(nm);
            y += b * CieY(nm);
            z += b * CieZ(nm);
        }

        return (x, y, z);
    }

    /// <summary>Linear-light sRGB of the body's glow, normalized to unit peak.</summary>
    public static (double r, double g, double b) BodyColor(double t)
    {
        (double x, double y, double z) = Tristimulus(t);
        double n = Math.Max(1e-30, y);
        x /= n; z /= n; y = 1.0;
        // XYZ → linear sRGB (D65)
        double r = 3.2406 * x - 1.5372 * y - 0.4986 * z;
        double g = -0.9689 * x + 1.8758 * y + 0.0415 * z;
        double bl = 0.0557 * x - 0.2040 * y + 1.0570 * z;
        r = Math.Max(0, r); g = Math.Max(0, g); bl = Math.Max(0, bl);
        double peak = Math.Max(r, Math.Max(g, Math.Max(bl, 1e-9)));
        return (r / peak, g / peak, bl / peak);
    }
}
