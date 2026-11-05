namespace MakarovPhysics.Renderer;

/// <summary>
/// Small allocation-free 3D vector used by the renderer.
/// The type is immutable; arithmetic is expressed through operators.
/// </summary>
public readonly struct Vec3(float x, float y, float z) : IEquatable<Vec3>
{
    public readonly float X = x;
    public readonly float Y = y;
    public readonly float Z = z;

    public static Vec3 Zero => default;
    public static Vec3 One => new(1f, 1f, 1f);
    public static Vec3 UnitX => new(1f, 0f, 0f);
    public static Vec3 UnitY => new(0f, 1f, 0f);
    public static Vec3 UnitZ => new(0f, 0f, 1f);

    public bool IsFinite => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

    public static Vec3 operator +(Vec3 value) => value;
    public static Vec3 operator -(Vec3 value) => new(-value.X, -value.Y, -value.Z);
    public static Vec3 operator +(Vec3 left, Vec3 right) => new(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
    public static Vec3 operator -(Vec3 left, Vec3 right) => new(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
    public static Vec3 operator *(Vec3 value, float scalar) => new(value.X * scalar, value.Y * scalar, value.Z * scalar);
    public static Vec3 operator *(float scalar, Vec3 value) => value * scalar;
    public static Vec3 operator /(Vec3 value, float scalar) => new(value.X / scalar, value.Y / scalar, value.Z / scalar);

    public static bool operator ==(Vec3 left, Vec3 right) => left.Equals(right);
    public static bool operator !=(Vec3 left, Vec3 right) => !left.Equals(right);

    public float LengthSquared() => X * X + Y * Y + Z * Z;
    public float Length() => MathF.Sqrt(LengthSquared());

    public Vec3 Normalized(float epsilon = 1e-8f)
    {
        float lengthSquared = LengthSquared();

        if (!float.IsFinite(lengthSquared) || lengthSquared <= epsilon * epsilon)
        {
            return Zero;
        }

        return this / MathF.Sqrt(lengthSquared);
    }

    public bool TryNormalize(out Vec3 normalized, float epsilon = 1e-8f)
    {
        float lengthSquared = LengthSquared();

        if (!float.IsFinite(lengthSquared) || lengthSquared <= epsilon * epsilon)
        {
            normalized = Zero;
            return false;
        }

        normalized = this / MathF.Sqrt(lengthSquared);
        return true;
    }

    public static float Dot(Vec3 left, Vec3 right) => left.X * right.X + left.Y * right.Y + left.Z * right.Z;

    public static Vec3 Cross(Vec3 left, Vec3 right) => new(
        left.Y * right.Z - left.Z * right.Y,
        left.Z * right.X - left.X * right.Z,
        left.X * right.Y - left.Y * right.X);

    public static float DistanceSquared(Vec3 left, Vec3 right) => (left - right).LengthSquared();
    public static float Distance(Vec3 left, Vec3 right) => MathF.Sqrt(DistanceSquared(left, right));
    public static Vec3 Lerp(Vec3 from, Vec3 to, float amount) => from + (to - from) * amount;

    public bool ApproximatelyEquals(Vec3 other, float tolerance = 1e-6f) =>
        MathF.Abs(X - other.X) <= tolerance &&
        MathF.Abs(Y - other.Y) <= tolerance &&
        MathF.Abs(Z - other.Z) <= tolerance;

    public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);
    public override bool Equals(object? obj) => obj is Vec3 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Z);
    public override string ToString() => $"({X:G6}, {Y:G6}, {Z:G6})";
}
