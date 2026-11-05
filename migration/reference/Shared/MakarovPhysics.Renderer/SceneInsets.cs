namespace MakarovPhysics.Renderer;

/// <summary>
/// Host-owned space reserved for screen furniture, as fractions of the scene
/// height. Optional context service: headless and other hosts need not supply it.
/// The viewport itself does not move, so picking, post-processing and overlays
/// continue to use the same coordinates as the module.
/// </summary>
public sealed class SceneInsets
{
    public float Top;
    public float Bottom;
}
