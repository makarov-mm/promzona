namespace MakarovPhysics.UI;

/// <summary>Immutable RGBA color in the 0..1 range for the 2D layer.</summary>
public readonly struct UiColor(float r, float g, float b, float a = 1f) : IEquatable<UiColor>
{
    public readonly float R = r;
    public readonly float G = g;
    public readonly float B = b;
    public readonly float A = a;
    public static UiColor White => new(1f, 1f, 1f, 1f);
    public static UiColor Black => new(0f, 0f, 0f, 1f);
    public static UiColor operator +(UiColor left, UiColor right) => new(left.R + right.R, left.G + right.G, left.B + right.B, left.A + right.A);
    public static UiColor operator -(UiColor left, UiColor right) => new(left.R - right.R, left.G - right.G, left.B - right.B, left.A - right.A);
    public static UiColor operator *(UiColor color, float scalar) => new(color.R * scalar, color.G * scalar, color.B * scalar, color.A * scalar);
    public static UiColor operator *(float scalar, UiColor color) => color * scalar;
    public static UiColor operator /(UiColor color, float scalar) => color * (1f / scalar);
    public static bool operator ==(UiColor left, UiColor right) => left.Equals(right);
    public static bool operator !=(UiColor left, UiColor right) => !left.Equals(right);
    public static UiColor FromBytes(byte r, byte g, byte b, byte a = 255) => new(r / 255f, g / 255f, b / 255f, a / 255f);

    public UiColor WithAlpha(float alpha) => new(R, G, B, alpha);

    public UiColor Clamped() => new(
        Math.Clamp(R, 0f, 1f),
        Math.Clamp(G, 0f, 1f),
        Math.Clamp(B, 0f, 1f),
        Math.Clamp(A, 0f, 1f));

    public static UiColor Lerp(UiColor from, UiColor to, float amount) => from + (to - from) * amount;
    public bool Equals(UiColor other) => R.Equals(other.R) && G.Equals(other.G) && B.Equals(other.B) && A.Equals(other.A);
    public override bool Equals(object? obj) => obj is UiColor other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);
    public override string ToString() => $"rgba({R:G4}, {G:G4}, {B:G4}, {A:G4})";
}
