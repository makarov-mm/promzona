namespace MakarovPhysics.UI;

/// <summary>
/// Dark theme for control chrome. Quantity and outcome accents use SemanticColors.
/// </summary>
public sealed class UiTheme
{
    public UiColor PanelBackground = UiColor.FromBytes(37, 37, 42);
    public UiColor PanelBorder = UiColor.FromBytes(58, 58, 64);
    public UiColor Text = UiColor.FromBytes(222, 222, 226);
    public UiColor TextDim = UiColor.FromBytes(140, 140, 148);
    public UiColor Accent = UiColor.FromBytes(86, 156, 214);
    public UiColor ControlBackground = UiColor.FromBytes(50, 50, 56);
    public UiColor ControlHover = UiColor.FromBytes(64, 64, 72);
    public UiColor ControlActive = UiColor.FromBytes(80, 80, 90);
    public UiColor SliderTrack = UiColor.FromBytes(58, 58, 64);
    public UiColor Good = SemanticColors.Event;
    public int FontSize = 14;
    public int Padding = 8;
    public int Spacing = 6;
    public int ControlHeight = 24;
    public static UiTheme Dark { get; } = new();
}
