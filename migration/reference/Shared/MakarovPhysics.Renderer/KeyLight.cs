namespace MakarovPhysics.Renderer;

/// <summary>
/// The studio key light, stated in VIEW space and pushed to world every frame
/// (23.08, Michael: "in many modules the light turns together with the scene;
/// it should stay put and shine from the user's side, from about upper-left").
///
/// THE DEFECT. Every lit shader in the suite takes <c>uLightDir</c> as a WORLD
/// direction, and every module stated it as a constant near (0.38, 0.80, 0.48).
/// A constant world direction under an orbiting camera glues the highlight to
/// the OBJECT: drag the scene around and the lit face travels with it, which
/// the eye reads as a lamp bolted to the apparatus. Measured in
/// <c>Tools/twins/keylight_twin.py</c>: the point of a sphere that faces the
/// camera - the point the user is looking at the whole time - swings between
/// N.L = 0.84 and N.L = 0.00 over one turn of the orbit. Half of every scene
/// went black on the way round.
///
/// THE FIX. State the key where it belongs - in the viewer's frame - and give
/// the shader the world direction that lands there this frame. For a
/// directional light that is pure rotation: the view matrix's rotation rows
/// are (side, up, -forward), so w = R^T * v costs three multiply-adds and is
/// exact (the twin's round trip returns the constant to 3e-16).
///
/// THE CONSTANT. <see cref="View"/> is 26.6 degrees to the LEFT of the screen
/// axis and 31.5 degrees ABOVE it - a classic three-quarter key, and inside
/// the band the modules were already tuned for: measured at their default
/// cameras, the old world constants sat between 40 degrees right and 9 degrees
/// left, at elevations of 24 to 47 degrees. The scene's mean brightness moves
/// by -8% to +4% depending on the pose, which ambient (0.5 and up in most
/// modules) more than absorbs. Retuning the whole suite is editing this one
/// vector.
///
/// WHAT MUST NOT USE IT. A light that is part of the PHYSICS or of the world
/// stays in world coordinates, and there the old constants are right:
/// - a sun in a <see cref="SkyDome"/> - the dome draws the sun disk at
///   uLightDir, so a camera-locked sun would follow the viewer across the sky;
/// - day and night on <see cref="Globe"/>, where the terminator IS the answer;
/// - the room in <see cref="LabRoom"/> - it is geometry with ceiling panels,
///   and its walls must not brighten because the user turned around;
/// - a lamp, laser or beam the module DRAWS, where the shading has to agree
///   with the visible source.
/// Those sites are listed with their reason in <c>Tools/keylightcheck.py</c>,
/// which fails on any new hard-coded uLightDir that is not.
///
/// USE (the whole change in a module is one line):
///     _lit.SetVec3("uLightDir", KeyLight.FromView(view));
/// </summary>
public static class KeyLight
{
    /// <summary>
    /// The key in VIEW space: x to the right of the screen, y up, z out of the
    /// screen towards the viewer. Negative x puts it on the left.
    /// </summary>
    public static readonly Vec3 View = new(-0.38f, 0.52f, 0.76f);

    /// <summary>
    /// Where the key must be if the world direction is unusable - a view
    /// matrix that is not finite, or a degenerate rotation. It is the constant
    /// the bench modules used before this class existed, so a broken frame
    /// looks like the old build rather than like a scene with no light.
    /// </summary>
    public static readonly Vec3 WorldFallback = new(0.38f, 0.80f, 0.48f);

    /// <summary>
    /// The world direction TOWARDS the key for this frame's view matrix - the
    /// convention every lit shader in the suite uses.
    /// </summary>
    public static Vec3 FromView(Mat4 view)
    {
        if (!view.IsFinite) return WorldFallback;

        // rotation ROWS of a LookAt view matrix: side, up, -forward
        Vec3 side = new(view[0, 0], view[0, 1], view[0, 2]);
        Vec3 up = new(view[1, 0], view[1, 1], view[1, 2]);
        Vec3 back = new(view[2, 0], view[2, 1], view[2, 2]);

        Vec3 world = side * View.X + up * View.Y + back * View.Z;
        Vec3 normalized = world.Normalized();
        return normalized == Vec3.Zero ? WorldFallback : normalized;
    }

    /// <summary>The same key straight from the camera, for call sites that
    /// hold the camera but not the matrix.</summary>
    public static Vec3 FromCamera(OrbitCamera camera) => FromView(camera.View());
}
