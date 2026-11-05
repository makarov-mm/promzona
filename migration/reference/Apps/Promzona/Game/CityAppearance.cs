namespace Promzona.Game;

/// <summary>Stable per-actor body paint shared by imported and procedural LODs.
/// Glass, lights, tyres and emergency stripes keep their authored materials.</summary>
public static class CityAppearance
{
    public static (float R, float G, float B) Paint(CityCar c)
    {
        if (c.Destroyed)
        {
            return (.055f, .050f, .045f);
        }

        if (c.AirPatrol) return (.72f, .78f, .84f);

        // A garage respray (29.10.2026) picks a palette slot by hand; zero keeps the factory colour.
        if (c.Paint > 0) return Slot((uint)(c.Paint - 1) % 12);

        switch (c.Kind)
        {
            case 13:
                return (.045f,.050f,.055f);
            case 14: 
                return (.27f,.32f,.20f);
            case 8:
                return (.95f,.68f,.08f);
            case 6:
                return (.33f,.40f,.31f);
            case 7:
                return (.20f,.27f,.22f);
            case 3:
                return (.86f, .89f, .90f);
            case 4:
                return (.94f, .94f, .84f);
            case 1:
                return (.49f, .56f, .39f);
            case 5:
                return (.71f, .66f, .42f);
        }

        if (c.Id == 1)
        {
            return (.92f, .28f, .045f);
        }

        // Mixed identity bits avoid correlation with car model (Id % 2 / 3).
        uint h = unchecked((uint)c.Id * 2654435761u);
        h ^= h >> 16;

        return Slot(h % 12);
    }

    public static (float R, float G, float B) Slot(uint n)
    {
        return n switch
        {
            0 => (.64f, .10f, .065f), 1 => (.13f, .29f, .48f), 2 => (.79f, .77f, .67f),
            3 => (.24f, .39f, .25f), 4 => (.38f, .12f, .23f), 5 => (.12f, .18f, .23f),
            6 => (.66f, .62f, .46f), 7 => (.38f, .43f, .46f), 8 => (.10f, .12f, .13f),
            9 => (.32f, .51f, .50f), 10 => (.61f, .38f, .18f), _ => (.57f, .63f, .68f)
        };
    }

    // Explicit palette slots: never infer body paint from redness or a name
    // containing "light" (the sedan's LightBlue is paint, not an emitter).
    public static int BodySlot(int kind) => kind switch
    {
        2 => 2,
        4 => 1,
        _ => 0
    };

    public static int PaletteWidth(int kind) => kind switch
    {
        1 or 2 or 6 or 7 or 8 => 8,
        4 or 5 => 10,
        _ => 32
    };
}
