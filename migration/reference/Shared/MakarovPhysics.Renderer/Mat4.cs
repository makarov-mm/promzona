namespace MakarovPhysics.Renderer;

/// <summary>
/// Allocation-free 4x4 column-major matrix compatible with OpenGL.
/// Matrices compose with the multiplication operator: <c>A * B</c> applies B first.
/// </summary>
public struct Mat4 : IEquatable<Mat4>
{
    internal Mat4Elements M;

    public float this[int row, int column]
    {
        readonly get
        {
            ValidateIndex(row, column);
            return M[column * 4 + row];
        }

        set
        {
            ValidateIndex(row, column);
            M[column * 4 + row] = value;
        }
    }

    public static Mat4 Identity
    {
        get
        {
            Mat4 result = default(Mat4);
            result[0, 0] = result[1, 1] = result[2, 2] = result[3, 3] = 1f;
            return result;
        }
    }

    public readonly bool IsFinite
    {
        get
        {
            for (int i = 0; i < 16; i++)
                if (!float.IsFinite(M[i]))
                    return false;

            return true;
        }
    }

    public static Mat4 operator +(Mat4 value) => value;

    public static Mat4 operator -(Mat4 value)
    {
        Mat4 result = default(Mat4);

        for (int i = 0; i < 16; i++)
        {
            result.M[i] = -value.M[i];
        }

        return result;
    }

    public static Mat4 operator +(Mat4 left, Mat4 right)
    {
        Mat4 result = default(Mat4);

        for (int i = 0; i < 16; i++)
        {
            result.M[i] = left.M[i] + right.M[i];
        }

        return result;
    }

    public static Mat4 operator -(Mat4 left, Mat4 right)
    {
        Mat4 result = default(Mat4);

        for (int i = 0; i < 16; i++)
        {
            result.M[i] = left.M[i] - right.M[i];
        }

        return result;
    }

    /// <summary>C = A * B; B applies first, then A.</summary>
    public static Mat4 operator *(Mat4 left, Mat4 right)
    {
        // Work directly with the column-major storage. This is on a hot render path,
        // so avoid bounds checks from the public row/column indexer in the inner loop.
        var result = default(Mat4);

        for (int column = 0; column < 4; column++)
        {
            int columnOffset = column * 4;

            for (int row = 0; row < 4; row++)
            {
                result.M[columnOffset + row] =
                    left.M[row] * right.M[columnOffset] +
                    left.M[4 + row] * right.M[columnOffset + 1] +
                    left.M[8 + row] * right.M[columnOffset + 2] +
                    left.M[12 + row] * right.M[columnOffset + 3];
            }
        }

        return result;
    }

    public static Mat4 operator *(Mat4 matrix, float scalar)
    {
        Mat4 result = default(Mat4);

        for (int i = 0; i < 16; i++)
        {
            result.M[i] = matrix.M[i] * scalar;
        }

        return result;
    }

    public static Mat4 operator *(float scalar, Mat4 matrix) => matrix * scalar;
    public static Mat4 operator /(Mat4 matrix, float scalar)
    {
        // A reciprocal can overflow for subnormal divisors even when each quotient is finite.
        var result = default(Mat4);

        for (int i = 0; i < 16; i++)
        {
            result.M[i] = matrix.M[i] / scalar;
        }

        return result;
    }

    public static bool operator ==(Mat4 left, Mat4 right) => left.Equals(right);
    public static bool operator !=(Mat4 left, Mat4 right) => !left.Equals(right);

    public static Mat4 Translate(float x, float y, float z)
    {
        Mat4 result = Identity;
        result[0, 3] = x;
        result[1, 3] = y;
        result[2, 3] = z;
        return result;
    }

    public static Mat4 Translate(Vec3 translation) => Translate(translation.X, translation.Y, translation.Z);

    public static Mat4 Scale(float x, float y, float z)
    {
        Mat4 result = Identity;
        result[0, 0] = x;
        result[1, 1] = y;
        result[2, 2] = z;
        return result;
    }

    public static Mat4 Scale(float uniformScale) => Scale(uniformScale, uniformScale, uniformScale);
    public static Mat4 Scale(Vec3 scale) => Scale(scale.X, scale.Y, scale.Z);

    public static Mat4 RotateX(float radians)
    {
        if (!float.IsFinite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        Mat4 result = Identity;
        result[1, 1] = c;
        result[1, 2] = -s;
        result[2, 1] = s;
        result[2, 2] = c;
        return result;
    }

    public static Mat4 RotateY(float radians)
    {
        if (!float.IsFinite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        Mat4 result = Identity;
        result[0, 0] = c;
        result[0, 2] = s;
        result[2, 0] = -s;
        result[2, 2] = c;
        return result;
    }

    public static Mat4 RotateZ(float radians)
    {
        if (!float.IsFinite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        Mat4 result = Identity;
        result[0, 0] = c;
        result[0, 1] = -s;
        result[1, 0] = s;
        result[1, 1] = c;
        return result;
    }

    /// <summary>Rotation around an arbitrary axis using Rodrigues' formula. A zero axis returns Identity.</summary>
    public static Mat4 Rotate(Vec3 axis, float radians)
    {
        if (!axis.IsFinite) throw new ArgumentOutOfRangeException(nameof(axis));
        if (!float.IsFinite(radians)) throw new ArgumentOutOfRangeException(nameof(radians));
        if (!TryNormalize(axis.X, axis.Y, axis.Z, out Vec3 a)) return Identity;

        float c = MathF.Cos(radians);
        float s = MathF.Sin(radians);
        float t = 1f - c;
        float x = a.X, y = a.Y, z = a.Z;

        var result = Identity;
        result[0, 0] = t * x * x + c;
        result[0, 1] = t * x * y - s * z;
        result[0, 2] = t * x * z + s * y;
        result[1, 0] = t * x * y + s * z;
        result[1, 1] = t * y * y + c;
        result[1, 2] = t * y * z - s * x;
        result[2, 0] = t * x * z - s * y;
        result[2, 1] = t * y * z + s * x;
        result[2, 2] = t * z * z + c;

        return result;
    }

    /// <summary>Right-handed OpenGL orthographic projection with NDC depth in [-1, 1].</summary>
    public static Mat4 Ortho(float left, float right, float bottom, float top, float near = -1f, float far = 1f)
    {
        if (!float.IsFinite(left)
            ||
            !float.IsFinite(right)
            || 
            !float.IsFinite(bottom)
            || 
            !float.IsFinite(top) 
            || 
            !float.IsFinite(near) 
            ||
            !float.IsFinite(far))
        {
            throw new ArgumentException("Orthographic projection requires finite plane values.");
        }

        if (left == right) throw new ArgumentException("Orthographic projection requires left != right.");
        if (bottom == top) throw new ArgumentException("Orthographic projection requires bottom != top.");
        if (near == far) throw new ArgumentException("Orthographic projection requires near != far.");

        // Keep sums and differences in double: finite float planes can overflow their intermediates.
        double width = (double)right - left;
        double height = (double)top - bottom;
        double depth = (double)far - near;

        var result = default(Mat4);
        result[0, 0] = (float)(2d / width);
        result[1, 1] = (float)(2d / height);
        result[2, 2] = (float)(-2d / depth);
        result[0, 3] = (float)(-((double)right + left) / width);
        result[1, 3] = (float)(-((double)top + bottom) / height);
        result[2, 3] = (float)(-((double)far + near) / depth);
        result[3, 3] = 1f;

        return !result.IsFinite
            ? throw new ArgumentException("Orthographic projection coefficients cannot be represented as finite floats.") 
            : result;
    }

    /// <summary>
    /// Right-handed OpenGL perspective projection with NDC depth in [-1, 1].
    /// Field of view is in radians; near and far must be finite and satisfy 0 &lt; near &lt; far.
    /// </summary>
    public static Mat4 Perspective(float fovy, float aspect, float near, float far)
    {
        if (!(fovy > 0f && fovy < MathF.PI))
            throw new ArgumentOutOfRangeException(nameof(fovy), "Field of view must be between 0 and PI radians.");

        if (!(aspect > 0f) || !float.IsFinite(aspect))
            throw new ArgumentOutOfRangeException(nameof(aspect));

        if (!(near > 0f) || !float.IsFinite(near))
            throw new ArgumentOutOfRangeException(nameof(near), "Near plane must be positive and finite.");

        if (!(far > near) || !float.IsFinite(far))
            throw new ArgumentOutOfRangeException(nameof(far), "Far plane must be finite and greater than near.");

        double f = 1d / Math.Tan(fovy / 2d);
        double depth = (double)near - far;
        var result = default(Mat4);
        result[0, 0] = (float)(f / aspect);
        result[1, 1] = (float)f;
        result[2, 2] = (float)(((double)far + near) / depth);
        result[2, 3] = (float)(2d * far * near / depth);
        result[3, 2] = -1f;

        if (!result.IsFinite || result[0, 0] == 0f)
            throw new ArgumentException("Perspective projection coefficients cannot be represented as finite, non-degenerate floats.");

        return result;
    }

    public static Mat4 LookAt(Vec3 eye, Vec3 target, Vec3 up)
    {
        if (!eye.IsFinite) throw new ArgumentOutOfRangeException(nameof(eye));
        if (!target.IsFinite) throw new ArgumentOutOfRangeException(nameof(target));
        if (!up.IsFinite) throw new ArgumentOutOfRangeException(nameof(up));

        double dx = (double)target.X - eye.X;
        double dy = (double)target.Y - eye.Y;
        double dz = (double)target.Z - eye.Z;

        // Cross the original direction with up before rounding a normalized direction to float.
        if (!TryNormalize(dx, dy, dz, out Vec3 forward)
            ||
            !TryNormalize(dy * up.Z - dz * up.Y, dz * up.X - dx * up.Z, dx * up.Y - dy * up.X, out Vec3 side))
        {
            throw new ArgumentException("LookAt requires a non-zero view direction and a non-parallel up vector.");
        }

        Vec3 correctedUp = Vec3.Cross(side, forward);

        Mat4 result = Identity;
        result[0, 0] = side.X;
        result[0, 1] = side.Y;
        result[0, 2] = side.Z;
        result[1, 0] = correctedUp.X;
        result[1, 1] = correctedUp.Y;
        result[1, 2] = correctedUp.Z;
        result[2, 0] = -forward.X;
        result[2, 1] = -forward.Y;
        result[2, 2] = -forward.Z;
        result[0, 3] = (float)(-((double)side.X * eye.X + (double)side.Y * eye.Y + (double)side.Z * eye.Z));
        result[1, 3] = (float)(-((double)correctedUp.X * eye.X + (double)correctedUp.Y * eye.Y + (double)correctedUp.Z * eye.Z));
        result[2, 3] = (float)((double)forward.X * eye.X + (double)forward.Y * eye.Y + (double)forward.Z * eye.Z);

        return !result.IsFinite 
            ? throw new ArgumentException("LookAt coefficients cannot be represented as finite floats.") 
            : result;
    }

    public static Mat4 FromColumns(Vec3 xColumn, Vec3 yColumn, Vec3 zColumn, Vec3 translation)
    {
        Mat4 result = Identity;
        result[0, 0] = xColumn.X;
        result[1, 0] = xColumn.Y;
        result[2, 0] = xColumn.Z;
        result[0, 1] = yColumn.X;
        result[1, 1] = yColumn.Y;
        result[2, 1] = yColumn.Z;
        result[0, 2] = zColumn.X;
        result[1, 2] = zColumn.Y;
        result[2, 2] = zColumn.Z;
        result[0, 3] = translation.X;
        result[1, 3] = translation.Y;
        result[2, 3] = translation.Z;
        return result;
    }

    /// <summary>Transforms a position using homogeneous w = 1. Perspective division is not performed.</summary>
    public readonly Vec3 TransformPoint(Vec3 point) => new(
        M[0] * point.X + M[4] * point.Y + M[8] * point.Z + M[12],
        M[1] * point.X + M[5] * point.Y + M[9] * point.Z + M[13],
        M[2] * point.X + M[6] * point.Y + M[10] * point.Z + M[14]);

    /// <summary>Transforms a direction using homogeneous w = 0.</summary>
    public readonly Vec3 TransformDirection(Vec3 direction) => new(
        M[0] * direction.X + M[4] * direction.Y + M[8] * direction.Z,
        M[1] * direction.X + M[5] * direction.Y + M[9] * direction.Z,
        M[2] * direction.X + M[6] * direction.Y + M[10] * direction.Z);

    public readonly Mat4 Transposed()
    {
        var result = default(Mat4);

        for (int row = 0; row < 4; row++)
            for (int column = 0; column < 4; column++)
                result[row, column] = this[column, row];

        return result;
    }

    /// <summary>
    /// General inverse by cofactor expansion with double-precision intermediates.
    /// Returns false and a zero matrix for a numerically singular or non-finite input, or when
    /// the inverse cannot be represented as finite floats. Ill-conditioned inputs
    /// can still lose accuracy. The output may alias the source matrix.
    /// </summary>
    public readonly bool TryInvert(out Mat4 inverse)
    {
        // Snapshot before writing the out parameter: matrix.TryInvert(out matrix) is valid.
        Span<double> m = stackalloc double[16];

        for (int i = 0; i < 16; i++)
            m[i] = M[i];

        inverse = default;

        for (int i = 0; i < 16; i++)
            if (!double.IsFinite(m[i]))
                return false;

        // Column-major flat layout, so this is the classic GL formulation and
        // the indices below match the storage one to one.
        Span<double> c = stackalloc double[16];

        c[0] = m[5] * m[10] * m[15] - m[5] * m[11] * m[14] - m[9] * m[6] * m[15]
             + m[9] * m[7] * m[14] + m[13] * m[6] * m[11] - m[13] * m[7] * m[10];
        c[4] = -m[4] * m[10] * m[15] + m[4] * m[11] * m[14] + m[8] * m[6] * m[15]
             - m[8] * m[7] * m[14] - m[12] * m[6] * m[11] + m[12] * m[7] * m[10];
        c[8] = m[4] * m[9] * m[15] - m[4] * m[11] * m[13] - m[8] * m[5] * m[15]
             + m[8] * m[7] * m[13] + m[12] * m[5] * m[11] - m[12] * m[7] * m[9];
        c[12] = -m[4] * m[9] * m[14] + m[4] * m[10] * m[13] + m[8] * m[5] * m[14]
              - m[8] * m[6] * m[13] - m[12] * m[5] * m[10] + m[12] * m[6] * m[9];
        c[1] = -m[1] * m[10] * m[15] + m[1] * m[11] * m[14] + m[9] * m[2] * m[15]
             - m[9] * m[3] * m[14] - m[13] * m[2] * m[11] + m[13] * m[3] * m[10];
        c[5] = m[0] * m[10] * m[15] - m[0] * m[11] * m[14] - m[8] * m[2] * m[15]
             + m[8] * m[3] * m[14] + m[12] * m[2] * m[11] - m[12] * m[3] * m[10];
        c[9] = -m[0] * m[9] * m[15] + m[0] * m[11] * m[13] + m[8] * m[1] * m[15]
             - m[8] * m[3] * m[13] - m[12] * m[1] * m[11] + m[12] * m[3] * m[9];
        c[13] = m[0] * m[9] * m[14] - m[0] * m[10] * m[13] - m[8] * m[1] * m[14]
              + m[8] * m[2] * m[13] + m[12] * m[1] * m[10] - m[12] * m[2] * m[9];
        c[2] = m[1] * m[6] * m[15] - m[1] * m[7] * m[14] - m[5] * m[2] * m[15]
             + m[5] * m[3] * m[14] + m[13] * m[2] * m[7] - m[13] * m[3] * m[6];
        c[6] = -m[0] * m[6] * m[15] + m[0] * m[7] * m[14] + m[4] * m[2] * m[15]
             - m[4] * m[3] * m[14] - m[12] * m[2] * m[7] + m[12] * m[3] * m[6];
        c[10] = m[0] * m[5] * m[15] - m[0] * m[7] * m[13] - m[4] * m[1] * m[15]
              + m[4] * m[3] * m[13] + m[12] * m[1] * m[7] - m[12] * m[3] * m[5];
        c[14] = -m[0] * m[5] * m[14] + m[0] * m[6] * m[13] + m[4] * m[1] * m[14]
              - m[4] * m[2] * m[13] - m[12] * m[1] * m[6] + m[12] * m[2] * m[5];
        c[3] = -m[1] * m[6] * m[11] + m[1] * m[7] * m[10] + m[5] * m[2] * m[11]
             - m[5] * m[3] * m[10] - m[9] * m[2] * m[7] + m[9] * m[3] * m[6];
        c[7] = m[0] * m[6] * m[11] - m[0] * m[7] * m[10] - m[4] * m[2] * m[11]
             + m[4] * m[3] * m[10] + m[8] * m[2] * m[7] - m[8] * m[3] * m[6];
        c[11] = -m[0] * m[5] * m[11] + m[0] * m[7] * m[9] + m[4] * m[1] * m[11]
              - m[4] * m[3] * m[9] - m[8] * m[1] * m[7] + m[8] * m[3] * m[5];
        c[15] = m[0] * m[5] * m[10] - m[0] * m[6] * m[9] - m[4] * m[1] * m[10]
              + m[4] * m[2] * m[9] + m[8] * m[1] * m[6] - m[8] * m[2] * m[5];

        double determinant = m[0] * c[0] + m[1] * c[4] + m[2] * c[8] + m[3] * c[12];

        // Compare against the magnitudes of all 24 determinant products. A fixed absolute
        // threshold rejects small, invertible matrices; comparing only with zero accepts
        // singular matrices whose determinant has a non-zero rounding residual.
        double determinantMagnitude = 0d;

        for (int a = 0; a < 4; a++)
        {
            for (int b = 0; b < 4; b++)
            {
                if (b == a) continue;

                for (int d = 0; d < 4; d++)
                {
                    if (d == a || d == b) continue;
                    int e = 6 - a - b - d;
                    determinantMagnitude += Math.Abs(m[a * 4] * m[b * 4 + 1] * m[d * 4 + 2] * m[e * 4 + 3]);
                }
            }
        }

        // Allow for accumulated double rounding error (double.Epsilon is not machine epsilon).
        const double relativeTolerance = 32d * 2.2204460492503131e-16;

        if (!double.IsFinite(determinant) || Math.Abs(determinant) <= relativeTolerance * determinantMagnitude)
            return false;

        var result = default(Mat4);

        for (int i = 0; i < 16; i++)
            result.M[i] = (float)(c[i] / determinant);

        if (!result.IsFinite)
            return false;

        inverse = result;
        return true;
    }

    /// <summary>Inverse of a non-singular matrix.</summary>
    public readonly Mat4 Inverted()
    {
        return !TryInvert(out Mat4 inverse)
            ? throw new InvalidOperationException("Matrix is singular, non-finite, or its inverse cannot be represented as finite floats.") 
            : inverse;
    }

    public readonly bool ApproximatelyEquals(Mat4 other, float tolerance = 1e-6f)
    {
        // NaN tolerance would make EVERY comparison "equal" (x > NaN is false)
        if (!float.IsFinite(tolerance) || tolerance < 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(tolerance), "Tolerance must be a non-negative finite number.");
        }

        for (int i = 0; i < 16; i++)
        {
            // written as !(diff <= tolerance) so a NaN diff
            // (any NaN element) REJECTS equality - "diff > tolerance" is
            // false for NaN and let a poisoned matrix slip through as equal
            float diff = MathF.Abs(M[i] - other.M[i]);

            if (!(diff <= tolerance))
                return false;

        }

        return true;
    }

    public readonly bool Equals(Mat4 other)
    {
        for (int i = 0; i < 16; i++)
            if (!M[i].Equals(other.M[i]))
                return false;

        return true;
    }

    public readonly override bool Equals(object? obj) => obj is Mat4 other && Equals(other);

    public readonly override int GetHashCode()
    {
        var hash = new HashCode();

        for (int i = 0; i < 16; i++)
        {
            hash.Add(M[i]);
        }

        return hash.ToHashCode();
    }

    public readonly override string ToString() =>
        $"[{this[0, 0]:G5}, {this[0, 1]:G5}, {this[0, 2]:G5}, {this[0, 3]:G5}; " +
        $"{this[1, 0]:G5}, {this[1, 1]:G5}, {this[1, 2]:G5}, {this[1, 3]:G5}; " +
        $"{this[2, 0]:G5}, {this[2, 1]:G5}, {this[2, 2]:G5}, {this[2, 3]:G5}; " +
        $"{this[3, 0]:G5}, {this[3, 1]:G5}, {this[3, 2]:G5}, {this[3, 3]:G5}]";

    private static void ValidateIndex(int row, int column)
    {
        if (row is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(row));
        if (column is < 0 or >= 4) throw new ArgumentOutOfRangeException(nameof(column));
    }

    private static bool TryNormalize(double x, double y, double z, out Vec3 normalized)
    {
        // Inputs come from floats or their differences/cross products. Their squared lengths
        // fit in double even when float length calculations would overflow or underflow.
        double length = Math.Sqrt(Math.Pow(x, 2) + Math.Pow(y, 2) + Math.Pow(z, 2));

        if (!(length > 0d) || !double.IsFinite(length))
        {
            normalized = default;
            return false;
        }

        normalized = new Vec3((float)(x / length), (float)(y / length), (float)(z / length));
        return true;
    }
}
