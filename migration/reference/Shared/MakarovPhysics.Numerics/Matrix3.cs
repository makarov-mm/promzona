namespace MakarovPhysics.Numerics;

/// <summary>
/// Real 3 × 3 matrices as double[3, 3]: products, determinant, the
/// characteristic cubic, real eigenpairs, singular values, rank, and the
/// full SVD by Jacobi with rotations as axis and angle - the algebra the
/// Math Suite's LinearTransform, Eigenvectors and SingularValues modules
/// share. Lifted out of LinearTransform on 01.09 so the second module
/// would not carry a second copy of the cubic solver; the SVD came with
/// the third.
///
/// The cubic: Cardano or the trigonometric form for the candidates, Newton
/// on each, the best kept as the anchor, and a deflated quadratic for the
/// other two. Newton alone crawls towards a double root (1.0048 after six
/// steps on a (x − 1)² factor), and a roundoff-level discriminant is snapped
/// to zero so a double root stays double. Held by
/// `Tools/twins/linear_transform_twin.py`.
/// </summary>
public static class Matrix3
{
    public static double[,] Identity() => new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

    public static double[,] RotZ(double degrees)
    {
        double c = Math.Cos(degrees * Math.PI / 180), s = Math.Sin(degrees * Math.PI / 180);
        return new[,] { { c, -s, 0 }, { s, c, 0 }, { 0, 0, 1 } };
    }

    public static double[,] RotY(double degrees)
    {
        double c = Math.Cos(degrees * Math.PI / 180), s = Math.Sin(degrees * Math.PI / 180);
        return new[,] { { c, 0, s }, { 0, 1, 0 }, { -s, 0, c } };
    }

    public static double[,] Diag(double x, double y, double z) => new double[,] { { x, 0, 0 }, { 0, y, 0 }, { 0, 0, z } };

    /// <summary>The proper rotation by `degrees` about `axis` (Rodrigues).</summary>
    public static double[,] Rotation((double X, double Y, double Z) axis, double degrees)
    {
        double n = Norm(axis);
        double x = axis.X / n, y = axis.Y / n, z = axis.Z / n;
        double c = Math.Cos(degrees * Math.PI / 180), s = Math.Sin(degrees * Math.PI / 180), k = 1 - c;

        return new[,]
        {
            { c + x * x * k, x * y * k - z * s, x * z * k + y * s },
            { y * x * k + z * s, c + y * y * k, y * z * k - x * s },
            { z * x * k - y * s, z * y * k + x * s, c + z * z * k },
        };
    }

    /// <summary>Unit axis and angle in degrees, [0, 180], of a PROPER
    /// rotation; at 180° the skew part vanishes and the axis is read off
    /// R + I. The identity reports the z axis and 0°.</summary>
    public static ((double X, double Y, double Z) Axis, double Degrees) AxisAngle(double[,] r)
    {
        double c = Math.Clamp((Trace(r) - 1) / 2, -1.0, 1.0);
        double ang = Math.Acos(c) * 180 / Math.PI;
        var axis = (r[2, 1] - r[1, 2], r[0, 2] - r[2, 0], r[1, 0] - r[0, 1]);
        double n = Norm(axis);
        if (n > 1e-9) return ((axis.Item1 / n, axis.Item2 / n, axis.Item3 / n), ang);
        if (ang < 1e-9) return ((0, 0, 1), 0);
        var best = (0.0, 0.0, 0.0); double bn = -1;

        for (int k = 0; k < 3; k++)
        {
            (double, double, double) colk = (r[0, k] + (k == 0 ? 1 : 0), r[1, k] + (k == 1 ? 1 : 0), r[2, k] + (k == 2 ? 1 : 0));
            double cn = Norm(colk);

            if (cn > bn)
            {
                best = colk;
                bn = cn;
            }
        }

        return ((best.Item1 / bn, best.Item2 / bn, best.Item3 / bn), ang);
    }

    /// <summary>The rotation a fraction t of the way from I to the proper
    /// rotation r, along the geodesic: same axis, t times the angle.</summary>
    public static double[,] Slerp(double[,] r, double t)
    {
        ((double X, double Y, double Z) axis, double deg) = AxisAngle(r);
        return Rotation(axis, t * deg);
    }

    public static double[,] Mul(double[,] a, double[,] b)
    {
        var r = new double[3, 3];

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                r[i, j] = a[i, 0] * b[0, j] + a[i, 1] * b[1, j] + a[i, 2] * b[2, j];
            }
        }

        return r;
    }

    public static (double X, double Y, double Z) Apply(double[,] a, (double X, double Y, double Z) v) =>
        (a[0, 0] * v.X + a[0, 1] * v.Y + a[0, 2] * v.Z,
         a[1, 0] * v.X + a[1, 1] * v.Y + a[1, 2] * v.Z,
         a[2, 0] * v.X + a[2, 1] * v.Y + a[2, 2] * v.Z);

    public static double[,] Transpose(double[,] a)
    {
        var r = new double[3, 3];

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                r[i, j] = a[j, i];
            }
        }

        return r;
    }

    /// <summary>sa·A + sb·B.</summary>
    public static double[,] Add(double[,] a, double[,] b, double sa = 1, double sb = 1)
    {
        var r = new double[3, 3];

        for (int i = 0; i < 3; i++)
        {
            for (int j = 0; j < 3; j++)
            {
                r[i, j] = sa * a[i, j] + sb * b[i, j];
            }
        }

        return r;
    }

    public static double Dot((double X, double Y, double Z) u, (double X, double Y, double Z) v) => u.X * v.X + u.Y * v.Y + u.Z * v.Z;
    public static (double X, double Y, double Z) Cross((double X, double Y, double Z) u, (double X, double Y, double Z) v) => (u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);
    public static double Norm((double X, double Y, double Z) v) => Math.Sqrt(Dot(v, v));

    public static double Det(double[,] a) => 
        a[0, 0] * (a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1]) - 
        a[0, 1] * (a[1, 0] * a[2, 2] - a[1, 2] * a[2, 0]) + 
        a[0, 2] * (a[1, 0] * a[2, 1] - a[1, 1] * a[2, 0]);

    /// <summary>The signed volume of the image of the unit cube: the triple
    /// product of the columns, against the cofactor expansion above (2e−16
    /// in the twin).</summary>
    public static double CubeVolume(double[,] a)
    {
        (double, double, double) c0 = (a[0, 0], a[1, 0], a[2, 0]);
        (double, double, double) c1 = (a[0, 1], a[1, 1], a[2, 1]);
        (double, double, double) c2 = (a[0, 2], a[1, 2], a[2, 2]);
        return Dot(c0, Cross(c1, c2));
    }

    public static double Trace(double[,] a) => a[0, 0] + a[1, 1] + a[2, 2];

    /// <summary>λ³ − c2·λ² + c1·λ − c0: c2 = trace, c1 = the sum of the
    /// principal 2 × 2 minors, c0 = det.</summary>
    public static (double C2, double C1, double C0) CharPoly(double[,] a)
    {
        double c1 = a[1, 1] * a[2, 2] - a[1, 2] * a[2, 1] + a[0, 0] * a[2, 2] - a[0, 2] * a[2, 0] + a[0, 0] * a[1, 1] - a[0, 1] * a[1, 0];
        return (Trace(a), c1, Det(a));
    }

    /// <summary>Real roots of x³ − c2·x² + c1·x − c0, largest first: Cardano
    /// or the trigonometric form for the candidates, Newton on each, the
    /// best kept as the anchor, and a deflated quadratic for the other two -
    /// Newton alone crawls towards a double root, and a roundoff-level
    /// discriminant is snapped to zero so a double root stays double
    /// (twin, 01.09).</summary>
    public static List<double> RealCubicRoots(double c2, double c1, double c0)
    {
        // solved at unit scale: x = s·y with s = max(|c2|, √|c1|, ∛|c0|), the
        // natural size of a root, so every threshold below is relative. Before
        // this (audit (19), M19-01) diag(1e−8, 2e−8, 3e−8) came back as a
        // triple root 2e−8: |p| and |q| were under the absolute 1e−14.
        double scale = Math.Max(Math.Abs(c2), Math.Max(Math.Sqrt(Math.Abs(c1)), Math.Cbrt(Math.Abs(c0))));
        if (scale == 0.0) return [0.0, 0.0, 0.0];

        if (scale != 1.0)
        {
            var unit = RealCubicRoots(c2 / scale, c1 / (scale * scale), c0 / (scale * scale * scale));
            for (int i = 0; i < unit.Count; i++) unit[i] *= scale;
            return unit;
        }

        double p = c1 - c2 * c2 / 3;
        double q = -2 * c2 * c2 * c2 / 27 + c2 * c1 / 3 - c0;
        double disc = q * q / 4 + p * p * p / 27;
        var roots = new List<double>(3);

        if (Math.Abs(p) < 1e-14 && Math.Abs(q) < 1e-14)
        {
            return [c2 / 3, c2 / 3, c2 / 3];
        }

        if (disc > 1e-15)
        {
            double s = Math.Sqrt(disc);
            double u = Math.CopySign(Math.Cbrt(Math.Abs(-q / 2 + s)), -q / 2 + s);
            double v = Math.CopySign(Math.Cbrt(Math.Abs(-q / 2 - s)), -q / 2 - s);
            roots.Add(u + v);
        }
        else
        {
            double r = Math.Sqrt(Math.Max(0.0, -p / 3));
            double arg = p != 0 ? Math.Clamp(3 * q / (2 * p * r), -1.0, 1.0) : 0.0;
            double phi = Math.Acos(arg);
            for (int k = 0; k < 3; k++) roots.Add(2 * r * Math.Cos((phi - 2 * Math.PI * k) / 3));
        }

        double F(double x) => ((x - c2) * x + c1) * x - c0;
        double DF(double x) => (3 * x - 2 * c2) * x + c1;

        // the anchor is the polished candidate with the LARGEST |f'| among those
        // that are roots (|f| < 1e−9 at unit scale) - the best-conditioned one -
        // not the smallest |f|: at a double root f is quadratically small (it
        // even evaluates to exactly 0.0) while the root itself is off by
        // √roundoff ~ 1e−8, and picking it as the anchor carried that error into
        // the answer (twin, scaling test at s = 1e−8 on Symmetric: 1 ± 2.7e−8)
        List<double> polished = [with(3)];

        foreach (double y in roots)
        {
            double x = y + c2 / 3;

            for (int i = 0; i < 8; i++)
            {
                double d = DF(x);
                if (Math.Abs(d) < 1e-300) break;
                x -= F(x) / d;
            }

            polished.Add(x);
        }

        double best = polished[0], bestKey = double.NegativeInfinity;
        bool anyGood = polished.Any(x => Math.Abs(F(x)) < 1e-9);

        foreach (double x in polished)
        {
            double key = anyGood ? (Math.Abs(F(x)) < 1e-9 ? Math.Abs(DF(x)) : double.NegativeInfinity) : -Math.Abs(F(x));

            if (key > bestKey)
            {
                best = x; 
                bestKey = key;
            }
        }

        double b2 = best - c2, b3 = c1 + best * b2;
        double disc2 = b2 * b2 - 4 * b3;
        double quadScale = Math.Max(1.0, Math.Max(best * best, Math.Abs(b3)));
        if (disc2 < -1e-9 * quadScale) return [best];
        double root = disc2 > 1e-12 * quadScale ? Math.Sqrt(disc2) : 0.0;
        List<double> all = [ best, (-b2 + root) / 2, (-b2 - root) / 2 ];
        all.Sort((x, y) => y.CompareTo(x));
        return all;
    }

    /// <summary>When the cubic has ONE real root, the other two are a complex
    /// pair whose common modulus is √(c0 / real): the product of all three
    /// roots is c0. NaN when all three are real.</summary>
    public static double ComplexPairModulus(double c2, double c1, double c0)
    {
        List<double> roots = RealCubicRoots(c2, c1, c0);

        if (roots.Count != 1 || Math.Abs(roots[0]) < 1e-300)
        {
            return double.NaN;
        }

        return Math.Sqrt(Math.Abs(c0 / roots[0]));
    }

    /// <summary>A unit vector in the null space of A − λI: the largest cross
    /// product of two of its rows.</summary>
    public static (double X, double Y, double Z) Eigenvector(double[,] a, double lambda)
    {
        double[,] m = Add(a, Identity(), 1.0, -lambda);
        (double, double, double) Row(int i) => (m[i, 0], m[i, 1], m[i, 2]);
        // thresholds relative to the size of A − λI, not absolute (M19-01)
        double size = Math.Max(Norm(Row(0)), Math.Max(Norm(Row(1)), Norm(Row(2))));
        var best = (0.0, 0.0, 0.0); double bl = -1;

        for (int i = 0; i < 3; i++)
        {
            for (int j = i + 1; j < 3; j++)
            {
                var c = Cross(Row(i), Row(j));
                double n = Norm(c);

                if (n > bl)
                {
                    best = c;
                    bl = n;
                }
            }
        }

        if (bl <= 1e-9 * size * size)
        {
            var row = Row(0);

            for (int i = 1; i < 3; i++)
            {
                if (Norm(Row(i)) > Norm(row))
                {
                    row = Row(i);
                }
            }

            if (size == 0.0 || Norm(row) < 1e-12 * size)
            {
                return (1, 0, 0);
            }

            var t = Cross(row, (1.0, 0.0, 0.0));

            if (Norm(t) < 1e-9 * Norm(row))
            {
                t = Cross(row, (0.0, 1.0, 0.0));
            }

            best = t;
        }

        double nn = Norm(best);
        return (best.Item1 / nn, best.Item2 / nn, best.Item3 / nn);
    }

    /// <summary>Every real eigenpair, largest eigenvalue first.</summary>
    public static List<(double Lambda, (double X, double Y, double Z) V)> RealEigen(double[,] a)
    {
        (double c2, double c1, double c0) = CharPoly(a);
        return [.. RealCubicRoots(c2, c1, c0).Select(lam => (lam, Eigenvector(a, lam)))];
    }

    /// <summary>√ of the eigenvalues of A^TA, largest first.</summary>
    public static double[] SingularValues(double[,] a)
    {
        (double c2, double c1, double c0) = CharPoly(Mul(Transpose(a), a));
        List<double> roots = RealCubicRoots(c2, c1, c0);
        var s = new double[3];
        for (int i = 0; i < 3; i++) s[i] = Math.Sqrt(Math.Max(0.0, i < roots.Count ? roots[i] : 0.0));
        return s;
    }

    /// <summary>Singular values above eps·σ₁: relative, so s·A has the rank of A.</summary>
    public static int Rank(double[,] a, double eps = 1e-9)
    {
        double[] sv = SingularValues(a);
        return sv[0] > 0 ? sv.Count(s => s > eps * sv[0]) : 0;
    }

    public static (double X, double Y, double Z) Column(double[,] m, int k) => (m[0, k], m[1, k], m[2, k]);
    public static double[,] FromColumns((double X, double Y, double Z) c0, (double X, double Y, double Z) c1, (double X, double Y, double Z) c2) => new[,] { { c0.X, c1.X, c2.X }, { c0.Y, c1.Y, c2.Y }, { c0.Z, c1.Z, c2.Z } };

    /// <summary>Eigenvalues (largest first) and orthonormal eigenvectors
    /// (the columns of V) of a SYMMETRIC matrix by cyclic Jacobi: one plane
    /// rotation per off-diagonal element, swept until the off-diagonal
    /// mass is below 1e−30. Unlike the cubic-plus-null-space route it gives
    /// three orthonormal vectors when eigenvalues repeat (A^TA of a
    /// rotation: 1, 1, 1). Held by `Tools/twins/singular_values_twin.py`.</summary>
    public static (double[] Lambda, double[,] V) SymmetricEigen(double[,] s)
    {
        var a = (double[,])s.Clone();
        double[,] v = Identity();

        for (int sweep = 0; sweep < 60; sweep++)
        {
            double off = 0;

            for (int i = 0; i < 3; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    if (i != j)
                    {
                        off += a[i, j] * a[i, j];
                    }
                }
            }

            if (off < 1e-30) break;

            for (int p = 0; p < 3; p++)
            {
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-300) continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.CopySign(1.0, theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    double c = 1 / Math.Sqrt(t * t + 1), sn = t * c;
                    var j = Identity();
                    j[p, p] = c; j[q, q] = c; j[p, q] = sn; j[q, p] = -sn;
                    a = Mul(Mul(Transpose(j), a), j);
                    v = Mul(v, j);
                }
            }
        }

        int[] order = [0, 1, 2];
        Array.Sort(order, (x, y) => a[y, y].CompareTo(a[x, x]));
        double[] lam = [a[order[0], order[0]], a[order[1], order[1]], a[order[2], order[2]]];
        return (lam, FromColumns(Column(v, order[0]), Column(v, order[1]), Column(v, order[2])));
    }

    /// <summary>Each column of an orthonormal V is only defined up to sign.
    /// Of the eight sign patterns four make V proper; of those, the one with
    /// the largest trace - the rotation closest to doing nothing, so a shear
    /// in the xy-plane turns about z and not by 180° about a skew axis.</summary>
    public static double[,] NearestProper(double[,] v)
    {
        double[,] best = v;
        double bestTr = double.NegativeInfinity;

        for (int bits = 0; bits < 8; bits++)
        {
            var cols = new (double, double, double)[3];

            for (int k = 0; k < 3; k++)
            {
                double sg = 1 - 2 * ((bits >> k) & 1);
                (double X, double Y, double Z) c = Column(v, k);
                cols[k] = (sg * c.X, sg * c.Y, sg * c.Z);
            }

            var cand = FromColumns(cols[0], cols[1], cols[2]);
            if (Det(cand) < 0) continue;
            double tr = Trace(cand);

            if (tr > bestTr)
            {
                best = cand;
                bestTr = tr;
            }
        }

        return best;
    }

    /// <summary>A = U·diag(σ)·V^T: σ descending and ≥ 0, V proper and the
    /// nearest such to I, U orthonormal - a reflection exactly when
    /// det A &lt; 0. Right vectors from Jacobi on A^TA; u_i = A·v_i/σ_i, and the
    /// directions a zero σ leaves undecided are completed by cross products
    /// so U stays orthonormal (twin, 01.09).</summary>
    public static (double[,] U, double[] Sigma, double[,] V) Svd(double[,] a)
    {
        (double[] lam, double[,] v0) = SymmetricEigen(Mul(Transpose(a), a));
        double[,] v = NearestProper(v0);
        double[] sigma = [Math.Sqrt(Math.Max(0.0, lam[0])), Math.Sqrt(Math.Max(0.0, lam[1])), Math.Sqrt(Math.Max(0.0, lam[2]))];
        double eps = 1e-9 * Math.Max(sigma[0], 1e-300);
        var u = new (double X, double Y, double Z)?[3];

        for (int k = 0; k < 3; k++)
        {
            if (sigma[k] > eps)
            {
                var w = Apply(a, Column(v, k));
                u[k] = (w.X / sigma[k], w.Y / sigma[k], w.Z / sigma[k]);
            }
        }

        int known = u.Count(x => x.HasValue);

        if (known == 0)
        {
            u[0] = (1, 0, 0); u[1] = (0, 1, 0); u[2] = (0, 0, 1);
        }
        else if (known == 1)
        {
            int k0 = Array.FindIndex(u, x => x.HasValue);
            var p = u[k0]!.Value;
            var t = Cross(p, (1.0, 0.0, 0.0));
            if (Norm(t) < 0.1) t = Cross(p, (0.0, 1.0, 0.0));
            double tn = Norm(t); t = (t.Item1 / tn, t.Item2 / tn, t.Item3 / tn);
            int r0 = (k0 + 1) % 3, r1 = (k0 + 2) % 3;
            if (r0 > r1) (r0, r1) = (r1, r0);
            u[r0] = t;
            u[r1] = Cross(p, t);
        }
        else if (known == 2)
        {
            int rest = Array.FindIndex(u, x => !x.HasValue);
            u[rest] = rest == 2 ? Cross(u[0]!.Value, u[1]!.Value) : Cross(u[(rest + 1) % 3]!.Value, u[(rest + 2) % 3]!.Value);
        }

        return (FromColumns(u[0]!.Value, u[1]!.Value, u[2]!.Value), sigma, v);
    }

    public static (double X, double Y, double Z) FibonacciPoint(int i, int n)
    {
        double golden = Math.PI * (3 - Math.Sqrt(5));
        double y = 1 - 2 * (i + 0.5) / n;
        double r = Math.Sqrt(1 - y * y);
        double th = golden * i;
        return (r * Math.Cos(th), y, r * Math.Sin(th));
    }

    /// <summary>Longest and shortest |A·v| over a sampled unit sphere: the
    /// ellipsoid's semi-axes, read off the picture rather than off A^TA.</summary>
    public static (double Longest, double Shortest) SphereExtremes(double[,] a, int n = 20000)
    {
        double lo = double.PositiveInfinity, hi = 0;

        for (int i = 0; i < n; i++)
        {
            double d = Norm(Apply(a, FibonacciPoint(i, n)));
            lo = Math.Min(lo, d);
            hi = Math.Max(hi, d);
        }

        return (hi, lo);
    }
}
