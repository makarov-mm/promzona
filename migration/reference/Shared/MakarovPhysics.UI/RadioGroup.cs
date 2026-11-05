using MakarovPhysics.Base;

namespace MakarovPhysics.UI;

/// <summary>
/// A titled group of radio buttons: one selected index, one Changed event.
/// Items can be individually disabled with a REASON - the row draws dim and
/// the reason renders as a second line under it, so a grey "CUDA" explains
/// itself instead of looking broken (the honest-models rule applied to a
/// widget). Item labels are mutable: Lbm2D rewrites its Auto row every
/// frame to show which backend Auto resolved to.
/// </summary>
public class RadioGroup(string title, string[] items, int selected = 0) : Widget
{
    public string Title = title;
    public string[] Items = items;
    public int Selected = selected;

    /// <summary>Per-item enabled flags; all true until a caller greys one.</summary>
    public bool[] ItemEnabled = Filled(items.Length, true);

    /// <summary>Shown dim under a DISABLED item; ignored for enabled ones.</summary>
    public string[] ItemReason = Filled(items.Length, string.Empty);

    private static T[] Filled<T>(int count, T value)
    {
        var array = new T[count];
        Array.Fill(array, value);
        return array;
    }

    public event Action<int>? Changed;

    /// <summary>Choose an item without a click; a disabled item is refused
    /// exactly as the click path refuses it, and Changed fires only on a change.</summary>
    public void Select(int index)
    {
        if (index < 0 || index >= Items.Length || !ItemEnabled[index] || index == Selected) return;
        Selected = index;
        Changed?.Invoke(index);
    }

    private float RowHeight => Theme.FontSize + 8;
    private float ReasonHeight => Theme.FontSize; // second dim line, smaller step

    public override float PreferredHeight(float w)
    {
        float height = Theme.FontSize + 6; // the title

        for (int i = 0; i < Items.Length; i++)
        {
            height += RowHeight;

            if (!ItemEnabled[i] && ItemReason[i].Length > 0)
            {
                height += ReasonHeight;
            }
        }

        return height;
    }

    public override void Draw(Draw2D d)
    {
        d.Text(Rect.X, Rect.Y, Title, Theme.Text);
        float y = Rect.Y + Theme.FontSize + 6;

        for (int i = 0; i < Items.Length; i++)
        {
            bool on = ItemEnabled[i];
            float s = 14;
            float cy = y + (RowHeight - s) / 2;

            // circle drawn as the theme draws its checkbox square - flat
            // fill and an outline; a filled inner disc marks the selection
            d.Circle(Rect.X + s / 2, cy + s / 2, s / 2, on && Hovered ? Theme.ControlHover : Theme.ControlBackground);
            d.CircleOutline(Rect.X + s / 2, cy + s / 2, s / 2, Theme.PanelBorder);

            if (i == Selected)
            {
                d.Circle(Rect.X + s / 2, cy + s / 2, s / 2 - 4, Theme.Accent);
            }

            d.Text(Rect.X + s + 8, y + (RowHeight - Theme.FontSize) / 2 - 1, Items[i], on ? Theme.Text : Theme.TextDim);
            y += RowHeight;

            if (!on && ItemReason[i].Length > 0)
            {
                d.Text(Rect.X + s + 8, y - 2, ItemReason[i], Theme.TextDim);
                y += ReasonHeight;
            }
        }
    }

    public override bool OnMouseDown(float x, float y, MouseButton b)
    {
        if (b != MouseButton.Left) return false;
        float rowY = Rect.Y + Theme.FontSize + 6;

        for (int i = 0; i < Items.Length; i++)
        {
            float rowH = RowHeight;

            if (y >= rowY && y < rowY + rowH)
            {
                if (!ItemEnabled[i]) return true; // consumed, not acted on

                if (Selected != i)
                {
                    Selected = i;
                    Changed?.Invoke(i);
                }

                return true;
            }

            rowY += rowH;

            if (!ItemEnabled[i] && ItemReason[i].Length > 0)
            {
                rowY += ReasonHeight;
            }
        }

        return false;
    }
}
