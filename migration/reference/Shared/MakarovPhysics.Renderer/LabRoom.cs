namespace MakarovPhysics.Renderer;

/// <summary>
/// The shared LABORATORY the bench modules stand in (22.08).
///
/// A whole family of modules is one apparatus on an oak bench - Franck-Hertz,
/// Davisson-Germer, the electromagnet, the pulleys - and every one of them
/// stood in the same nothing: a flat dark gradient. That gradient is why
/// additive diagrams wash out over bright wood and why a bench reads as a
/// drawing of a bench. A room fixes both: it gives the eye a floor to place
/// the table on and a wall to judge brightness against.
///
/// It is drawn like <see cref="SkyDome"/> - one cube around the camera, depth
/// writes off, everything else renders over it - but the fragment stage does
/// not shade a direction, it RAYCASTS a room. For each pixel the view ray is
/// intersected with the floor, the ceiling and the four walls, and the
/// nearest hit decides the colour. That costs one quadratic-free intersection
/// per pixel and buys real parallax: walk the camera around the bench and the
/// floor tiles and the ceiling panels slide past like a room's do. A cube of
/// gradients cannot do that - it turns with the camera and the eye reads it
/// as paint.
///
/// The room is anchored in WORLD space, not on the camera, so the bench keeps
/// its place in it. Give it the height of the floor the bench stands on and
/// it lines up on its own.
///
/// ADOPTING IT IN A MODULE - three lines, no other change:
///
///     _room = new LabRoom { FloorY = benchTopY - 0.95f };   // in Initialize
///     _room?.Draw(view, proj, _camera.Position(), _camera.Far);   // first in Render
///     _room?.Dispose();                                     // in Dispose
///
/// Draw it FIRST, before any opaque geometry: it writes no depth, and a pass
/// that writes no depth is repainted by everything drawn after it (the lesson
/// of Davisson-Germer, tail296).
///
/// GL resources are created in the constructor, so build it from Initialize,
/// on the render thread, never from a module constructor. Construction is
/// transactional.
/// </summary>
public sealed class LabRoom : IDisposable
{
    private readonly Mesh _cube;
    private readonly Shader _shader;

    /// <summary>World height of the lab floor. Set it to the underside of the
    /// bench's legs - or, when a module draws a bench slab and no legs, about
    /// a metre under the slab, which is what a table height looks like.</summary>
    public float FloorY = -1.0f;

    /// <summary>Half the room's width and depth. The default is a room about
    /// fourteen units across, which reads as a large teaching lab at the scale
    /// the bench modules use.</summary>
    public float RoomHalf = 7.0f;

    /// <summary>Ceiling height ABOVE <see cref="FloorY"/>.</summary>
    public float CeilingHeight = 3.4f;

    /// <summary>Where the room's own light comes from. Walls facing it are
    /// shaded a touch brighter.
    ///
    /// It is a WORLD direction and it stays one (23.08). The apparatus inside
    /// the room is lit by a key locked to the viewer now (see
    /// <see cref="KeyLight"/>), but the room is not apparatus: it is geometry
    /// with ceiling panels in it, and a wall that brightens because the user
    /// turned around destroys the very parallax this class was built for.
    /// The former <c>LightDirFrom</c> helper - which copied the scene's key in
    /// here - went with that reasoning; its single caller (Franck-Hertz) no
    /// longer has a world key to hand over.</summary>
    public Vec3 LightDir = new(0.38f, 0.80f, 0.48f);

    public Vec3 WallColor = new(0.335f, 0.350f, 0.375f);
    public Vec3 FloorColor = new(0.200f, 0.205f, 0.220f);
    public Vec3 CeilingColor = new(0.360f, 0.370f, 0.385f);

    /// <summary>Brightness of the ceiling light panels, 0 turns them off.
    /// Halved 02.09 (visual audit): the ceiling must not outshine the bench.</summary>
    public float PanelStrength = 0.5f;

    /// <summary>How strongly distance washes the room toward its haze colour.
    /// Zero gives a hard-edged doll's house; the default keeps the far corners
    /// from competing with the apparatus.</summary>
    public float Haze = 0.55f;

    public LabRoom()
    {
        Mesh? cube = null;
        Shader? shader = null;

        try
        {
            cube = Primitives.Cube();
            shader = new Shader(RoomVertexShader, RoomFragmentShader);
        }
        catch
        {
            cube?.Dispose();
            shader?.Dispose();
            throw;
        }

        _cube = cube;
        _shader = shader;
    }

    /// <summary>Draw the room around the camera. Depth writes are disabled for
    /// the draw and restored afterwards, exactly like the sky dome, so the
    /// caller's state comes back untouched.</summary>
    public void Draw(Mat4 view, Mat4 proj, Vec3 camPos, float farPlane)
    {
        GL.DepthMask(false);
        GL.Disable(GL.DEPTH_TEST);
        GL.Disable(GL.CULL_FACE); // the camera is inside the cube

        _shader.Use();
        _shader.SetMatrix4("uView", view);
        _shader.SetMatrix4("uProj", proj);
        // same rule as the dome: half-diagonal = scale/2*sqrt(3), so 1.1x the
        // far plane keeps every corner inside the frustum
        float scale = farPlane * 1.1f;
        _shader.SetMatrix4("uModel", Mat4.Translate(camPos) * Mat4.Scale(scale, scale, scale));
        _shader.SetVec3("uCamPos", camPos);
        _shader.SetVec3("uLightDir", LightDir);
        _shader.SetVec3("uWallColor", WallColor);
        _shader.SetVec3("uFloorColor", FloorColor);
        _shader.SetVec3("uCeilingColor", CeilingColor);

        // THE CAMERA MUST BE INSIDE THE ROOM. The shader takes the nearest
        // positive hit, which is only "the wall I am facing" while the eye is
        // between the walls; from outside it picks the near face and the room
        // turns inside out. Modules set their own zoom limits and nobody
        // should have to check them against a wall position, so the room
        // stretches to contain the camera instead of breaking: walls, floor
        // and ceiling are pushed out with a margin when the eye passes them.
        // Everything is world-anchored, so the tiles and panels keep their
        // size - only the far walls recede.
        float half = Math.Max(1f, RoomHalf);
        half = Math.Max(half, Math.Max(Math.Abs(camPos.X), Math.Abs(camPos.Z)) + 1.5f);
        float floorY = Math.Min(FloorY, camPos.Y - 0.6f);
        float ceilingY = Math.Max(floorY + Math.Max(0.5f, CeilingHeight), camPos.Y + 0.6f);

        _shader.SetFloat("uFloorY", floorY);
        _shader.SetFloat("uRoomHalf", half);
        _shader.SetFloat("uCeilingY", ceilingY);
        _shader.SetFloat("uPanels", Math.Clamp(PanelStrength, 0f, 2f));
        _shader.SetFloat("uHaze", Math.Clamp(Haze, 0f, 1f));
        _cube.Draw();

        GL.Enable(GL.CULL_FACE);
        GL.Enable(GL.DEPTH_TEST);
        GL.DepthMask(true);
    }

    public void Dispose()
    {
        _cube.Dispose();
        _shader.Dispose();
    }

    // The room shaders live here, next to the only class that uses them - the
    // same rule the dome follows: a shared resource travels with its assets.
    private const string RoomVertexShader = @"#version 330 core
layout(location = 0) in vec3 aPos;
uniform mat4 uModel;
uniform mat4 uView;
uniform mat4 uProj;
out vec3 vWorldPos;
void main()
{
    vec4 world = uModel * vec4(aPos, 1.0);
    vWorldPos = world.xyz;
    gl_Position = uProj * uView * world;
}";

    private const string RoomFragmentShader = @"#version 330 core
in vec3 vWorldPos;
out vec4 FragColor;

uniform vec3 uCamPos;
uniform vec3 uLightDir;
uniform vec3 uWallColor;
uniform vec3 uFloorColor;
uniform vec3 uCeilingColor;
uniform float uFloorY;
uniform float uCeilingY;
uniform float uRoomHalf;
uniform float uPanels;
uniform float uHaze;

/* A soft rectangular pattern: 1 inside the tile, falling off over `edge`
   world units near the seam. Screen-space derivatives set the width so the
   lines stay one pixel wide however far away they are - the same LOD idea
   the procedural bump needed (22.08), applied before it can go wrong. */
float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float vnoise(vec2 p)
{
    vec2 i = floor(p), f = fract(p);
    float a = hash21(i), b = hash21(i + vec2(1, 0)), c = hash21(i + vec2(0, 1)), e = hash21(i + vec2(1, 1));
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(a, b, u.x), mix(c, e, u.x), u.y);
}

float grid(vec2 p, float pitch, float edge)
{
    vec2 f = abs(fract(p / pitch) - 0.5) * pitch;
    vec2 w = fwidth(p) + 1e-5;
    vec2 line = smoothstep(edge - w, edge + w, f);
    return min(line.x, line.y);
}

void main()
{
    vec3 d = normalize(vWorldPos - uCamPos);

    /* Distance to each of the six planes, keeping only the ones the ray can
       actually reach. No quadratics, no loop: a room is an axis-aligned box
       and the ray hits exactly one of its faces. */
    float tFloor   = d.y < -1e-5 ? (uFloorY   - uCamPos.y) / d.y : 1e9;
    float tCeiling = d.y >  1e-5 ? (uCeilingY - uCamPos.y) / d.y : 1e9;
    float tXp = d.x >  1e-5 ? ( uRoomHalf - uCamPos.x) / d.x : 1e9;
    float tXm = d.x < -1e-5 ? (-uRoomHalf - uCamPos.x) / d.x : 1e9;
    float tZp = d.z >  1e-5 ? ( uRoomHalf - uCamPos.z) / d.z : 1e9;
    float tZm = d.z < -1e-5 ? (-uRoomHalf - uCamPos.z) / d.z : 1e9;

    float tWall = min(min(tXp, tXm), min(tZp, tZm));
    float t = min(min(tFloor, tCeiling), tWall);
    vec3 hit = uCamPos + d * t;

    vec3 col;
    float lightFace = 1.0;

    if (t == tFloor)
    {
        /* Lino tiles, wide and low contrast: a floor is what tells the eye
           how big the bench is, and a busy floor competes with the physics. */
        float g = grid(hit.xz, 1.0, 0.020);
        /* two-tone lino: alternate tiles a couple of percent apart, and
           every tile carries its own faint cast - a poured floor reads as
           plastic, a tiled one as a floor (24.08) */
        vec2 tileId = floor(hit.xz);
        float check = mod(tileId.x + tileId.y, 2.0);
        // (`cast` is a GLSL reserved word - glslcheck 25.08)
        float tint = hash21(tileId) - 0.5;
        col = uFloorColor * (0.86 + 0.20 * g)
            * (1.0 + 0.030 * (check - 0.5) + 0.024 * tint);
        /* a wide, soft pool of light under the ceiling fittings */
        float pool = exp(-dot(hit.xz, hit.xz) * 0.010);
        col *= 1.0 + 0.55 * pool * uPanels;
        /* corner AO: the floor darkens softly where it meets the walls,
           which is what visually GLUES a room together */
        float wallDist = uRoomHalf - max(abs(hit.x), abs(hit.z));
        col *= 0.72 + 0.28 * smoothstep(0.0, 1.4, wallDist);
        lightFace = 0.95;
    }
    else if (t == tCeiling)
    {
        /* Recessed panels on a 2.4 grid, the fittings a third of the cell.
           They are the reason the room reads as lit from above, which is the
           direction every bench module already lights its apparatus from. */
        vec2 cell = abs(fract(hit.xz / 2.4) - 0.5);
        float m = max(cell.x, cell.y);
        /* an edge one pixel wide at any distance: a hard step turned the
           far panels into horizontal streaks of random pixel width on a
           grazing view (Mac frames 25.08) - the same fwidth rule the tile
           seams already follow */
        float pw = fwidth(m) + 1e-5;
        float panel = 1.0 - smoothstep(0.17 - pw, 0.17 + pw, m);
        /* a soft halo bleeding off each fitting: a lamp that lights the
           room must first light its own ceiling */
        float halo = smoothstep(0.34, 0.17, m) * 0.30;
        col = uCeilingColor * (0.92 + 0.10 * grid(hit.xz, 2.4, 0.030));
        col += vec3(1.00, 0.97, 0.90) * (panel * 0.85 + halo) * uPanels;
        float wallDistC = uRoomHalf - max(abs(hit.x), abs(hit.z));
        col *= 0.80 + 0.20 * smoothstep(0.0, 1.2, wallDistC);
        lightFace = 1.0;
    }
    else
    {
        /* Walls: a painted upper half over a darker dado, with the seam at a
           metre and a bit off the floor. The band is not decoration - it puts
           a horizontal line at a KNOWN height, so the bench has something to
           be measured against. */
        float h = (hit.y - uFloorY) / max(0.001, uCeilingY - uFloorY);
        float dado = smoothstep(0.30, 0.33, h);
        vec3 wall = mix(uWallColor * 0.72, uWallColor, dado);
        /* a thin bright line right on the seam, like a rail; smoothstep and
           not a step, or the band aliases into a crawling wire at a distance */
        float rail = 1.0 - smoothstep(0.0, 0.010, abs(h - 0.315));
        wall += vec3(0.055) * rail;
        /* roller-painted plaster: a whisper of low-frequency noise on the
           painted half, so the wall stops being a mathematical plane */
        vec2 wallUv = (t == tXp || t == tXm)
            ? vec2(hit.z, hit.y)
            : vec2(hit.x, hit.y);
        wall *= 1.0 + 0.035 * (vnoise(wallUv * 1.7) - 0.5) * dado;
        /* baseboard: the dark strip every real wall stands on */
        float base = 1.0 - smoothstep(0.020, 0.042, h);
        wall = mix(wall, uWallColor * 0.38, base * 0.85);
        /* the ceiling spills down the top of the wall */
        wall *= 1.0 + 0.28 * smoothstep(0.72, 1.0, h) * uPanels;
        /* vertical AO into the floor line and corner AO where two walls
           meet - the same glue the floor got */
        wall *= 0.80 + 0.20 * smoothstep(0.0, 0.55, h);
        float alongWall = (t == tXp || t == tXm) ? abs(hit.z) : abs(hit.x);
        wall *= 0.80 + 0.20 * smoothstep(0.0, 1.3, uRoomHalf - alongWall);
        col = wall;

        /* which wall it is decides how it faces the key light */
        vec3 n = vec3(0.0);
        if (t == tXp) n = vec3(-1.0, 0.0, 0.0);
        else if (t == tXm) n = vec3(1.0, 0.0, 0.0);
        else if (t == tZp) n = vec3(0.0, 0.0, -1.0);
        else n = vec3(0.0, 0.0, 1.0);
        lightFace = 0.88 + 0.12 * max(dot(n, normalize(uLightDir)), 0.0);
    }

    col *= lightFace;

    /* Distance haze. Without it the far corners are as crisp as the bench and
       the eye has no idea what to look at; with it the room recedes and the
       apparatus stays the subject. */
    float fog = 1.0 - exp(-t * 0.045 * uHaze);
    vec3 haze = mix(uWallColor, uCeilingColor, 0.35) * 0.92;
    col = mix(col, haze, clamp(fog, 0.0, 1.0));

    FragColor = vec4(col, 1.0);
}";
}
