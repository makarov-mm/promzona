using MakarovPhysics.UI;
namespace Promzona.Game;

internal static class CityMinimap
{
    public static void Draw(Draw2D d, CityModel model, float width, float height, bool ru = false, bool ukrainian=false)
    {
        float size = Math.Clamp(height * .26f, 150, 245), x = 22, y = height - size - 46;
        float span = 78 + (float)Math.Abs(model.Speed) * 1.1f, scale = size / (span * 2);
        float X(double a) => x + size * .5f + (float)CityMapProjection.NorthUp(new(a-model.X,0)).X * scale;
        float Y(double a) => y + size * .5f - (float)(a - model.Z) * scale;
        d.Rect(x - 4, y - 4, size + 8, size + 31, new(.025f, .035f, .035f, .92f));
        d.SetClip(new(x, y, size, size));
        d.Rect(x, y, size, size, new(.075f, .20f, .25f, .94f));
        float edge = (float)CityTraffic.LandEdge;
        d.Rect(X(edge), Y(edge), edge * 2 * scale, edge * 2 * scale, new(.13f, .20f, .15f, .94f));
        if(CityLayout.Rivers)for(int column=0;column<CityTraffic.Nodes;column++)if(CityLayout.CanalColumn(column))d.Rect(X(CityTraffic.Coordinate(column)+29),Y(edge),22*scale,edge*2*scale,new(.075f,.23f,.29f));
        for (int i = 0; i < CityTraffic.Nodes; i++)
        for (int j = 0; j < CityTraffic.Nodes; j++)
        {
            double cx = CityTraffic.Coordinate(i), cz = CityTraffic.Coordinate(j);
            if (Math.Abs(cx - model.X) > span + 40 || Math.Abs(cz - model.Z) > span + 40) continue;
            for (int dir = 0; dir < 2; dir++)
                if (CityTraffic.HasRoad(i, j, dir))
                {
                    var f = CityTraffic.Forward(dir);
                    d.Line(X(cx), Y(cz), X(cx + f.X * 36), Y(cz + f.Z * 36), new(.39f, .40f, .37f), (float)(CityTraffic.RoadHalf*2) * scale);
                }
        }

        // The industrial district (29.10.2026) is shaded rust-brown, its spur drawn as a dark line with ties.
        var layout = CityLayout.Current;
        if (layout.HasIndustry)
        {
            for (int bi = CityIndustrial.ZoneWest; bi < CityIndustrial.ZoneEast; bi++)
            for (int bj = CityIndustrial.ZoneRow; bj < CityIndustrial.ZoneRow + CityIndustrial.ZoneRows; bj++)
            {
                double bx = CityTraffic.Coordinate(bi) + 18, bz = CityTraffic.Coordinate(bj) + 18;
                if (Math.Abs(bx - model.X) > span + 30 || Math.Abs(bz - model.Z) > span + 30) continue;
                d.Rect(X(bx + 18), Y(bz + 18), 36 * scale, 36 * scale, new(.30f, .22f, .16f, .55f));
            }
            double rz = CityIndustrial.RailZ;
            if (Math.Abs(rz - model.Z) < span + 10)
            {
                double ra = Math.Max(CityIndustrial.RailX0, model.X - span - 10), rb = Math.Min(CityIndustrial.RailX1, model.X + span + 10);
                if (rb > ra)
                {
                    d.Line(X(ra), Y(rz), X(rb), Y(rz), new(.16f, .14f, .12f), Math.Max(2, 3 * scale));
                    for (double a = Math.Ceiling(ra / 6) * 6; a < rb; a += 6) d.Line(X(a), Y(rz - 1.6), X(a), Y(rz + 1.6), new(.55f, .5f, .4f), 1);
                }
            }
        }

        foreach (var b in CityModel.Blocks)
        {
            if (Math.Abs(b.X - model.X) > span + 12 || Math.Abs(b.Z - model.Z) > span + 12) continue;
            d.Rect(X(b.X + b.HalfX), Y(b.Z + b.HalfZ), (float)b.HalfX * 2 * scale, (float)b.HalfZ * 2 * scale,
                new(.72f, .66f, .52f));
        }

        // A separate stroke shows connectivity even where the covered road passes
        // beneath buildings. Tunnel dashes distinguish it from the upper deck.
        foreach(var road in CityRoadLevels.All){
            if(Math.Abs(road.X-model.X)>span+145||Math.Abs(road.Z-model.Z)>span+30)continue;
            for(double a=road.X-144;a<road.X+144;a+=3){
                if(road.Tunnel&&road.Roof(a)&&((int)(a-road.X+144)/3)%2==0)continue;
                double b=Math.Min(road.X+144,a+3);
                d.Line(X(a),Y(road.CentreZ(a)),X(b),Y(road.CentreZ(b)),road.Tunnel?new(.38f,.65f,.69f):new(.78f,.65f,.40f),Math.Max(2,7*scale));
            }
        }
        // The second sample of a 4 m stroke is taken only for strokes inside the map
        // span: the whole network (several km) was sampled twice per frame before.
        foreach(var route in CityTransit.Routes)for(double s=0;s<route.Length;s+=4){var a=route.Sample(s).Point;if(Math.Abs(a.X-model.X)>=span+4||Math.Abs(a.Z-model.Z)>=span+4)continue;var b=route.Sample(s+4).Point;d.Line(X(a.X),Y(a.Z),X(b.X),Y(b.Z),route.Kind==9?new(.94f,.43f,.17f):new(.2f,.75f,.9f),2);}
        foreach (var p in model.Traffic.Pedestrians)
            if (Math.Abs(p.Position.X - model.X) < span && Math.Abs(p.Position.Z - model.Z) < span)
                d.Circle(X(p.Position.X), Y(p.Position.Z), 1, new(.72f, .86f, .67f));
        // Jobs: hailers and injured survivors as ringed dots, the destination as
        // a pulsing marker or an arrow at the rim when it is beyond the span.
        foreach (var p in model.Traffic.Pedestrians)
            if ((p.Hailing || p.Injured > 0) && !p.Dead && Math.Abs(p.Position.X - model.X) < span && Math.Abs(p.Position.Z - model.Z) < span)
            {
                float px = X(p.Position.X), py = Y(p.Position.Z);
                d.Circle(px, py, 3.5f, p.Hailing ? new(1, .8f, .2f) : new(1, .35f, .3f));
                d.Circle(px, py, 1.5f, new(.1f, .1f, .1f));
            }
        if (model.Traffic.JobKind != JobKind.None && !model.Traffic.PeaceEscape)
        {
            var target = model.Traffic.JobStage == 0 && model.Traffic.JobPassenger is { } passenger ? passenger.Position : model.Traffic.JobTarget;
            float tx = X(target.X), ty = Y(target.Z), pulse = .6f + .4f * MathF.Sin((float)model.Traffic.Time * 5);
            if (Math.Abs(target.X - model.X) < span && Math.Abs(target.Z - model.Z) < span)
            {
                d.Circle(tx, ty, 6 * pulse + 3, new(1, .9f, .3f, .35f));
                d.Circle(tx, ty, 3, new(1, .95f, .4f));
            }
            else
            {
                float cx = x + size * .5f, cy = y + size * .5f, targetDx = tx - cx, targetDy = ty - cy, len = MathF.Max(1e-3f, MathF.Sqrt(targetDx * targetDx + targetDy * targetDy));
                targetDx /= len; targetDy /= len;
                float r = size * .5f - 10, ax = cx + targetDx * r, ay = cy + targetDy * r;
                d.Line(ax - targetDx * 12, ay - targetDy * 12, ax, ay, new(1, .9f, .3f), 3);
                d.Circle(ax, ay, 3.5f, new(1, .95f, .4f));
            }
        }
        foreach (var c in model.Traffic.Cars)
            if (!model.Traffic.PlayerControls(c) && Math.Abs(c.Position.X - model.X) < span &&
                Math.Abs(c.Position.Z - model.Z) < span)
                d.Circle(X(c.Position.X), Y(c.Position.Z), c.Kind is 5 or 9 or 10 ? 3 : 2,
                    c.Kind==9?new(.95f,.4f,.12f):c.Kind==10?new(.15f,.8f,.95f):c.PoliceVehicle||c.AirPatrol ? new(.25f, .55f, 1) : c.Kind == 4 ? new(1, .4f, .3f) : new(.85f, .85f, .75f));
        foreach (var p in model.Traffic.Pickups)
            if (Math.Abs(p.Position.X - model.X) < span && Math.Abs(p.Position.Z - model.Z) < span)
            {
                float px = X(p.Position.X), py = Y(p.Position.Z);
                if(p.Kind==CityPickupKind.EnergyDrink){d.Rect(px-2,py-4,4,8,new(.6f,1,.2f));}
                else if (p.Kind == CityPickupKind.Pills)
                {
                    d.Rect(px - 3, py - 1, 6, 2, new(.3f, 1, .65f));
                    d.Rect(px - 1, py - 3, 2, 6, new(.3f, 1, .65f));
                }
                else
                {
                    d.Rect(px - 3, py - 1, 6, 2, new(1, .75f, .22f));
                    d.Rect(px - 2, py + 1, 2, 3, new(1, .75f, .22f));
                }
            }

        foreach(var shop in model.Traffic.FoodSites)if(Math.Abs(shop.Position.X-model.X)<span&&Math.Abs(shop.Position.Z-model.Z)<span){d.Circle(X(shop.Position.X),Y(shop.Position.Z),4,new(.95f,.6f,.15f));}
        // Garages as a grey square with a dark door, markets as a striped square.
        foreach (var s in model.Traffic.Garages)
            if (Math.Abs(s.Position.X - model.X) < span && Math.Abs(s.Position.Z - model.Z) < span)
            {
                float gx = X(s.Position.X), gy = Y(s.Position.Z);
                d.Rect(gx - 4, gy - 4, 8, 8, new(.62f, .62f, .58f));
                d.Rect(gx - 2, gy + 1, 4, 3, new(.2f, .22f, .22f));
            }
        foreach (var s in model.Traffic.Markets)
            if (s.Id % 3 == 2 && Math.Abs(s.Position.X - model.X) < span && Math.Abs(s.Position.Z - model.Z) < span)
            {
                float mx = X(s.Position.X), my = Y(s.Position.Z);
                d.Rect(mx - 5, my - 4, 10, 8, new(.9f, .3f, .25f));
                d.Rect(mx - 5, my - 1, 10, 2, new(.95f, .95f, .9f));
            }
        foreach (var clinic in CityTraffic.ClinicBlocks)
        {
            float cx = X(CityTraffic.Coordinate(clinic.I) + 18), cy = Y(CityTraffic.Coordinate(clinic.J) + 18);
            d.Rect(cx - 4, cy - 1, 8, 2, new(.5f, 1, .65f));
            d.Rect(cx - 1, cy - 4, 2, 8, new(.5f, 1, .65f));
        }
        {
            float sx = X(CityTraffic.Coordinate(CityTraffic.StationBlock.I) + 18), sy = Y(CityTraffic.Coordinate(CityTraffic.StationBlock.J) + 18);
            d.Rect(sx - 4, sy - 4, 8, 8, new(.25f, .45f, 1));
            d.Rect(sx - 4, sy - 1, 8, 2, new(.95f, .95f, 1));
        }

        float hx = X(model.X),
            hy = Y(model.Z),
            dx = -9 * (float)Math.Sin(model.Heading),
            dy = -9 * (float)Math.Cos(model.Heading);
        d.Circle(hx, hy, 5, new(1, .66f, .15f));
        d.Line(hx, hy, hx + dx, hy + dy, new(1, 1, .8f), 3);
        d.SetClip(null);
        d.TextScaled(x + 6, y + 5, "N", new(.94f, .94f, .8f), .8f);
        d.TextScaled(x + 4, y + size + 7,
            $"{Math.Abs(model.Speed) * 3.6:0} {(ukrainian?"км/год":ru ? "км/ч" : "km/h")}   {(int)model.Hour:00}:{(int)(model.Hour % 1 * 60):00}",
            new(.93f, .91f, .77f), .8f);
    }
}
