using System.Reflection;
using System.Text.Json;
using MakarovPhysics.RigidBodies2D;

namespace Promzona.Game;

/// <summary>Reflection-driven actor state for saves. Capture boxes the primitive
/// fields of an actor (int, double, bool, enum, CityPoint) under their names;
/// the JSON writer turns the boxes into numbers on the save thread, and a
/// loaded file hands the same names back as JsonElements. Format 3 (29.10.2026)
/// made the capture cheap and the file small without a second schema: no
/// JsonElement is built per field any more, and a compact capture leaves out
/// every field that still holds its fresh-instance value, which Restore keeps
/// by never touching an absent key.</summary>
internal static class CityState
{
    public static readonly JsonSerializerOptions Json = new() { IncludeFields = true };
    private static readonly Dictionary<Type, FieldInfo[]> Fields = [];
    private static readonly Dictionary<Type, object?[]> Baselines = [];

    private static FieldInfo[] Schema(Type t)
    {
        lock (Fields)
        {
            if (Fields.TryGetValue(t, out var result)) return result;

            return Fields[t] =
            [
                .. t.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .Where(f => !f.IsInitOnly && (f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(CityPoint)))
            ];
        }
    }

    /// <summary>The field values of a freshly constructed instance: what Restore
    /// leaves in place for every key a compact capture omitted.</summary>
    private static object?[] Baseline(Type t)
    {
        lock (Fields)
        {
            if (Baselines.TryGetValue(t, out var result)) return result;
            FieldInfo[] schema = Schema(t);
            object fresh = Activator.CreateInstance(t) ?? throw new InvalidOperationException(t.Name);
            result = new object?[schema.Length];
            for (int i = 0; i < schema.Length; i++) result[i] = schema[i].GetValue(fresh);
            return Baselines[t] = result;
        }
    }

    public static Dictionary<string, object?> Capture(object value, bool compact = false)
    {
        FieldInfo[] schema = Schema(value.GetType());
        object?[]? baseline = compact ? Baseline(value.GetType()) : null;
        var result = new Dictionary<string, object?>(schema.Length);

        for (int i = 0; i < schema.Length; i++)
        {
            object? v = schema[i].GetValue(value);
            if (baseline is not null && Equals(v, baseline[i])) continue;
            result[schema[i].Name] = v;
        }

        return result;
    }

    public static void Restore(object value, Dictionary<string, object?> state)
    {
        foreach (FieldInfo f in Schema(value.GetType()))
            if (state.TryGetValue(f.Name, out object? saved) && saved is not null)
                f.SetValue(value, saved is JsonElement e ? e.Deserialize(f.FieldType, Json) : saved);
    }

    /// <summary>An integer field of a captured state, whether still boxed or already parsed from a file.</summary>
    public static int? Int(Dictionary<string, object?> state, string name)
    {
        if (!state.TryGetValue(name, out object? v) || v is null) return null;
        if (v is JsonElement e) return e.TryGetInt32(out int n) ? n : null;
        return Convert.ToInt32(v);
    }

    public static BodySave? Body(Body2D? b) => b is null ? null : new BodySave(b.X, b.Y, b.Angle, b.Vx, b.Vy, b.W);

    public static void RestoreBody(Body2D b, BodySave? s)
    {
        if (s is null) return;
        b.X = s.X;
        b.Y = s.Z;
        b.Angle = s.Angle;
        b.Vx = s.Vx;
        b.Vy = s.Vz;
        b.W = s.W;
        b.Wake();
    }

    /// <summary>A captured value that has not been through a file is a box; one that has is a JsonElement.</summary>
    public static void ValidateValue(object? value)
    {
        switch (value)
        {
            case JsonElement e: ValidateJson(e); break;
            case double d when !double.IsFinite(d) || Math.Abs(d) > 1e12: throw new InvalidDataException("Invalid numeric save state");
            case float f when !float.IsFinite(f) || Math.Abs(f) > 1e12: throw new InvalidDataException("Invalid numeric save state");
            case CityPoint p when !double.IsFinite(p.X) || !double.IsFinite(p.Z) || Math.Abs(p.X) > 1e12 || Math.Abs(p.Z) > 1e12:
                throw new InvalidDataException("Invalid numeric save state");
        }
    }

    public static void ValidateJson(JsonElement value, int depth = 0)
    {
        if (depth > 24) throw new InvalidDataException("Save nesting is too deep");

        if (value.ValueKind == JsonValueKind.Number && (!value.TryGetDouble(out double n) || !double.IsFinite(n) || Math.Abs(n) > 1e12))
            throw new InvalidDataException("Invalid numeric save state");

        if (value.ValueKind == JsonValueKind.Array)
        {
            if (value.GetArrayLength() > 20000)
                throw new InvalidDataException("Save array too large");

            foreach (JsonElement v in value.EnumerateArray())
                ValidateJson(v, depth + 1);
        }

        if (value.ValueKind == JsonValueKind.Object)
            foreach (JsonProperty p in value.EnumerateObject())
                ValidateJson(p.Value, depth + 1);
    }
}
