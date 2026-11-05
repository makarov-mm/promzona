using MakarovPhysics.Renderer;

namespace MakarovPhysics.UI.Overlays;

/// <summary>
/// One labelled vector attached to a point of the scene: a force on a block, a
/// velocity of a planet, an acceleration of a bob.
/// </summary>
/// <param name="Origin">World point the vector acts at.</param>
/// <param name="Value">The quantity itself, in the module's own units.</param>
/// <param name="Label">Short name shown next to the value, for example "Fg".</param>
/// <param name="Color">Color of the arrow and its label.</param>
public readonly record struct SceneVector(Vec3 Origin, Vec3 Value, string Label, UiColor Color)
{
    public float Magnitude => Value.Length();
}
