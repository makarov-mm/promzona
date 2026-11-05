namespace MakarovPhysics.UI;

/// <summary>
/// Categorical accents for quantities and outcomes, not a physical colormap.
/// Keep wavelength, temperature, charge sign and multi-component budgets on
/// their own labelled scales. The conventions and checked bindings live in
/// Docs/COLORS.md and Tools/color-semantics.json.
/// </summary>
public static class SemanticColors
{
    public static UiColor Field => UiColor.FromBytes(120, 190, 255);
    public static UiColor Energy => UiColor.FromBytes(230, 140, 60);
    public static UiColor Event => UiColor.FromBytes(126, 214, 128);
    public static UiColor Loss => UiColor.FromBytes(232, 96, 84);
    public static UiColor Phase => UiColor.FromBytes(190, 130, 235);
}
