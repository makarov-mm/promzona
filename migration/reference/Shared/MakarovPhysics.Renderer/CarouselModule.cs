namespace MakarovPhysics.Renderer;

public sealed class CarouselModule
{
    public string Name = string.Empty;

    /// <summary>front-face image (experiment screenshot or a label)</summary>
    public Texture? Texture;

    /// <summary>anything: a DLL handle, a module factory, etc.</summary>
    public object? Tag;

    /// <summary>slab body color (back/sides)</summary>
    public Vec3 BodyColor;

    /// <summary>position: column around the axis</summary>
    public int Column;

    /// <summary>position: level (0..Rings-1)</summary>
    public int Ring;

    /// <summary>Filtering keeps the panel in its original slot but dims and disables it.</summary>
    public bool MatchesFilter = true;
}
