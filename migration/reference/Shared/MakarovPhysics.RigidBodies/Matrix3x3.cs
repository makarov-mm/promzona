using System.Numerics;

namespace MakarovPhysics.RigidBodies;

/// <summary>
/// Immutable 3x3 matrix used for inertia tensors.
/// Vectors are treated as columns, so <c>matrix * vector</c> performs a transform.
/// </summary>
public readonly struct Matrix3x3(
    float m00,
    float m01,
    float m02,
    float m10,
    float m11,
    float m12,
    float m20,
    float m21,
    float m22)
    : IEquatable<Matrix3x3>
{
    public readonly float M00 = m00, M01 = m01, M02 = m02;
    public readonly float M10 = m10, M11 = m11, M12 = m12;
    public readonly float M20 = m20, M21 = m21, M22 = m22;

    public static Matrix3x3 Zero => default;
    public static Matrix3x3 Identity => new(
        1f, 0f, 0f,
        0f, 1f, 0f,
        0f, 0f, 1f);

    public float this[int row, int column] => (row, column) switch
    {
        (0, 0) => M00, (0, 1) => M01, (0, 2) => M02,
        (1, 0) => M10, (1, 1) => M11, (1, 2) => M12,
        (2, 0) => M20, (2, 1) => M21, (2, 2) => M22,
        _ => throw new ArgumentOutOfRangeException((uint)row >= 3 ? nameof(row) : nameof(column))
    };

    public float Determinant =>
        M00 * (M11 * M22 - M12 * M21) -
        M01 * (M10 * M22 - M12 * M20) +
        M02 * (M10 * M21 - M11 * M20);

    public float Trace => M00 + M11 + M22;

    public bool IsFinite =>
        float.IsFinite(M00) && float.IsFinite(M01) && float.IsFinite(M02) &&
        float.IsFinite(M10) && float.IsFinite(M11) && float.IsFinite(M12) &&
        float.IsFinite(M20) && float.IsFinite(M21) && float.IsFinite(M22);

    public Matrix3x3 Transposed => new(
        M00, M10, M20,
        M01, M11, M21,
        M02, M12, M22);

    public static Matrix3x3 Diagonal(Vector3 diagonal) => new(
        diagonal.X, 0f, 0f,
        0f, diagonal.Y, 0f,
        0f, 0f, diagonal.Z);

    public static Matrix3x3 FromQuaternion(Quaternion quaternion)
    {
        float lengthSquared = quaternion.LengthSquared();

        if (!float.IsFinite(lengthSquared) || lengthSquared <= 1e-12f)
        {
            throw new ArgumentException("Quaternion must be finite and non-zero.", nameof(quaternion));
        }

        Quaternion q = MathF.Abs(lengthSquared - 1f) <= 1e-6f
            ? quaternion
            : Quaternion.Normalize(quaternion);

        float x = q.X, y = q.Y, z = q.Z, w = q.W;

        return new Matrix3x3(
            1f - 2f * (y * y + z * z),  2f * (x * y - w * z),       2f * (x * z + w * y),
            2f * (x * y + w * z),       1f - 2f * (x * x + z * z),  2f * (y * z - w * x),
            2f * (x * z - w * y),       2f * (y * z + w * x),       1f - 2f * (x * x + y * y));
    }

    public static Matrix3x3 operator +(Matrix3x3 value) => value;
    public static Matrix3x3 operator -(Matrix3x3 value) => new(
        -value.M00, -value.M01, -value.M02,
        -value.M10, -value.M11, -value.M12,
        -value.M20, -value.M21, -value.M22);

    public static Matrix3x3 operator +(Matrix3x3 left, Matrix3x3 right) => new(
        left.M00 + right.M00, left.M01 + right.M01, left.M02 + right.M02,
        left.M10 + right.M10, left.M11 + right.M11, left.M12 + right.M12,
        left.M20 + right.M20, left.M21 + right.M21, left.M22 + right.M22);

    public static Matrix3x3 operator -(Matrix3x3 left, Matrix3x3 right) => new(
        left.M00 - right.M00, left.M01 - right.M01, left.M02 - right.M02,
        left.M10 - right.M10, left.M11 - right.M11, left.M12 - right.M12,
        left.M20 - right.M20, left.M21 - right.M21, left.M22 - right.M22);

    public static Matrix3x3 operator *(Matrix3x3 left, Matrix3x3 right) => new(
        left.M00 * right.M00 + left.M01 * right.M10 + left.M02 * right.M20,
        left.M00 * right.M01 + left.M01 * right.M11 + left.M02 * right.M21,
        left.M00 * right.M02 + left.M01 * right.M12 + left.M02 * right.M22,
        left.M10 * right.M00 + left.M11 * right.M10 + left.M12 * right.M20,
        left.M10 * right.M01 + left.M11 * right.M11 + left.M12 * right.M21,
        left.M10 * right.M02 + left.M11 * right.M12 + left.M12 * right.M22,
        left.M20 * right.M00 + left.M21 * right.M10 + left.M22 * right.M20,
        left.M20 * right.M01 + left.M21 * right.M11 + left.M22 * right.M21,
        left.M20 * right.M02 + left.M21 * right.M12 + left.M22 * right.M22);

    public static Vector3 operator *(Matrix3x3 matrix, Vector3 vector) => new(
        matrix.M00 * vector.X + matrix.M01 * vector.Y + matrix.M02 * vector.Z,
        matrix.M10 * vector.X + matrix.M11 * vector.Y + matrix.M12 * vector.Z,
        matrix.M20 * vector.X + matrix.M21 * vector.Y + matrix.M22 * vector.Z);

    public static Matrix3x3 operator *(Matrix3x3 matrix, float scalar) => new(
        matrix.M00 * scalar, matrix.M01 * scalar, matrix.M02 * scalar,
        matrix.M10 * scalar, matrix.M11 * scalar, matrix.M12 * scalar,
        matrix.M20 * scalar, matrix.M21 * scalar, matrix.M22 * scalar);

    public static Matrix3x3 operator *(float scalar, Matrix3x3 matrix) => matrix * scalar;
    public static Matrix3x3 operator /(Matrix3x3 matrix, float scalar) => matrix * (1f / scalar);

    public static bool operator ==(Matrix3x3 left, Matrix3x3 right) => left.Equals(right);
    public static bool operator !=(Matrix3x3 left, Matrix3x3 right) => !left.Equals(right);

    public bool TryInvert(out Matrix3x3 inverse, float relativeTolerance = 1e-8f)
    {
        if (!(relativeTolerance >= 0f) || !float.IsFinite(relativeTolerance))
        {
            throw new ArgumentOutOfRangeException(nameof(relativeTolerance));
        }

        float c00 = M11 * M22 - M12 * M21;
        float c01 = M12 * M20 - M10 * M22;
        float c02 = M10 * M21 - M11 * M20;
        float determinant = M00 * c00 + M01 * c01 + M02 * c02;

        float scale = MaxAbsElement();
        float threshold = relativeTolerance * scale * scale * scale;

        if (!float.IsFinite(determinant) || scale == 0f || MathF.Abs(determinant) <= threshold)
        {
            inverse = Zero;
            return false;
        }

        float invDet = 1f / determinant;

        inverse = new Matrix3x3(
            c00 * invDet,
            (M02 * M21 - M01 * M22) * invDet,
            (M01 * M12 - M02 * M11) * invDet,
            c01 * invDet,
            (M00 * M22 - M02 * M20) * invDet,
            (M02 * M10 - M00 * M12) * invDet,
            c02 * invDet,
            (M01 * M20 - M00 * M21) * invDet,
            (M00 * M11 - M01 * M10) * invDet);

        return true;
    }

    public Matrix3x3 Inverse(float relativeTolerance = 1e-8f)
    {
        if (!TryInvert(out Matrix3x3 inverse, relativeTolerance))
        {
            throw new InvalidOperationException("The matrix is singular or numerically ill-conditioned.");
        }

        return inverse;
    }

    public bool ApproximatelyEquals(Matrix3x3 other, float tolerance = 1e-5f) =>
        MathF.Abs(M00 - other.M00) <= tolerance &&
        MathF.Abs(M01 - other.M01) <= tolerance &&
        MathF.Abs(M02 - other.M02) <= tolerance &&
        MathF.Abs(M10 - other.M10) <= tolerance &&
        MathF.Abs(M11 - other.M11) <= tolerance &&
        MathF.Abs(M12 - other.M12) <= tolerance &&
        MathF.Abs(M20 - other.M20) <= tolerance &&
        MathF.Abs(M21 - other.M21) <= tolerance &&
        MathF.Abs(M22 - other.M22) <= tolerance;

    public bool Equals(Matrix3x3 other) =>
        M00.Equals(other.M00) && M01.Equals(other.M01) && M02.Equals(other.M02) &&
        M10.Equals(other.M10) && M11.Equals(other.M11) && M12.Equals(other.M12) &&
        M20.Equals(other.M20) && M21.Equals(other.M21) && M22.Equals(other.M22);

    public override bool Equals(object? obj) => obj is Matrix3x3 other && Equals(other);
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(M00); hash.Add(M01); hash.Add(M02);
        hash.Add(M10); hash.Add(M11); hash.Add(M12);
        hash.Add(M20); hash.Add(M21); hash.Add(M22);
        return hash.ToHashCode();
    }

    public override string ToString() =>
        $"[{M00:G5}, {M01:G5}, {M02:G5}; " +
        $"{M10:G5}, {M11:G5}, {M12:G5}; " +
        $"{M20:G5}, {M21:G5}, {M22:G5}]";

    private float MaxAbsElement()
    {
        float max = MathF.Abs(M00);
        max = MathF.Max(max, MathF.Abs(M01)); max = MathF.Max(max, MathF.Abs(M02));
        max = MathF.Max(max, MathF.Abs(M10)); max = MathF.Max(max, MathF.Abs(M11)); max = MathF.Max(max, MathF.Abs(M12));
        max = MathF.Max(max, MathF.Abs(M20)); max = MathF.Max(max, MathF.Abs(M21)); max = MathF.Max(max, MathF.Abs(M22));
        return max;
    }
}
