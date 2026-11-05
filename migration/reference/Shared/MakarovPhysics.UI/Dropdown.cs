using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>Dropdown list: clicking opens a popup with the items (UI overlay).</summary>
public class Dropdown(string text, string[] items, int selected = 0) : Widget
{
    public string Text = text;
    public string[] Items = items;
    public int Selected = selected;
    public event Action<int>? Changed;

    /// <summary>Choose an item without the popup; Changed is raised only on a change.</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= Items.Length || index == Selected) return;
        Selected = index;
        Changed?.Invoke(index);
    }
    private int _captionRows=1;
    public override float PreferredHeight(float w) => Theme.FontSize + Theme.ControlHeight + (_captionRows-1)*(Theme.FontSize+4);

    public override void Draw(Draw2D d)
    {
        var lines=Overlays.SceneDashboard.Wrap(Text,MathF.Max(24,Rect.W),d.MeasureText);
        if (_captionRows!=lines.Count) { _captionRows=lines.Count; RequestLayout(); }
        for(int i=0;i<lines.Count;i++) d.Text(Rect.X,Rect.Y+i*(Theme.FontSize+4),lines[i],Theme.Text);
        float ty = Rect.Y + _captionRows*(Theme.FontSize+4);
        float h = Theme.ControlHeight - 4;
        d.Rect(Rect.X, ty, Rect.W, h, Hovered ? Theme.ControlHover : Theme.ControlBackground);
        d.RectOutline(Rect.X, ty, Rect.W, h, Theme.PanelBorder);

        if (Items.Length > 0)
        {
            string label=Items[Math.Clamp(Selected,0,Items.Length-1)];
            float width=MathF.Max(1,Rect.W-32);
            label=TextLayout.Ellipsize(label,width,d.MeasureText);
            d.Text(Rect.X + 6, ty + (h - Theme.FontSize) / 2 - 1, label, Theme.Text);
        }

        // ▾ (U+25BE), NOT ▼ (U+25BC): the atlas bakes only the small
        // triangle (the InfoOverlay chevron). The big one fell back to "?"
        // - which every dropdown in the suite dutifully displayed on its
        // right edge until 13.08.2026, reading like a help button.
        d.Text(Rect.Right - 18, ty + (h - Theme.FontSize) / 2 - 1, "▾", Theme.TextDim);
    }

    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left || Items.Length == 0 || Ui is null)
        {
            return false;
        }

        OpenPopup();
        return true;
    }

    private void OpenPopup()
    {
        var ui = Ui!;
        var popup = new ScrollArea();

        for (int i = 0; i < Items.Length; i++)
        {
            int index = i; // capture per item

            UiButton b = popup.Add(new UiButton(Items[i], () =>
            {
                Selected = index;
                ui.CloseOverlay();
                Changed?.Invoke(index);
            }));

            if (index == Selected)
            {
                b.Text = "• " + b.Text;
            }
        }

        float itemH = Theme.ControlHeight + Theme.Spacing;
        float h = Items.Length * itemH - Theme.Spacing + 2 * Theme.Padding;
        h=MathF.Min(h,MathF.Max(Theme.ControlHeight,ui.Height-16));
        float width=MathF.Min(MathF.Max(Rect.W,Items.Max(ui.Draw.MeasureText)+2*Theme.Padding+32),MathF.Max(1,ui.Width-16));
        float y = Rect.Y + _captionRows*(Theme.FontSize+4) + (Theme.ControlHeight - 4);

        // no room below - open upwards
        if (y + h > ui.Height)
        {
            y = Math.Max(0, Rect.Y - h);
        }

        float x=Math.Clamp(Rect.X,8,MathF.Max(8,ui.Width-width-8));
        y=Math.Clamp(y,8,MathF.Max(8,ui.Height-h-8));
        popup.Rect = new UiRect(x, y, width, h);
        ui.ShowOverlay(popup, dim: false);
    }
}
