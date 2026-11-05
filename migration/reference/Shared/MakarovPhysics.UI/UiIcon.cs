namespace MakarovPhysics.UI;

/// <summary>Draws one pictogram inside a square box.</summary>
/// <param name="d">Batcher to draw into.</param>
/// <param name="x">Left of the box, logical pixels.</param>
/// <param name="y">Top of the box.</param>
/// <param name="size">Side of the box.</param>
/// <param name="color">Ink color.</param>
public delegate void UiIcon(Draw2D d, float x, float y, float size, UiColor color);
