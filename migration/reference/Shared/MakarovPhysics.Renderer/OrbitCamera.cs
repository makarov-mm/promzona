namespace MakarovPhysics.Renderer;

public sealed class OrbitCamera
{
    public Vec3 Target = new(0, 0, 0);
    public float Distance = 8f;
    public float Yaw = 0.7f;
    public float Pitch = 0.5f;
    public float Fovy = MathF.PI / 4f;
    public float Near = 0.05f;
    public float Far = 500f;

    /// <summary>
    /// Zoom limits. Keep MaxDistance close to the scene size: an unbounded
    /// zoom-out lets an accidental wheel spin shrink the scene to nothing and
    /// the experiment "disappears" for the user. Modules with large scenes
    /// (projectile range) raise the limit explicitly.
    /// </summary>
    public float MinDistance = 0.5f;
    public float MaxDistance = 80f;

    private const float PitchLimit = MathF.PI / 2f - 0.02f;

    /// <summary>
    /// The scene is a DRAWING: one plane, seen square-on. Set by a module
    /// whose whole content lies in that plane - a Voronoi diagram, a sorting
    /// race, a bifurcation plot.
    ///
    /// Such a module used to get the same right-drag as a 3D one, and the
    /// drag tilted the sheet of paper: circles became ellipses, the axes
    /// stopped being perpendicular, and nothing was gained, because there is
    /// nothing behind a plane to look at (Michael, 21.09: "a module made in 2D
    /// must not be rotatable in 3D space"). Every module drives its own
    /// camera, so the rule lives HERE, in the two calls they all make: with
    /// Planar set, <see cref="Orbit"/> does nothing and <see cref="Drag"/>
    /// slides the drawing under the cursor instead of turning it. Zoom is
    /// unchanged. `planarcheck` keeps the declared modules declared.
    /// </summary>
    public bool Planar;

    /// <summary>
    /// For a module whose scenes are NOT all of one kind - three drawings and
    /// a cube (MonteCarlo), two plane orbits and a Lorenz attractor. Called
    /// every frame with "is the current scene a drawing"; on the change to a
    /// drawing the view is squared up, because a drawing left at the tilt of
    /// the 3D scene before it is the bug this flag exists to end. The way
    /// back is the module's business: only it knows its 3D pose.
    /// </summary>
    public void SetPlanar(bool drawing)
    {
        if (drawing == Planar) return;
        Planar = drawing;
        if (drawing)
        {
            Pitch = 0f;
            Yaw = 0f;
        }
    }

    public void Orbit(float dYaw, float dPitch)
    {
        if (Planar) return;
        // NaN deltas (degenerate input math) must not poison the camera, and a
        // long auto-orbit session must not drift Yaw into float imprecision
        if (!float.IsFinite(dYaw) || !float.IsFinite(dPitch)) return;
        Yaw = MathF.IEEERemainder(Yaw + dYaw, 2f * MathF.PI);
        Pitch += dPitch;

        if (Pitch > PitchLimit) Pitch = PitchLimit;
        if (Pitch < -PitchLimit) Pitch = -PitchLimit;
    }

    /// <summary>
    /// Orbit from a MOUSE DRAG, in pixels. The vertical sign is already right
    /// - dragging down raises the camera and the scene sinks with the cursor -
    /// but the horizontal one was not: dragging right increased Yaw, which
    /// walks the camera to the right and sends the scene the other way. Every
    /// module passed MouseDx straight into Orbit, so every module in the suite
    /// spun the wrong way under the hand (found on Laser, 14.08).
    ///
    /// The convention is the one every 3D tool uses and the one a globe has:
    /// the scene follows the cursor. Auto-rotation still calls Orbit directly,
    /// where the argument means "increase the yaw" and no hand is involved.
    /// </summary>
    public void Drag(int mouseDx, int mouseDy, float sensitivity)
    {
        if (mouseDx == 0 && mouseDy == 0) return;
        _handHeld = true;
        if (Planar)
        {
            Pan(mouseDx, mouseDy);
            return;
        }
        Orbit(-mouseDx * sensitivity, mouseDy * sensitivity);
    }

    /// <summary>
    /// Slide the target in the view plane by a mouse move in pixels, so that
    /// the drawing follows the cursor. The camera does not know the viewport,
    /// so a pixel is taken as 1/1000 of the visible height at the target's
    /// distance - exact on a 1000-pixel-high scene, and close enough on any
    /// other that the drawing stays under the hand.
    /// </summary>
    public void Pan(int mouseDx, int mouseDy)
    {
        float perPixel = 2f * Distance * MathF.Tan(Fovy / 2f) / 1000f;
        if (!float.IsFinite(perPixel)) return;
        _handHeld = true; // the reader framed the drawing; an author preset must not undo it
        Vec3 forward = (Target - Position()).Normalized();
        Vec3 right = Vec3.Cross(forward, Vec3.UnitY).Normalized();
        Vec3 up = Vec3.Cross(right, forward);
        Target = Target - right * (mouseDx * perPixel) + up * (mouseDy * perPixel);
    }

    public void Zoom(float factor)
    {
        // Zoom(NaN) would make Distance NaN forever (NaN fails both clamps)
        if (!float.IsFinite(factor) || factor <= 0f) return;
        _handHeld = true;
        Distance *= factor;
        if (Distance < MinDistance) Distance = MinDistance;
        if (Distance > MaxDistance) Distance = MaxDistance;
    }

    public Vec3 Position()
    {
        float cp = MathF.Cos(Pitch);

        return Target + new Vec3(
            Distance * cp * MathF.Sin(Yaw),
            Distance * MathF.Sin(Pitch),
            Distance * cp * MathF.Cos(Yaw));
    }

    public Mat4 View() => Mat4.LookAt(Position(), Target, Vec3.UnitY);
    public Mat4 Projection(float aspect) => Mat4.Perspective(Fovy, aspect <= 0 ? 1f : aspect, Near, Far);

    // ------------------------------------------------------- author presets

    /// <summary>The preset in force, or null once the reader has taken the
    /// camera in hand (a zoom); a preset re-fits itself to the viewport, a
    /// hand-held camera is left alone.</summary>
    public CameraPreset? Preset { get; private set; }
    private float _fitAspect = -1f;
    private bool _handHeld;

    /// <summary>
    /// The distance at which a sphere of `radius` about the target fills the
    /// viewport with `margin` to spare, on THIS aspect: the narrower of the
    /// vertical and horizontal fields of view decides, so an ultrawide
    /// window backs off for height and a tall one for width (visual audit
    /// 02.09: the same apparatus read as a close-up on 16:9 and as a room
    /// with a speck in it on 21:9). The viewport is the module's render
    /// area, so the side panel is already out of the count.
    /// </summary>
    public float FitDistance(float radius, float aspect, float margin = 1.15f)
    {
        if (aspect <= 0 || !float.IsFinite(aspect)) aspect = 1f;
        float fovX = 2f * MathF.Atan(MathF.Tan(Fovy / 2f) * aspect);
        float half = MathF.Min(Fovy, fovX) / 2f;
        float d = radius * margin / MathF.Max(0.05f, MathF.Sin(half));
        return MathF.Max(MinDistance, MathF.Min(MaxDistance, d));
    }

    /// <summary>Put the camera on an author's preset and fit it to the aspect.</summary>
    public void Apply(CameraPreset preset, float aspect)
    {
        Preset = preset;
        Target = preset.Target;
        Yaw = preset.Yaw;
        Pitch = preset.Pitch;
        Distance = FitDistance(preset.Radius, aspect, preset.Margin);
        _fitAspect = aspect;
        _handHeld = false;
    }

    /// <summary>Call at the top of every frame with the current aspect: a
    /// preset re-fits when the window changes shape, a hand-held camera
    /// (zoomed by the reader) does not.</summary>
    public void Refit(float aspect)
    {
        if (Preset is null || _handHeld || !float.IsFinite(aspect) || aspect <= 0) return;
        if (MathF.Abs(aspect - _fitAspect) < 1e-3f) return;
        Distance = FitDistance(Preset.Radius, aspect, Preset.Margin);
        _fitAspect = aspect;
    }

    /// <summary>The reader took the camera: the next Refit leaves it alone.</summary>
    public void TakeInHand() => _handHeld = true;

    /// <summary>Whether a drag, pan or zoom has taken the camera out of automatic framing.</summary>
    public bool HandHeld => _handHeld;

    /// <summary>Return to automatic framing on the next FrameScene call.</summary>
    public void ResetFrame() => _handHeld = false;

    /// <summary>
    /// Fit all eight corners of a world-space box into the unobscured part of
    /// the viewport, keeping the author's yaw and pitch. Call before making
    /// the view/projection matrices. Bounds describe the whole experiment,
    /// not the moving body, so animation never makes the camera chase it.
    /// A mouse drag or zoom holds the reader's view until ResetFrame.
    /// Returns false for invalid bounds or when the far/zoom limit cannot fit
    /// them; in that case the old view is left untouched.
    /// </summary>
    public bool FrameScene(Vec3 min, Vec3 max, float aspect, SceneInsets? insets = null, float margin = 1.08f)
    {
        if (!min.IsFinite || !max.IsFinite || min.X > max.X || min.Y > max.Y || min.Z > max.Z) return false;
        Span<Vec3> corners = stackalloc Vec3[8];
        for (int i=0;i<8;i++) corners[i] = new Vec3((i&1)==0 ? min.X : max.X, (i&2)==0 ? min.Y : max.Y, (i&4)==0 ? min.Z : max.Z);
        return FramePoints(corners,aspect,insets,margin);
    }

    /// <summary>Fit a sampled shape/envelope without the empty corners of its world box.</summary>
    public bool FramePoints(ReadOnlySpan<Vec3> points, float aspect, SceneInsets? insets = null, float margin = 1.08f)
    {
        if (points.Length == 0) return false;
        Vec3 min=points[0],max=points[0];
        foreach(Vec3 p in points)
        {
            if (!p.IsFinite) return false;
            min=new Vec3(MathF.Min(min.X,p.X),MathF.Min(min.Y,p.Y),MathF.Min(min.Z,p.Z));
            max=new Vec3(MathF.Max(max.X,p.X),MathF.Max(max.Y,p.Y),MathF.Max(max.Z,p.Z));
        }
        if (_handHeld) return false;
        float top = insets?.Top ?? 0f, bottom = insets?.Bottom ?? 0f;
        if (!min.IsFinite || !max.IsFinite || min.X > max.X || min.Y > max.Y || min.Z > max.Z
            || !float.IsFinite(aspect) || aspect <= 0f
            || !float.IsFinite(margin) || margin < 1f
            || !float.IsFinite(top) || !float.IsFinite(bottom)
            || top < 0f || bottom < 0f || top + bottom >= 0.9f
            || !float.IsFinite(Yaw) || !float.IsFinite(Pitch)
            || !float.IsFinite(Fovy) || Fovy <= 0f || Fovy >= MathF.PI
            || !float.IsFinite(Near) || !float.IsFinite(Far) || Near <= 0f || Far <= Near
            || !float.IsFinite(MinDistance) || !float.IsFinite(MaxDistance)
            || MinDistance <= 0f || MaxDistance < MinDistance)
            return false;

        Vec3 centre = min * 0.5f + max * 0.5f;
        float cp = MathF.Cos(Pitch);
        Vec3 back = new(cp * MathF.Sin(Yaw), MathF.Sin(Pitch), cp * MathF.Cos(Yaw));
        Vec3 right = Vec3.Cross(Vec3.UnitY, back).Normalized();
        if (right.LengthSquared() < 0.5f) return false;
        Vec3 up = Vec3.Cross(back, right);
        float tanY = MathF.Tan(Fovy * 0.5f), tanX = tanY * aspect;
        float centreY = bottom - top;
        float halfY = (1f - top - bottom) / margin;
        float distance = MinDistance;
        float furthestZ = float.PositiveInfinity;
        foreach (Vec3 corner in points)
        {
            Vec3 q = corner - centre;
            float x = Vec3.Dot(q, right), y = Vec3.Dot(q, up), z = Vec3.Dot(q, back);
            // Perspective depth is distance - z. Include z in EVERY plane
            // inequality; fitting the projected centre alone clips near corners.
            distance = MathF.Max(distance, z + MathF.Abs(x) * margin / tanX);
            distance = MathF.Max(distance, (y / tanY + (centreY + halfY) * z) / halfY);
            distance = MathF.Max(distance, (-y / tanY + (halfY - centreY) * z) / halfY);
            distance = MathF.Max(distance, z + Near * 1.05f);
            furthestZ = MathF.Min(furthestZ, z);
        }
        // The scene centre sits above a bottom card without changing projection
        // or viewport coordinates: move the look-at target down in the view plane.
        Vec3 target = centre - up * (centreY * distance * tanY);
        if (!float.IsFinite(distance) || !target.IsFinite || distance > MaxDistance
            || distance - furthestZ >= Far) return false;
        Target = target;
        Distance = distance;
        Preset = null;
        return true;
    }
}

/// <summary>
/// An author's camera for one module: where to look, from which side, and
/// the radius of the sphere that must be in frame. Distance is not stored -
/// it follows from the radius and the viewport (see OrbitCamera.FitDistance),
/// which is what makes one preset right on 16:9 and 21:9 alike. Key names
/// the shell string ("Camera.Overview", "Camera.Close", "Camera.Instrument")
/// or a module string.
/// </summary>
public sealed record CameraPreset(string Key, Vec3 Target, float Yaw, float Pitch, float Radius, float Margin = 1.15f);
