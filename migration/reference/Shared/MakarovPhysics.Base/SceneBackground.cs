namespace MakarovPhysics.Base;

/// <summary>Module category - a carousel ring.</summary>
/// <summary>
/// Backdrop drawn by the host behind the experiment scene. Space themes suit
/// orbital mechanics; mechanics reads better on a neutral "laboratory" wall;
/// fields and abstract models sit on an engineering grid.
/// </summary>
public enum SceneBackground
{
    Stars, // deep space with a starfield
    Laboratory, // neutral gray gradient
    Grid, // dark engineering grid
    Diagram, // quiet dark canvas, no simulated stars or grid
}
