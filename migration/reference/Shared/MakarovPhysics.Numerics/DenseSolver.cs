namespace MakarovPhysics.Numerics;

/// <summary>
/// Dense linear systems, Gaussian elimination with partial pivoting - the
/// method Structures (truss statics) and Circuits (nodal analysis) each keep a
/// private copy of today. At module scale (tens to a few hundred unknowns)
/// O(n³) with pivoting is the honest, dependency-free answer; anything larger
/// belongs to an iterative or banded solver, not here.
/// </summary>
public static class DenseSolver
{
    /// <summary>
    /// Solve A·x = b. The matrix and right-hand side are copied, not
    /// destroyed - callers reuse their stiffness matrices between frames.
    /// Throws on a numerically singular pivot instead of returning a vector of
    /// Infinities that then renders as a bridge exploding for no visible reason.
    /// </summary>
    public static double[] Solve(double[,] matrix, double[] rhs)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(rhs);

        int n = rhs.Length;

        if (matrix.GetLength(0) != n || matrix.GetLength(1) != n)
        {
            throw new ArgumentException($"DenseSolver: matrix is {matrix.GetLength(0)}x{matrix.GetLength(1)}, rhs has {n} entries.");
        }

        if (n == 0)
        {
            throw new ArgumentException("DenseSolver: empty system.");
        }

        double[,] a = (double[,])matrix.Clone();
        double[] x = (double[])rhs.Clone();

        // Each row's OWN magnitude - scaled partial pivoting, and the reason
        // it is per row rather than one number for the matrix.
        //
        // The previous test compared every pivot against the largest entry
        // anywhere in the matrix. That is invariant to scaling the whole
        // system, which was the point and is still true here, but it is NOT
        // invariant to the rows having different magnitudes - and modified
        // nodal analysis produces exactly that. Circuits stamps 1/1e-3 = 1000
        // siemens for an ammeter shunt and 1e-9 for the Gmin that keeps a
        // floating node solvable; past about two hundred unknowns the global
        // threshold (scale x n x 1e-14) climbs over Gmin itself and the
        // solver calls a perfectly ordinary schematic singular. Measured:
        // a chain with an ammeter and one dead-end node behind an open switch
        // is refused from n = 202 up, and solved at every size with the row
        // scales (08.08).
        //
        // A row is singular when it has no entry in this column comparable to
        // its OWN largest one. An entirely zero row is singular by
        // inspection, and is caught before the elimination starts rather than
        // by dividing by its scale.
        int n2 = n;
        double[] rowScale = new double[n2];

        for (int i = 0; i < n2; i++)
        {
            double worst = 0;

            for (int j = 0; j < n2; j++)
            {
                worst = Math.Max(worst, Math.Abs(a[i, j]));
            }

            if (worst == 0.0)
            {
                throw new ArgumentException($"DenseSolver: row {i} is entirely zero, so the system is singular.");
            }

            rowScale[i] = worst;
        }

        // dimensionless now: the ratio it bounds is pivot/rowScale
        const double relativeFloor = 1e-14;
        double tiny = n2 * relativeFloor;

        for (int col = 0; col < n; col++)
        {
            // partial pivoting on the RATIO, not the raw magnitude
            int pivot = col;
            double best = Math.Abs(a[col, col]) / rowScale[col];

            for (int row = col + 1; row < n; row++)
            {
                double candidate = Math.Abs(a[row, col]) / rowScale[row];

                if (candidate > best)
                {
                    best = candidate; 
                    pivot = row;
                }
            }

            if (best <= tiny)
            {
                throw new ArgumentException($"DenseSolver: matrix is singular to working precision (column {col}).");
            }

            if (pivot != col)
            {
                for (int j = col; j < n; j++)
                {
                    (a[col, j], a[pivot, j]) = (a[pivot, j], a[col, j]);
                }

                (x[col], x[pivot]) = (x[pivot], x[col]);
                (rowScale[col], rowScale[pivot]) = (rowScale[pivot], rowScale[col]);
            }

            for (int row = col + 1; row < n; row++)
            {
                double factor = a[row, col] / a[col, col];
                if (factor == 0) continue;

                for (int j = col; j < n; j++)
                {
                    a[row, j] -= factor * a[col, j];
                }

                x[row] -= factor * x[col];
            }
        }

        for (int row = n - 1; row >= 0; row--)
        {
            double sum = x[row];

            for (int j = row + 1; j < n; j++)
            {
                sum -= a[row, j] * x[j];
            }

            x[row] = sum / a[row, row];
        }

        return x;
    }
}
