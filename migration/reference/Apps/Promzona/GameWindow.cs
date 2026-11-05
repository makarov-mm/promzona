using System.ComponentModel;
using System.Diagnostics;
using MakarovPhysics.Base;
using MakarovPhysics.Renderer;
namespace Promzona;

/// <summary>Owns the real GLControl and disposes scene resources before its HWND.</summary>
public sealed class GameWindow : Form
{
    private readonly BlackGlControl _view=new(){Dock=DockStyle.Fill};
    private readonly GameSession _session;
    private readonly Makarov.Host.ImeInputBridge _ime;
    private readonly Stopwatch _clock=Stopwatch.StartNew();
    private double _last;
    private int _width,_height,_wheel;
    private Point? _mouse;
    private bool _ready,_shutdown,_lost,_cursorHidden;
    [Browsable(false),DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int ExitCode {get;private set;}
    public GameWindow(string directory,string language,bool fullscreen=true,string? saveDirectory=null)
    {
        BackColor=Color.Black;
        string iconFile=Path.Combine(directory,"PROMZONA.ico");if(File.Exists(iconFile))Icon=new Icon(iconFile);
        Text="PROMZONA";ClientSize=new(1440,900);MinimumSize=new(900,580);
        StartPosition=FormStartPosition.CenterScreen;KeyPreview=true;
        if(fullscreen){
            FormBorderStyle=FormBorderStyle.None;StartPosition=FormStartPosition.Manual;
            Bounds=Screen.FromPoint(Cursor.Position).Bounds;WindowState=FormWindowState.Maximized;
        }
        _session=new(directory,language,saveDirectory);_session.ExitRequested+=Close;
        Activated+=(_,_)=>{_session.Active=true;_session.Input.ResetAll();_last=_clock.Elapsed.TotalSeconds;};
        _ime=new(_view,_session.Input,()=>_session.Input.ResetAll(),CaretRectangle);
        _session.Ui.TextFocusChanging+=_ime.Cancel;
        _view.ContextFailed+=_=>Fault(new InvalidOperationException("OpenGL 3.3 core is required. Please update the graphics driver."));
        _view.ContextLost+=(_,reason)=>{_lost=true;Fault(new InvalidOperationException(reason));};
        _view.ContextReady+=c=>{
            try{GL.ClearColor(0,0,0,1);GL.Clear(GL.COLOR_BUFFER_BIT|GL.DEPTH_BUFFER_BIT);c.Context?.SwapBuffers();_width=c.ClientSize.Width;_height=c.ClientSize.Height;_session.Initialize(_width,_height,DeviceDpi/96f);_ready=true;_last=_clock.Elapsed.TotalSeconds;}
            catch(Exception ex){Fault(ex);}
        };
        _view.Render+=Frame;
        DpiChanged+=(_,e)=>{
            if(_ready&&!_shutdown&&!_lost&&_view.Context?.MakeCurrent()==true)
                try{_session.SetScale(e.DeviceDpiNew/96f);}catch(Exception ex){Fault(ex);}
        };
        WireInput();
        Controls.Add(_view);
        Shown+=(_,_)=>{_view.Focus();_last=_clock.Elapsed.TotalSeconds;_view.Start();};
    }
    private void WireInput()
    {
        InputState input=_session.Input;
        _view.MouseMove+=(_,e)=>{
            if(_mouse is {} last){input.MouseDx+=e.X-last.X;input.MouseDy+=e.Y-last.Y;}
            _mouse=e.Location;input.MouseX=e.X;input.MouseY=e.Y;
        };
        _view.MouseDown+=(_,e)=>{if(_session.Console.Open)return;_view.Focus();input.MouseX=e.X;input.MouseY=e.Y;input.Buttons|=Map(e.Button);input.Pressed|=Map(e.Button);};
        _view.MouseUp+=(_,e)=>{input.Buttons&=~Map(e.Button);input.Released|=Map(e.Button);};
        _view.MouseWheel+=(_,e)=>{
            _wheel+=e.Delta;int step=Math.Max(1,SystemInformation.MouseWheelScrollDelta);
            input.WheelDelta+=_wheel/step;_wheel%=step;
        };
        KeyDown+=(_,e)=>{
            // On the fault screen the session may be half-torn-down: Esc closes
            // the window directly instead of saving through ShowMenu.
            if(_faulted){if(e.KeyCode==Keys.Escape)Close();return;}
            if(_session.ConsoleKey(e.KeyCode)){e.Handled=true;e.SuppressKeyPress=e.KeyCode is Keys.F1 or Keys.Oemtilde or Keys.Escape or Keys.Enter or Keys.Tab or Keys.Back;UpdateCursor();return;}
            if(e.KeyCode==Keys.Escape){_session.ShowMenu();UpdateCursor();return;}
            input.KeyDown((int)e.KeyCode);
            if(e.KeyCode is Keys.W or Keys.A or Keys.S or Keys.D or Keys.Space){e.Handled=true;e.SuppressKeyPress=true;}
        };
        KeyUp+=(_,e)=>input.KeyUp((int)e.KeyCode);
        KeyPress+=(_,e)=>{if(_session.Console.Open){_session.Console.Insert(e.KeyChar);e.Handled=true;}else input.TypeChar(e.KeyChar);};
        Deactivate+=(_,_)=>{_ime.Cancel();_session.LoseFocus();UpdateCursor();_mouse=null;_wheel=0;};
    }
    private static MouseButton Map(MouseButtons b)=>b switch{
        MouseButtons.Left=>MouseButton.Left,MouseButtons.Right=>MouseButton.Right,
        MouseButtons.Middle=>MouseButton.Middle,_=>MouseButton.None};
    private Rectangle? CaretRectangle()
    {
        var ui=_session.Ui;
        if(ui.TextFocus is not {} field||ui.Font is null)return null;
        var r=field.CaretBounds(ui.Draw);
        return new((int)(r.X*ui.Scale),(int)(r.Y*ui.Scale),Math.Max(1,(int)Math.Ceiling(r.W*ui.Scale)),Math.Max(1,(int)Math.Ceiling(r.H*ui.Scale)));
    }
    private void Frame(GLControl view)
    {
        if(!_ready||_shutdown||_lost)return;
        try{
            double now=_clock.Elapsed.TotalSeconds,dt=now-_last;_last=now;
            if(WindowState==FormWindowState.Minimized){_session.Input.EndFrame();return;}
            int w=view.ClientSize.Width,h=view.ClientSize.Height;
            if(w!=_width||h!=_height){_width=w;_height=h;_session.Resize(w,h);}
            _session.Frame(dt);UpdateCursor();CentreMouse();_ime.RefreshPosition();
        }catch(Exception ex){Fault(ex);}
    }
    private bool _faulted;
    private void Fault(Exception ex)
    {
        if(_shutdown)return;
        // A context loss after a fault, or a DPI change on the fault screen,
        // must not stack a second label and a second Esc handler.
        if(_faulted){Trace.WriteLine(ex);return;}_faulted=true;
        ExitCode=1;_view.Stop();
        _session.LoseFocus();UpdateCursor();Trace.WriteLine(ex);_view.Visible=false;
        var error=new Label{Dock=DockStyle.Fill,BackColor=Color.Black,ForeColor=Color.Silver,Text="PROMZONA\n\n"+ex.Message+"\n\nPress Esc to close.",Padding=new Padding(40)};
        Controls.Add(error);error.BringToFront();
    }
    private void CentreMouse(){
        // Re-centre only during focused on-foot play; reset the remembered point
        // before warping so the synthetic mouse event is not treated as input.
        if(!_cursorHidden||!_session.HasCity||!(_session.City.Model.Traffic.OnFoot||_session.City.Model.Traffic.Hero.Kind is (11 or 14)))return;
        var centre=new Point(_view.ClientSize.Width/2,_view.ClientSize.Height/2);_mouse=centre;Cursor.Position=_view.PointToScreen(centre);
    }
    private void UpdateCursor()
    {
        bool hide=!_shutdown&&_ready&&_session.Active&&!_session.Console.Open&&_session.Screen is GameScreen.Playing or GameScreen.Generating;
        if(hide==_cursorHidden)return;_cursorHidden=hide;if(hide){Cursor.Hide();_mouse=null;}else{Cursor.Show();_mouse=null;}
    }
    protected override void OnPaintBackground(PaintEventArgs e)=>e.Graphics.Clear(Color.Black);
    protected override void WndProc(ref Message m){if(m.Msg==0x14){BlackWindow.Paint(m.HWnd,m.WParam);m.Result=1;return;}base.WndProc(ref m);}
    private void Shutdown()
    {
        if(_shutdown)return;_shutdown=true;UpdateCursor();_view.Stop();
        _session.Ui.TextFocusChanging-=_ime.Cancel;_ime.Dispose();
        // Without a usable GL context the session still saves and releases its
        // CPU-side resources (audio device, generation task); only the GPU
        // objects are left to the process exit, as before.
        try{_session.Dispose(!_lost&&_view.Context is {IsLegacy:false} context&&context.MakeCurrent());}
        catch(Exception ex){ExitCode=1;Trace.WriteLine(ex);}
    }
    protected override void OnFormClosing(FormClosingEventArgs e){base.OnFormClosing(e);if(!e.Cancel)Shutdown();}
    protected override void Dispose(bool disposing){if(disposing)Shutdown();base.Dispose(disposing);}
}
