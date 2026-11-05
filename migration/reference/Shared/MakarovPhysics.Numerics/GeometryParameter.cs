namespace MakarovPhysics.Numerics;

public sealed class GeometryParameter(string key, double min, double max, double initial, string[]? options = null)
{
    public string Key { get; } = key;
    public double Min { get; } = min;
    public double Max { get; } = max;
    public double Initial { get; } = initial;
    public double Value { get; private set; } = initial;
    public string[]? Options { get; } = options;

    public void Set(double value)
    {
        if (double.IsFinite(value))
        {
            Value = Math.Clamp(Options is null ? value : Math.Round(value), Min, Max);
        }
    }
}
