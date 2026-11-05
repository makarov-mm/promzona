using MakarovPhysics.UI;

namespace MakarovPhysics.Charts;

/// <summary>
/// One term of an energy budget: a name, a color and a live reading.
/// </summary>
/// <param name="Name">Short caption, already translated by the module.</param>
/// <param name="Color">Bar color.</param>
/// <param name="Read">Current value of this term, in the module's units.</param>
public readonly record struct EnergyChannel(string Name, UiColor Color, Func<double> Read);
