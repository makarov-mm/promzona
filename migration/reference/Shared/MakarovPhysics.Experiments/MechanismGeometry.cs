using MakarovPhysics.Renderer;
using MakarovPhysics.RigidBodies;

namespace MakarovPhysics.Experiments;

/// <summary>Machined parts for the mechanism exhibits: involute spur, helical and bevel gears, shafts
/// in pillow blocks, motors, brakes, pulleys and links. Shapes only - a moving part is drawn in the
/// frame of its body: a centre, the axis, and a reference direction across the axis (the body's
/// local X), so the picture turns exactly as far as the solver turned the body.</summary>
public static class MechanismGeometry
{
    public static readonly Vec3 Steel = new(.62f, .67f, .71f), DarkSteel = new(.27f, .30f, .33f), Brass = new(.80f, .57f, .24f),
        Copper = new(.78f, .40f, .17f), Rubber = new(.05f, .06f, .07f), Cast = new(.20f, .23f, .26f);

    public static Vec3 V(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>Drawing frame of a part built by MechanismParts: centre, axis (local Z) and the
    /// reference across it (local X) - for a link, local X runs along the link.</summary>
    public static (Vec3 Centre, Vec3 Axis, Vec3 Reference) Frame(RigidBody body) =>
        (V(body.Position), V(System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, body.Rotation)), V(System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitX, body.Rotation)));

    /// <summary>Signed angle about the axis that carries one direction onto another (both projected across the axis).</summary>
    public static float AngleAbout(Vec3 axis, Vec3 from, Vec3 to)
    {
        var a = axis.Normalized();
        var f = Across(a, from);
        var t = Across(a, to);
        return MathF.Atan2(Vec3.Dot(a, Vec3.Cross(f, t)), Vec3.Dot(f, t));
    }

    /// <summary>Extruded star-shaped plate: a closed outline in polar form (radius, angle) about the
    /// centre, filled by a fan from the centre - every radial notch of a Geneva wheel qualifies.</summary>
    public static void Profile(DioramaGeometry g, Vec3 centre, Vec3 axis, Vec3 reference, IReadOnlyList<(float R, float T)> outline, float thickness, Vec3 color, float phase = 0)
    {
        var a = axis.Normalized();
        var u = Across(a, reference);
        var v = Vec3.Cross(a, u);
        Vec3 P(float r, float t, float z) => centre + (u * MathF.Cos(t + phase) + v * MathF.Sin(t + phase)) * r + a * z;
        float h = thickness / 2;
        for (int i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var q = outline[(i + 1) % outline.Count];
            g.Quad(P(p.R, p.T, -h), P(q.R, q.T, -h), P(q.R, q.T, h), P(p.R, p.T, h), color);
            g.Triangle(P(0, 0, h), P(p.R, p.T, h), P(q.R, q.T, h), color);
            g.Triangle(P(0, 0, -h), P(q.R, q.T, -h), P(p.R, p.T, -h), color);
        }
    }

    /// <summary>Unit vector across the axis, as close as possible to the hint.</summary>
    public static Vec3 Across(Vec3 axis, Vec3 hint)
    {
        var a = axis.Normalized();
        var r = hint - a * Vec3.Dot(hint, a);
        if (r.LengthSquared() < 1e-8f) r = Vec3.Cross(a, MathF.Abs(a.Y) < .9f ? Vec3.UnitY : Vec3.UnitX);
        return r.Normalized();
    }

    /// <summary>Involute outline of a gear in polar form (radius, angle), tooth 0 centred on angle
    /// zero; 20° pressure angle, addendum one module, dedendum 1.25 modules.</summary>
    public static List<(float R, float T)> Outline(float pitchRadius, int teeth, int flankPoints = 6)
    {
        float m = 2 * pitchRadius / teeth, root = pitchRadius - 1.25f * m, tip = pitchRadius + m;
        float rb = pitchRadius * MathF.Cos(20 * MathF.PI / 180), pitch = MathF.Tau / teeth;
        float Inv(float r)
        {
            float alpha = MathF.Acos(Math.Clamp(rb / r, -1, 1));
            return MathF.Tan(alpha) - alpha;
        }

        float half = MathF.PI / (2 * teeth) + Inv(pitchRadius);
        float start = Math.Max(root, rb);
        var points = new List<(float, float)>(teeth * (2 * flankPoints + 6));
        for (int k = 0; k < teeth; k++)
        {
            float t = k * pitch;
            points.Add((root, t - pitch / 2));
            points.Add((root, t - half));
            for (int j = 0; j <= flankPoints; j++)
            {
                float r = start + (tip - start) * j / flankPoints;
                points.Add((r, t - half + Inv(r)));
            }

            for (int j = flankPoints; j >= 0; j--)
            {
                float r = start + (tip - start) * j / flankPoints;
                points.Add((r, t + half - Inv(r)));
            }

            points.Add((root, t + half));
        }

        return points;
    }

    /// <summary>Spur or helical gear. The tooth at angle zero of the reference sits on the reference;
    /// a helix twists the outline by width·tan β / r across the face (sign by hand).</summary>
    public static void Gear(DioramaGeometry g, Vec3 centre, Vec3 axis, Vec3 reference, float pitchRadius, int teeth, float width, Vec3 color,
        float helix = 0, float phase = 0, float bore = .05f)
    {
        int firstSurface = g.SurfacePoints.Count;
        var a = axis.Normalized();
        var u = Across(a, reference);
        var v = Vec3.Cross(a, u);
        var outline = Outline(pitchRadius, teeth);
        int slices = Math.Abs(helix) > 1e-4f ? 6 : 1;
        float twist = width * MathF.Tan(helix) / pitchRadius;
        Vec3 P(float r, float t, float z) => centre + (u * MathF.Cos(t) + v * MathF.Sin(t)) * r + a * z;
        float m = 2 * pitchRadius / teeth, rim = pitchRadius - 2.4f * m;
        // The default shaft diameter is suitable for workshop gears, not a
        // centimetre-scale clock train. Keep the web inside its own tooth root.
        bore = Math.Clamp(bore, 0, rim * .35f);

        g.Material = SurfaceMaterial.MachinedMetal;
        for (int s = 0; s < slices; s++)
        {
            float z0 = -width / 2 + width * s / slices, z1 = -width / 2 + width * (s + 1) / slices;
            float t0 = phase + twist * (z0 / width), t1 = phase + twist * (z1 / width);
            for (int i = 0; i < outline.Count; i++)
            {
                var p = outline[i];
                var q = outline[(i + 1) % outline.Count];
                g.Quad(P(p.R, p.T + t0, z0), P(q.R, q.T + t0, z0), P(q.R, q.T + t1, z1), P(p.R, p.T + t1, z1), color);
            }
        }

        // the two faces of the toothed rim
        foreach (float side in new[] { -1f, 1f })
        {
            float z = side * width / 2, t = phase + twist * side / 2;
            for (int i = 0; i < outline.Count; i++)
            {
                var p = outline[i];
                var q = outline[(i + 1) % outline.Count];
                if (side > 0) g.Quad(P(rim, p.T + t, z), P(p.R, p.T + t, z), P(q.R, q.T + t, z), P(rim, q.T + t, z), color);
                else g.Quad(P(rim, q.T + t, z), P(q.R, q.T + t, z), P(p.R, p.T + t, z), P(rim, p.T + t, z), color);
            }
        }

        // Open machined web. These are real openings, not black cylinders laid
        // on an opaque face: the rear carrier and tooth engagement remain visible.
        MachineGeometry.Annulus(g, centre, a, rim * .86f, rim * 1.01f, width * .78f, 0, MathF.Tau, color, 64);
        MachineGeometry.Annulus(g, centre, a, bore, bore * 2.1f, width * 1.15f, 0, MathF.Tau, color, 32);
        float hub = bore * 1.9f, outer = rim * .9f;
        for (int k = 0; k < 6; k++)
        {
            float t = phase + k * MathF.Tau / 6;
            var radial = u * MathF.Cos(t) + v * MathF.Sin(t);
            g.Box(centre + radial * ((hub + outer) / 2),
                new Vec3((outer-hub)/2, pitchRadius*.055f, width*.19f),
                color, radial, Vec3.Cross(a,radial), a);
        }
        // Narrow bright shoulders catch the softboxes without changing tooth geometry.
        foreach(float side in new[]{-1f,1f})
            MachineGeometry.Annulus(g,centre+a*(side*width*.39f),a,rim*.85f,rim*.89f,width*.025f,0,MathF.Tau,color*1.06f,64);

            SurfaceFrame(g, firstSurface, centre, a, u);
    }

    /// <summary>Straight bevel gear on a pitch cone of half-angle cone from the apex: the outline at the
    /// back (cone distance outer) shrinks linearly to the front (outer − face), so the teeth run to the apex.</summary>
    public static void BevelGear(DioramaGeometry g, Vec3 apex, Vec3 axis, Vec3 reference, float cone, float outer, float face, int teeth, Vec3 color, float phase = 0)
    {
        int firstSurface = g.SurfacePoints.Count;
        var a = axis.Normalized();
        var u = Across(a, reference);
        var v = Vec3.Cross(a, u);
        float back = outer * MathF.Sin(cone);
        var outline = Outline(back, teeth);
        float inner = (outer - face) / outer;
        Vec3 P(float r, float t, float s) => apex + (u * MathF.Cos(t + phase) + v * MathF.Sin(t + phase)) * (r * s) + a * (outer * MathF.Cos(cone) * s);
        g.Material = SurfaceMaterial.MachinedMetal;
        for (int i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var q = outline[(i + 1) % outline.Count];
            g.Quad(P(p.R, p.T, inner), P(q.R, q.T, inner), P(q.R, q.T, 1), P(p.R, p.T, 1), color);
            float hubBack = back * .45f;
            g.Quad(P(hubBack, p.T, 1), P(p.R, p.T, 1), P(q.R, q.T, 1), P(hubBack, q.T, 1), color);
            g.Quad(P(hubBack, q.T, inner), P(q.R, q.T, inner), P(p.R, p.T, inner), P(hubBack, p.T, inner), color);
        }

        // hub behind the gear and a bore collar in front
        var backCentre = apex + a * (outer * MathF.Cos(cone));
        MachineGeometry.Annulus(g, backCentre + a * (face * .25f), a, back * .18f, back * .5f, face * .6f, 0, MathF.Tau, color, 48);
            SurfaceFrame(g, firstSurface, apex, a, u);
    }

    /// <summary>Screw thread turning with its body: `starts` helical ridges of the given lead about the
    /// axis on a core at the root radius, trapezoidal in the axial plane - addendum and dedendum about the
    /// pitch radius, flanks at the given angle (20° worm, 14.5° trapezoidal lead screw). Hand +1 is a
    /// right-hand helix about the axis, −1 a left-hand one.</summary>
    public static void Thread(DioramaGeometry g, Vec3 centre, Vec3 axis, Vec3 reference, float pitchRadius, float addendum, float dedendum,
        float lead, int starts, float length, float flankAngle, Vec3 color, int hand = 1, int segmentsPerTurn = 32)
    {
        int firstSurface = g.SurfacePoints.Count;
        var a = axis.Normalized();
        var u = Across(a, reference);
        var v = Vec3.Cross(a, u);
        float pitch = lead / starts, tan = MathF.Tan(flankAngle);
        float root = pitchRadius - dedendum, crest = pitchRadius + addendum;
        float rootHalf = Math.Min(pitch * .48f, pitch / 4 + dedendum * tan), crestHalf = Math.Max(pitch * .04f, pitch / 4 - addendum * tan);
        g.Material = SurfaceMaterial.MachinedMetal;
        g.Tube(centre - a * (length / 2), centre + a * (length / 2), root * 1.002f, color);
        (float R, float Z)[] profile = [(root, -rootHalf), (crest, -crestHalf), (crest, crestHalf), (root, rootHalf)];
        float turns = length / lead + 1;
        int n = Math.Max(8, (int)MathF.Ceiling(turns * segmentsPerTurn));
        // start s is the same helix turned by s·2π/starts about the axis
        Vec3 P(float t, float turn, int k) => centre + (u * MathF.Cos(t + turn) + v * MathF.Sin(t + turn)) * profile[k].R + a * (hand * lead * t / MathF.Tau + profile[k].Z);
        float limit = length / 2 - rootHalf;
        for (int s = 0; s < starts; s++)
        {
            float turn = s * MathF.Tau / starts, t0 = -MathF.PI * turns;
            for (int i = 0; i < n; i++)
            {
                float ta = t0 + i * MathF.Tau * turns / n, tb = t0 + (i + 1) * MathF.Tau * turns / n;
                // keep whole segments inside the length of the screw
                float za = hand * lead * ta / MathF.Tau, zb = hand * lead * tb / MathF.Tau;
                if (Math.Abs(za) > limit || Math.Abs(zb) > limit) continue;
                for (int k = 0; k < 3; k++)
                {
                    if (hand > 0) g.Quad(P(ta, turn, k), P(ta, turn, k + 1), P(tb, turn, k + 1), P(tb, turn, k), color);
                    else g.Quad(P(ta, turn, k), P(tb, turn, k), P(tb, turn, k + 1), P(ta, turn, k + 1), color);
                }
            }
        }
            SurfaceFrame(g, firstSurface, centre, a, u);
    }

    /// <summary>Ring gear with internal teeth: the involute outline of an external gear of the same pitch
    /// circle turned inside out about it (r → 2r₀ − r), so tooth k points inwards at angle k·2π/z of the
    /// reference; a plain rim out to the outer radius.</summary>
    public static void InternalGear(DioramaGeometry g, Vec3 centre, Vec3 axis, Vec3 reference, float pitchRadius, int teeth, float width, float outerRadius, Vec3 color, float phase = 0)
    {
        int firstSurface = g.SurfacePoints.Count;
        var a = axis.Normalized();
        var u = Across(a, reference);
        var v = Vec3.Cross(a, u);
        var outline = Outline(pitchRadius, teeth, 4);
        float m = 2 * pitchRadius / teeth, rootCircle = pitchRadius + 1.3f * m;
        Vec3 P(float r, float t, float z) => centre + (u * MathF.Cos(t + phase) + v * MathF.Sin(t + phase)) * r + a * z;
        g.Material = SurfaceMaterial.MachinedMetal;
        float h = width / 2;
        for (int i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var q = outline[(i + 1) % outline.Count];
            float rp = 2 * pitchRadius - p.R, rq = 2 * pitchRadius - q.R;
            g.Quad(P(rp, p.T, h), P(rq, q.T, h), P(rq, q.T, -h), P(rp, p.T, -h), color);
            g.Quad(P(rp, p.T, h), P(rootCircle, p.T, h), P(rootCircle, q.T, h), P(rq, q.T, h), color);
            g.Quad(P(rq, q.T, -h), P(rootCircle, q.T, -h), P(rootCircle, p.T, -h), P(rp, p.T, -h), color);
        }

        MachineGeometry.Annulus(g, centre, a, rootCircle - .001f, outerRadius, width, 0, MathF.Tau, color, 96);
            SurfaceFrame(g, firstSurface, centre, a, u);
    }

    /// <summary>External involute teeth on a band down to the inner radius - the outside of a ring gear
    /// that a pinion drives. Tooth k sits at angle k·2π/z of the reference.</summary>
    public static void ToothBand(DioramaGeometry g, Vec3 centre, Vec3 axis, Vec3 reference, float pitchRadius, int teeth, float width, float innerRadius, Vec3 color, float phase = 0)
    {
        int firstSurface = g.SurfacePoints.Count;
        var a = axis.Normalized();
        var u = Across(a, reference);
        var v = Vec3.Cross(a, u);
        var outline = Outline(pitchRadius, teeth, 4);
        Vec3 P(float r, float t, float z) => centre + (u * MathF.Cos(t + phase) + v * MathF.Sin(t + phase)) * r + a * z;
        g.Material = SurfaceMaterial.MachinedMetal;
        float h = width / 2;
        for (int i = 0; i < outline.Count; i++)
        {
            var p = outline[i];
            var q = outline[(i + 1) % outline.Count];
            g.Quad(P(p.R, p.T, -h), P(q.R, q.T, -h), P(q.R, q.T, h), P(p.R, p.T, h), color);
            g.Quad(P(innerRadius, p.T, h), P(p.R, p.T, h), P(q.R, q.T, h), P(innerRadius, q.T, h), color);
            g.Quad(P(innerRadius, q.T, -h), P(q.R, q.T, -h), P(p.R, p.T, -h), P(innerRadius, p.T, -h), color);
        }
            SurfaceFrame(g, firstSurface, centre, a, u);
    }

    /// <summary>Roller-chain sprocket on its body frame: z seats of the roller radius on the pitch circle
    /// R = p / (2 sin(π/z)), seat 0 on the reference (as <c>RigidBodies.Sprocket</c> numbers them), a hub and
    /// six lightening holes.</summary>
    public static void Sprocket(DioramaGeometry g, Vec3 centre, Vec3 axis, Vec3 reference, int teeth, float pitch, float rollerRadius, float width, Vec3 color)
    {
        int firstSurface = g.SurfacePoints.Count;
        float radius = pitch / (2 * MathF.Sin(MathF.PI / teeth));
        float seat = MathF.Asin(Math.Clamp(rollerRadius * 1.05f / radius, 0, .9f)), half = MathF.PI / teeth;
        var outline = new List<(float R, float T)>(teeth * 8);
        for (int k = 0; k < teeth; k++)
        {
            float t = k * 2 * half;
            for (int j = -2; j <= 2; j++)
            {
                // the seat: an arc of the roller's circle sunk into the pitch circle
                float f = j / 2f * seat;
                outline.Add((radius - rollerRadius * 1.05f * MathF.Cos(f / seat * MathF.PI / 2.4f), t + f));
            }

            outline.Add((radius + pitch * .12f, t + seat + (half - seat) * .45f));
            outline.Add((radius + pitch * .3f, t + half));
            outline.Add((radius + pitch * .12f, t + 2 * half - seat - (half - seat) * .45f));
        }

        g.Material = SurfaceMaterial.MachinedMetal;
        Profile(g, centre, axis, reference, outline, width, color);
        var a = axis.Normalized();
        var u = Across(a, reference);
        var v = Vec3.Cross(a, u);
        MachineGeometry.Annulus(g, centre, a, radius * .12f, radius * .3f, width * 1.6f, 0, MathF.Tau, color * .8f, 32);
        g.Material = SurfaceMaterial.Rubber;
        float ring = (radius * .3f + radius - rollerRadius * 1.6f) / 2;
        for (int k = 0; k < 6 && ring > radius * .35f; k++)
        {
            float t = k * MathF.Tau / 6 + MathF.PI / 6;
            var hole = centre + (u * MathF.Cos(t) + v * MathF.Sin(t)) * ring;
            g.Tube(hole - a * (width * .52f), hole + a * (width * .52f), (radius - rollerRadius * 1.6f - radius * .3f) * .32f, new(.03f, .035f, .04f));
        }
            SurfaceFrame(g, firstSurface, centre, a, u);
    }

    /// <summary>Roller chain through the given pin centres, in order: a roller on every pin and inner and
    /// outer side plates alternating along the chain, the plates of width `pitch`·0.62 either side.</summary>
    public static void Chain(DioramaGeometry g, IReadOnlyList<Vec3> pins, Vec3 axis, float pitch, float rollerRadius, float width, Vec3 color, bool closed)
    {
        var a = axis.Normalized();
        g.Material = SurfaceMaterial.MachinedMetal;
        int links = closed ? pins.Count : pins.Count - 1;
        for (int i = 0; i < links; i++)
        {
            var p = pins[i];
            var q = pins[(i + 1) % pins.Count];
            float offset = i % 2 == 0 ? width * .5f : width * .5f + .012f;
            foreach (float side in new[] { -1f, 1f })
                Link(g, p + a * (side * offset), q + a * (side * offset), a, pitch * .62f, .012f, i % 2 == 0 ? color : color * .82f);
        }

        g.Material = SurfaceMaterial.MachinedMetal;
        foreach (var p in pins)
            g.Tube(p - a * (width * .5f), p + a * (width * .5f), rollerRadius, color * .9f);
    }

    /// <summary>A plain shaft with a key-way stripe that shows its turn.</summary>
    public static void Shaft(DioramaGeometry g, Vec3 from, Vec3 to, float radius, Vec3 reference)
    {
        int firstSurface = g.SurfacePoints.Count;
        g.Material = SurfaceMaterial.MachinedMetal;
        g.Tube(from, to, radius, Steel);
        var axis = (to - from).Normalized();
        var across = Across(axis, reference);
        g.Material = SurfaceMaterial.Rubber;
        g.Tube(from + across * (radius * .93f) + axis * .03f, to + across * (radius * .93f) - axis * .03f, radius * .16f, DarkSteel);
            SurfaceFrame(g, firstSurface, from, axis, across);
    }

    /// <summary>Pillow-block bearing for a horizontal shaft at height of the centre, standing on floor.</summary>
    public static void PillowBlock(DioramaGeometry g, Vec3 centre, Vec3 axis, float shaftRadius, float floor, Vec3 paint)
    {
        var a = axis.Normalized();
        var side = Vec3.Cross(Vec3.UnitY, a).Normalized();
        float body = shaftRadius * 2.4f + .05f;
        g.Material = SurfaceMaterial.Enamel;
        g.Box(new Vec3(centre.X, floor + .03f, centre.Z), new Vec3(body * 1.6f, .03f, body * 1.6f) * 1, paint, side, Vec3.UnitY, a);
        g.Box(new Vec3(centre.X, (centre.Y + floor) / 2, centre.Z), new Vec3(body * .7f, (centre.Y - floor) / 2, body * .55f), paint, side, Vec3.UnitY, a);
        MachineGeometry.Annulus(g, centre, a, shaftRadius * 1.05f, body, body * 1.1f, 0, MathF.Tau, paint, 40);
        g.Material = SurfaceMaterial.MachinedMetal;
        MachineGeometry.Annulus(g, centre, a, shaftRadius * 1.02f, shaftRadius * 1.5f, body * 1.16f, 0, MathF.Tau, Steel, 32);
        foreach (float s in new[] { -1f, 1f })
            g.Tube(new Vec3(centre.X, floor + .06f, centre.Z) + side * (s * body * 1.2f), new Vec3(centre.X, floor + .1f, centre.Z) + side * (s * body * 1.2f), .03f, Steel);
    }

    /// <summary>Electric motor: finned housing, end shields, feet and a terminal box; the shaft leaves along axis.</summary>
    public static void Motor(DioramaGeometry g, Vec3 face, Vec3 axis, float radius, float length, float floor, Vec3 paint)
    {
        var a = axis.Normalized();
        var back = face - a * length;
        g.Material = SurfaceMaterial.Enamel;
        g.Tube(back, face, radius, paint);
        for (int k = 1; k < 9; k++)
            MachineGeometry.Annulus(g, back + a * (length * k / 9f), a, radius * .98f, radius * 1.1f, length * .03f, 0, MathF.Tau, paint, 40);
        g.Material = SurfaceMaterial.MachinedMetal;
        MachineGeometry.Annulus(g, face, a, radius * .25f, radius * 1.02f, .04f, 0, MathF.Tau, Steel, 40);
        MachineGeometry.Annulus(g, back, a, 0.001f, radius * 1.02f, .05f, 0, MathF.Tau, Steel, 40);
        var up = Across(a, Vec3.UnitY);
        var side = Vec3.Cross(up, a).Normalized();
        var mid = (face + back) / 2;
        g.Material = SurfaceMaterial.Enamel;
        if(MathF.Abs(a.Y)<.7f)
            g.Box(new Vec3(mid.X, (floor + mid.Y - radius) / 2 + .02f, mid.Z), new Vec3(radius * .8f, Math.Max(.02f, (mid.Y - radius - floor) / 2 + .03f), length * .38f), paint * .7f, side, up, a);
        else
            MachineGeometry.Annulus(g,face-a*.03f,a,radius*.25f,radius*1.25f,.06f,0,MathF.Tau,paint,40);
        g.Box(mid + up * (radius + .07f), new Vec3(radius * .35f, .08f, length * .2f), paint * .85f, side, up, a);
        for(int k=0;k<4;k++)
        {
            float t=(k+.5f)*MathF.Tau/4;
            Fastener(g,face+(side*MathF.Cos(t)+up*MathF.Sin(t))*(radius*.81f)+a*.023f,a,radius*.075f);
        }
    }

    /// <summary>Spoked pulley or flywheel rim on its body frame.</summary>
    public static void Pulley(DioramaGeometry g, Vec3 centre, Vec3 axis, Vec3 reference, float radius, float width, Vec3 color, int spokes = 5)
    {
        int firstSurface = g.SurfacePoints.Count;
        var a = axis.Normalized();
        var u = Across(a, reference);
        var v = Vec3.Cross(a, u);
        g.Material = SurfaceMaterial.MachinedMetal;
        MachineGeometry.Annulus(g, centre, a, radius * .86f, radius, width, 0, MathF.Tau, color, 72);
        foreach (float s in new[] { -1f, 1f })
            MachineGeometry.Annulus(g, centre + a * (s * width * .5f), a, radius * .97f, radius * 1.06f, width * .1f, 0, MathF.Tau, color, 72);
        MachineGeometry.Annulus(g, centre, a, .03f, radius * .2f, width * 1.2f, 0, MathF.Tau, color, 32);
        for (int k = 0; k < spokes; k++)
        {
            float t = k * MathF.Tau / spokes;
            var d = u * MathF.Cos(t) + v * MathF.Sin(t);
            g.Tube(centre + d * (radius * .18f), centre + d * (radius * .87f), radius * .055f, color);
        }

        // one painted spoke marks the turn
        g.Material = SurfaceMaterial.Enamel;
        g.Tube(centre + u * (radius * .2f) + a * (width * .02f), centre + u * (radius * .85f) + a * (width * .02f), radius * .062f, new(.85f, .22f, .16f));
            SurfaceFrame(g, firstSurface, centre, a, u);
    }

    /// <summary>Flat link between two joints with rounded ends; normal is the axis of its joints.</summary>
    public static void Link(DioramaGeometry g, Vec3 from, Vec3 to, Vec3 normal, float width, float thickness, Vec3 color) =>
        Link(g, from, to, normal, width, thickness, color, SurfaceMaterial.Enamel);

    /// <summary>Opt-in material for the same link geometry; the original painted overload is retained.</summary>
    public static void Link(DioramaGeometry g, Vec3 from, Vec3 to, Vec3 normal, float width, float thickness, Vec3 color, SurfaceMaterial material)
    {
        var d = to - from;
        float length = d.Length();
        if (length < 1e-4f) return;
        var x = d / length;
        var n = normal.Normalized();
        var y = Vec3.Cross(n, x).Normalized();
        g.Material = material;
        g.Box((from + to) / 2, new Vec3(length / 2, width / 2, thickness / 2), color, x, y, n);
        foreach (var end in new[] { from, to })
        {
            g.Tube(end - n * (thickness / 2), end + n * (thickness / 2), width / 2, color);
            g.Material = SurfaceMaterial.MachinedMetal;
            g.Tube(end - n * (thickness * .9f), end + n * (thickness * .9f), width * .2f, Steel);
            g.Material = material;
        }
    }

    /// <summary>Base plate with chamfer strip and four bolts.</summary>
    public static void Plate(DioramaGeometry g, Vec3 centre, Vec3 half, Vec3 paint)
    {
        g.Material = SurfaceMaterial.CastMetal;
        g.Box(centre, half, paint);
        g.Box(centre - new Vec3(0, half.Y * .6f, 0), new Vec3(half.X + .015f, half.Y * .45f, half.Z + .015f), paint * .7f);
        g.Material = SurfaceMaterial.MachinedMetal;
        foreach (float x in new[] { -1f, 1f })
            foreach (float z in new[] { -1f, 1f })
                Fastener(g, centre + new Vec3(x * (half.X - .1f), half.Y, z * (half.Z - .1f)), Vec3.UnitY, .035f);
    }

    /// <summary>Disc brake on a shaft: disc on the body frame and a fixed caliper at the top.</summary>
    public static void Brake(DioramaGeometry g, Vec3 centre, Vec3 axis, Vec3 reference, float radius, float load, Vec3 paint)
    {
        var a = axis.Normalized();
        var u = Across(a, reference);
        g.Material = SurfaceMaterial.MachinedMetal;
        MachineGeometry.Annulus(g, centre, a, radius * .3f, radius, .04f, 0, MathF.Tau, Steel, 56);
        g.Material = SurfaceMaterial.Rubber;
        for (int k = 0; k < 8; k++)
        {
            float t = k * MathF.Tau / 8;
            var d = u * MathF.Cos(t) + Vec3.Cross(a, u) * MathF.Sin(t);
            g.Tube(centre + d * (radius * .65f) - a * .025f, centre + d * (radius * .65f) + a * .025f, radius * .06f, DarkSteel);
        }

        var top = centre + Vec3.UnitY * (radius * .82f);
        var side = Vec3.Cross(a, Vec3.UnitY).Normalized();
        g.Material = SurfaceMaterial.Enamel;
        float clamp = Math.Clamp(load, 0, 1);
        foreach (float s in new[] { -1f, 1f })
            g.Box(top + a * (s * (.05f + .02f * (1 - clamp))), new Vec3(radius * .14f, radius * .2f, .025f), paint, side, Vec3.UnitY, a);
        g.Box(top + Vec3.UnitY * (radius * .22f), new Vec3(radius * .16f, radius * .05f, .1f), paint * .8f, side, Vec3.UnitY, a);
    }
    private static void SurfaceFrame(DioramaGeometry g, int first, Vec3 centre, Vec3 axis, Vec3 reference)
    {
        var a=axis.Normalized();var u=Across(a,reference);var v=Vec3.Cross(a,u);
        for(int i=first;i<g.SurfacePoints.Count;i++)
        {
            var vertex=g.SurfacePoints[i];var p=vertex.Position-centre;
            g.SurfacePoints[i]=vertex with {Local=new(Vec3.Dot(p,u),Vec3.Dot(p,v),Vec3.Dot(p,a))};
        }
    }

    /// <summary>A recessed hex head and washer, used on fixed mounting plates.</summary>
    public static void Fastener(DioramaGeometry g, Vec3 at, Vec3 axis, float radius)
    {
        g.Material=SurfaceMaterial.MachinedMetal;
        MachineGeometry.Annulus(g,at+axis*(radius*.10f),axis,radius*.38f,radius*1.3f,radius*.20f,0,MathF.Tau,Steel*.78f,24);
        MachineGeometry.Annulus(g,at+axis*(radius*.45f),axis,radius*.38f,radius,radius*.60f,0,MathF.Tau,Steel,6);
    }

}
