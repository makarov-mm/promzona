using MakarovPhysics.Renderer;
namespace Promzona;

internal sealed class BlackGlControl : GLControl
{
    public BlackGlControl()
    {
        BackColor = Color.Black;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        BlackWindow.Paint(Handle);
        base.OnHandleCreated(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e) => e.Graphics.Clear(Color.Black);

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Context is null || IsContextLost)
        {
            e.Graphics.Clear(Color.Black);
        }
        else
        {
            base.OnPaint(e);
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == 0x14)
        {
            BlackWindow.Paint(m.HWnd, m.WParam);
            m.Result = 1;
            return;
        }

        base.WndProc(ref m);
    }
}
