using MakarovPhysics.Experiments;
using MakarovPhysics.Renderer;

namespace Promzona.Game;

internal static class CityArchitecture
{
    private static int Poster(int id) => id < 10 ? id : 31 + id - 10;
    private static readonly Dictionary<string, List<(int X, int Y, int W)>> _letters = [];

    private static List<(int X, int Y, int W)> LetterRuns(string text)
    {
        if (_letters.TryGetValue(text, out List<(int X, int Y, int W)>? result)) return result;

        using var bmp = new Bitmap(180, 24);
        using (var graphics = Graphics.FromImage(bmp))
        {
            graphics.Clear(Color.Black);
            using var font = new Font("Arial", 15, FontStyle.Bold, GraphicsUnit.Pixel);
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            graphics.DrawString(text, font, Brushes.White, 0, -1);
        }

        result = [];

        for (int y = 0; y < 22; y++)
        {
            for (int x = 0; x < 180; x++)
            {
                if (bmp.GetPixel(x, y).R < 128) continue;
                int start = x;
                while (x + 1 < 180 && bmp.GetPixel(x + 1, y).R >= 128) x++;
                result.Add((start, y, x - start + 1));
            }
        }

        _letters[text] = result;
        return result;
    }

    internal static void Picture(DioramaGeometry g, Vec3 a, Vec3 b, Vec3 c, Vec3 d, int tile)
    {
        if (tile < 10 || tile is >= 31 and <= 38)
        {
            Vec3 r = b - a, vertical = d - a;
            float w = r.Length(), h = vertical.Length(), aspect = tile >=35?2f/3:tile < 10 ? 2 : 1.5f;
            float nw = Math.Min(w, h * aspect), nh = nw / aspect;
            Vec3 centre = (a + b + c + d) * .25f;
            r = r.Normalized() * nw * .5f;
            vertical = vertical.Normalized() * nh * .5f;
            a = centre - r - vertical;
            b = centre + r - vertical;
            c = centre + r + vertical;
            d = centre - r + vertical;
        }

        g.Material = (SurfaceMaterial)(100 + tile);
        int start = g.SurfacePoints.Count;

        if (Math.Abs(a.Y - b.Y) < .0001f && Math.Abs(a.Y - c.Y) < .0001f)
        {
            g.Quad(d, c, b, a, new Vec3(1, 1, 1));
        }
        else
        {
            g.Quad(a, b, c, d, new Vec3(1, 1, 1));
        }

        Vec3 right = b - a, down = d - a;

        for (int k = start; k < g.SurfacePoints.Count; k++)
        {
            SurfaceVertex v = g.SurfacePoints[k];
            Vec3 offset = v.Position - a;

            g.SurfacePoints[k] = v with
            {
                Local = new Vec3(
                    Vec3.Dot(offset, right) / Vec3.Dot(right, right),
                    Vec3.Dot(offset, down) / Vec3.Dot(down, down), 0)
            };
        }
    }

    private static void Sign(DioramaGeometry g, Vec3 c, string text, float width, bool fascia=false)
    {
        g.Material = SurfaceMaterial.Enamel;
        g.Box(c, new(width * .5f, fascia?.28f:.55f, .10f), new(.14f, .25f, .25f));
        int tile = text == "ПРОДУКТЫ" ? 5 : text == "КОВРЫ" ? 6 : 4;
        if (!fascia && ((int)c.X + (int)c.Z) % 3 != 0)
        {
            Picture(g, c + new Vec3(-width * .49f, .53f, .105f), c + new Vec3(width * .49f, .53f, .105f),
                c + new Vec3(width * .49f, -.53f, .105f), c + new Vec3(-width * .49f, -.53f, .105f), tile);
            return;
        }

        var runs = LetterRuns(text);
        int xmax = runs.Count == 0 ? 1 : runs.Max(r => r.X + r.W);
        float scale = Math.Min((width - .2f) / xmax, fascia?.021f:.045f);

        foreach ((int X, int Y, int W) r in runs)
        {
            float x = c.X - xmax*scale*.5f + r.X * scale, y = c.Y + (fascia?.20f:.37f) - r.Y * scale;
            bool neon=((int)Math.Abs(c.X*3+c.Z))%11==0;
            g.Material=neon?(SurfaceMaterial)(r.X>=xmax*.32&&r.X<xmax*.46?351:350):SurfaceMaterial.Enamel;

            g.Box(
                new Vec3(x + r.W * scale * .5f, y - scale * .5f, c.Z + .14f),
                new Vec3(r.W * scale * .5f, scale * .5f, .045f),
                neon?new Vec3(.72f,.47f,.22f):new Vec3(.78f,.76f,.62f));
        }
    }

    public static void Text(DioramaGeometry g, Vec3 c, string text, float width, Vec3 color, Vec3? horizontal = null,Vec3? vertical=null)
    {
        var runs = LetterRuns(text);
        int xmax = runs.Count == 0 ? 1 : runs.Max(r => r.X + r.W);
        float scale = width / xmax;
        Vec3 right = horizontal ?? Vec3.UnitX; Vec3 up=vertical??Vec3.UnitY;
        g.Material = SurfaceMaterial.Enamel;

        foreach ((int X, int Y, int W) r in runs)
        {
            Vec3 centre = c + right * ((r.X + r.W * .5f) * scale - width * .5f) - up * ((r.Y + .5f) * scale);
            g.Box(centre, new Vec3(r.W * scale * .5f, scale * .5f, .012f), color, right, up, Vec3.Cross(right, up));
        }
    }

    private static void Top(DioramaGeometry g, float x, float y, float z, float hx, float hz, Vec3 color) => CityTerrainGeometry.Top(g, x, z, hx, hz, y, color);

    private static void DrawCivicSign(DioramaGeometry g, CityModel.Obstacle building, bool clinic)
    {
        float x = (float)building.X, front = (float)(building.Z + building.HalfZ);
        g.Material = SurfaceMaterial.Enamel;
        g.Box(new(x, 4, front + .13f), new(4, .65f, .08f),
            clinic ? new(.12f, .35f, .32f) : new(.16f, .22f, .38f));
        Text(g, new(x, 4.3f, front + .23f), clinic ? "ПОЛИКЛИНИКА" : "МИЛИЦИЯ", 7,
            clinic ? new(.80f, .94f, .82f) : new(.86f, .90f, .96f));
        g.Material = SurfaceMaterial.Emissive;
        if (clinic)
        {
            var cross = new Vec3(x - 6, 4, front + .15f);
            g.Box(cross, new(.50f, .15f, .08f), new(.15f, 1, .48f));
            g.Box(cross, new(.15f, .50f, .09f), new(.15f, 1, .48f));
        }
        else g.Box(new(x, 5.4f, front + .2f), new(.22f, .22f, .16f), new(.25f, .45f, 1));
    }

    public static void Block(DioramaGeometry g, int i, int j, int lod)
    {
        bool skyline = lod >= 3;
        lod = Math.Min(lod, 2);
        float x = (float)CityTraffic.BlockCentre(i), z = (float)CityTraffic.BlockCentre(j);
        if(CityLayout.CanalColumn(i)&&j>=-2&&j<=CityTraffic.Nodes){CityWaterVisual.Canal(g,i,j,lod);return;}
        float edge = (float)CityTraffic.LandEdge;
        float x0 = Math.Max(-edge, x - 18),
            x1 = Math.Min(edge, x + 18),
            z0 = Math.Max(-edge, z - 18),
            z1 = Math.Min(edge, z + 18);
        if (x1 <= x0 || z1 <= z0) return;
        bool coast = i < 0 || j < 0 || i >= CityTraffic.Nodes - 1 || j >= CityTraffic.Nodes - 1;
        g.Material = coast ? (SurfaceMaterial)301 : (SurfaceMaterial)300;
        if(coast)CityCoastSurface.Draw(g,x0,x1,z0,z1);
        else Top(g, (x0 + x1) * .5f, 0, (z0 + z1) * .5f, (x1 - x0) * .5f, (z1 - z0) * .5f, new Vec3(.14f,.15f,.15f));
        g.Material = SurfaceMaterial.Stone;

        if (!CityLayout.Rivers&&(x0 == -edge || x1 == edge))
        {
            float sx = x0 == -edge ? x0 : x1;
            g.Box(new(sx, -4, (z0 + z1) * .5f), new(.08f, 4, (z1 - z0) * .5f), new(.31f, .32f, .27f));
        }

        if (!CityLayout.Rivers&&(z0 == -edge || z1 == edge))
        {
            float sz = z0 == -edge ? z0 : z1;
            g.Box(new((x0 + x1) * .5f, -4, sz), new((x1 - x0) * .5f, 4, .08f), new(.31f, .32f, .27f));
        }

        if(coast&&CityLayout.Rivers&&lod<2)CityBeach.Draw(g,x,z,i,j);
        if (i < 0 || j < 0 || i >= CityTraffic.Nodes - 1 || j >= CityTraffic.Nodes - 1) return;
        if(CityRoadLevels.ReservedBlock(i,j))return;
        bool park = CityModel.ParkBlock(i, j), industrial = CityLayout.Current.Industrial(i, j);
        g.Material = SurfaceMaterial.Paving;
        CityTerrainGeometry.Pavement(g, x, z, (CityLayout.WideRoads?10.6f:13.1f), .15f, new(.38f, .39f, .36f));

        if (industrial) CityIndustrialVisual.Ground(g, x, z);
        else if (!park&&CityLayout.Current.RoadRevision>=4&&CityNeighbourhoods.At(i,j,CityLayout.Current.Seed,CityTraffic.Nodes)==CityNeighbourhood.PrivateHouses)
        {
            // Gardens sit inside the pavement border; baked paths remain walkable.
            float yard=CityLayout.WideRoads?9.5f:12;
            g.Material=SurfaceMaterial.Landscape;Top(g,x,.16f,z,yard,yard,new(.29f,.34f,.18f));
            g.Material=SurfaceMaterial.Paving;Top(g,x,.18f,z,.65f,yard,new(.42f,.40f,.33f));
            Top(g,x,.18f,z,yard,.7f,new(.42f,.40f,.33f));
        }
        else if (park)
        {
            g.Material = SurfaceMaterial.Landscape;
            Top(g, x, .16f, z, CityLayout.WideRoads?9.5f:12, CityLayout.WideRoads?9.5f:12, new(.25f, .33f, .17f));
            g.Material = SurfaceMaterial.Paving;
            Top(g, x, .18f, z, 1.1f, CityLayout.WideRoads?9.5f:12, new(.48f, .43f, .34f));
            Top(g, x, .18f, z, CityLayout.WideRoads?9.5f:12, .9f, new(.48f, .43f, .34f));

            if (lod < 2 && (i * 7 + j) % 11 == 0)
            {
                g.Material = SurfaceMaterial.CastMetal;
                g.Tube(new(x + 9, .2f, z + 9), new(x + 9, 4, z + 9), .12f, InstrumentDrawing.Steel);
                g.Box(new(x + 9, 3.1f, z + 9), new(2.2f, 1.1f, .09f), InstrumentDrawing.Dark);
                Picture(g, new(x + 6.85f, 4.15f, z + 9.10f), new(x + 11.15f, 4.15f, z + 9.10f),
                    new(x + 11.15f, 2.05f, z + 9.10f), new(x + 6.85f, 2.05f, z + 9.10f), Poster((i * 3 + j) % 13));
            }

            int count = 8; int treeStep=lod==0?1:lod==1?2:8;

            for (int t = 0; t < count; t+=treeStep)
            {
                float a=t*MathF.Tau/count+.17f*MathF.Sin(i*13+j*7+t*3.4f);
                float radius=6.5f+1.8f*(.5f+.5f*MathF.Sin(i*4.3f+j+t*8.7f));
                Tree(g,new(x+radius*MathF.Cos(a),.17f,z+radius*MathF.Sin(a)),4.1f+1.7f*(.5f+.5f*MathF.Sin(i+j*3+t*4.1f)),(i+j+t)%3,lod);
            }
        }
        else if (lod < 2 && (i + j) % 6 != 0)
        {
            Tree(g, new(x, .15f, z), 3.8f, (i + j) % 3, lod);
        }

        if (lod < 2)
        {
            g.Material = (SurfaceMaterial)302;

            for (int n = 0; n < 6; n++)
            {
                float at = 11 + n * 2.5f;
                if (at > 25) break;
                if (CityTraffic.HasRoad(i, j, 0))
                    g.Box(new(x - 18, .012f, z - 18 + at), new(.035f, .005f, .65f), new(.72f, .66f, .43f));
                if (CityTraffic.HasRoad(i, j, 1))
                    g.Box(new(x - 18 + at, .012f, z - 18), new(.65f, .005f, .035f), new(.72f, .66f, .43f));
            }

            for (int side = -1; side <= 1; side += 2)
            for (int stripe = 0; stripe < (CityLayout.WideRoads?16:10); stripe++)
            {
                float v = -(CityLayout.WideRoads?6.9f:4.15f) + stripe * .92f;
                if (CityTraffic.HasRoad(i, j, side > 0 ? 0 : 2))
                    CityCoastSurface.Mark(g,new(x - 18 + v, .02f, z - 18 + side * (CityLayout.WideRoads?8.5f:6)), new(.27f, .008f, .85f));
                if (CityTraffic.HasRoad(i, j, side > 0 ? 1 : 3))
                    CityCoastSurface.Mark(g,new(x - 18 + side * (CityLayout.WideRoads?8.5f:6), .02f, z - 18 + v), new(.85f, .008f, .27f));
            }

            foreach (var hole in CityRoadDetail.Holes(i, j))
            {
                float px = (float)hole.Position.X, pz = (float)hole.Position.Z, size = (float)hole.Size;
                Picture(g, new(px - size, .025f, pz - size), new(px + size, .025f, pz - size),
                    new(px + size, .025f, pz + size), new(px - size, .025f, pz + size), 25 + hole.Style);
            }

            if ((i + j) % 3 == 0)
                Picture(g, new(x - 18.55f, .026f, z + 7 - .55f), new(x - 17.45f, .026f, z + 7 - .55f),
                    new(x - 17.45f, .026f, z + 7 + .55f), new(x - 18.55f, .026f, z + 7 + .55f), 28 + (i % 2));

        }

        if(CityLayout.WideRoads&&lod<2){
            g.Material=(SurfaceMaterial)302;
            for(int n=0;n<4;n++)for(int side=-1;side<=1;side+=2){float t=11+n*4;
                if(CityTraffic.HasRoad(i,j,0))g.Box(new(x-18+side*3.55f,.019f,z-18+t),new(.035f,.004f,1),new(.68f,.67f,.59f));
                if(CityTraffic.HasRoad(i,j,1))g.Box(new(x-18+t,.019f,z-18+side*3.55f),new(1,.004f,.035f),new(.68f,.67f,.59f));
            }
        }
        CityAmenitiesVisual.Block(g,i,j,lod);
        CityImportedSiteVisual.Block(g,i,j,lod);
        // The industrial district draws its own halls, tanks and the spur from the same obstacle list.
        if (industrial) CityIndustrialVisual.Block(g, i, j, lod);
        else foreach (int b in CityLayout.Current.BuildingIds(i, j))
        {
            Building(g, CityModel.Blocks[b], b, skyline ? 3 : lod, CityTraffic.IsClinic(i, j) || CityTraffic.IsStation(i, j));
        }

        if (lod < 2 && (CityTraffic.IsClinic(i, j) || CityTraffic.IsStation(i, j)))
            DrawCivicSign(g, CityCivicSites.Building(i, j), CityTraffic.IsClinic(i, j));

        if (lod < 2)
            foreach (var f in CityLayout.Current.FurnitureInBlock(i,j))
            {
                if (!CityAmenities.StopClear(new(f.X,f.Z),.2) || f.Height < 4 || f.X < x - 18 || f.X >= x + 18 || f.Z < z - 18 || f.Z >= z + 18) continue;
                int lampStart = g.SurfacePoints.Count;
                g.Material = SurfaceMaterial.CastMetal;
                g.Tube(new((float)f.X, .1f, (float)f.Z), new((float)f.X, 4.2f, (float)f.Z), .07f,
                    InstrumentDrawing.Steel);
                g.Tube(new((float)f.X, 4.2f, (float)f.Z), new((float)f.X + .9f, 4.2f, (float)f.Z), .05f,
                    InstrumentDrawing.Steel);
                g.Box(new((float)f.X + .9f, 4.16f, (float)f.Z), new(.36f, .07f, .20f), InstrumentDrawing.Dark);
                CityTerrainGeometry.Rigid(g, lampStart, f.X, f.Z);
            }

        // Closed graph edges are pedestrian promenades, flush with the street.
        for (int d = 0; d < 2; d++)
            if (!CityTraffic.HasRoad(i, j, d) && !(CityLayout.Current.HasIndustry && CityIndustrial.RailEdge(i, j, d)))
            {
                var f = CityTraffic.Forward(d);
                float px = x - 18 + (float)f.X * 18, pz = z - 18 + (float)f.Z * 18;
                if(!CityWater.UrbanSite(new(px,pz),1))continue;
                g.Material = SurfaceMaterial.Paving;
                Top(g, px, .034f, pz, d == 0 ? (float)CityTraffic.RoadHalf : 9, d == 0 ? 9 : (float)CityTraffic.RoadHalf, new(.48f, .44f, .36f));
                if (lod < 2)
                    for (int side = -1; side <= 1; side += 2)
                    {
                        Vec3 at = new(px + (d == 0 ? side * 3.8f : 0), .035f, pz + (d == 1 ? side * 3.8f : 0));
                        g.Material = SurfaceMaterial.Concrete;
                        g.Box(at + new Vec3(0, .22f, 0), new(.7f, .22f, .7f), new(.43f, .42f, .35f));
                        g.Material = SurfaceMaterial.Landscape;
                        Top(g, at.X, .49f, at.Z, .62f, .62f, new(.18f, .14f, .08f));
                        if(CityWater.UrbanSite(new(at.X,at.Z),.8))Tree(g, at + new Vec3(0, .5f, 0), 3.3f, (i + j + side + 3) % 3, lod);
                    }
            }

        // Worn courtyard clothes lines and communal notice boards.
        if (lod == 0 && (i * 7 + j) % 9 == 0)
        {
            float px = x + 10, pz = z + 7;
            g.Material = SurfaceMaterial.CastMetal;
            for (int side = -1; side <= 1; side += 2)
                g.Tube(new(px + side * 2.2f, .15f, pz), new(px + side * 2.2f, 2.5f, pz), .04f, new(.23f, .32f, .27f));
            g.Tube(new(px - 2.2f, 2.35f, pz), new(px + 2.2f, 2.35f, pz), .012f, new(.3f, .3f, .25f));
            for (int n = 0; n < 4; n++)
            {
                g.Material = SurfaceMaterial.Paper;
                g.Box(new(px - 1.5f + n, 1.96f, pz), new(.32f, .38f, .008f),
                    n % 2 == 0 ? new(.65f, .63f, .51f) : new(.34f, .43f, .51f));
            }
        }
    }

    private static void Building(DioramaGeometry g, CityModel.Obstacle b, int id, int lod, bool clinic)
    {
        int start = g.SurfacePoints.Count;
        BuildingCore(g, b, id, lod, clinic);
        CityTerrainGeometry.Rigid(g, start, b.X, b.Z);
    }

    private static void BuildingCore(DioramaGeometry g, CityModel.Obstacle b, int id, int lod, bool clinic)
    {
        float x = (float)b.X, z = (float)b.Z, h = (float)b.Height, hx = (float)b.HalfX, hz = (float)b.HalfZ;
        if(!clinic&&CityLayout.Current.RoadRevision>=4){
            var kind=CityNeighbourhoods.At(new(b.X,b.Z));
            if(kind==CityNeighbourhood.Garages&&h<3.7f){CooperativeGarage(g,x,z,hx,hz,h,id,Math.Min(lod,2));return;}
            if(kind==CityNeighbourhood.PrivateHouses&&h<5.5f){Cottage(g,x,z,hx,hz,h,id,Math.Min(lod,2));return;}
        }
        if (h < 3.1f)
        {
            g.Material = (SurfaceMaterial)(218 + id % 2);
            g.Box(new(x, 1.2f, z), new(hx, 1.1f, hz), new(.19f, .36f, .34f));
            g.Material = SurfaceMaterial.BrushedAluminium;
            g.Box(new(x, 2.4f, z), new(hx + .25f, .12f, hz + .25f), new(.62f, .64f, .58f));
            if (lod < 2)
            {
                g.Material = SurfaceMaterial.ArchitecturalGlass;
                g.Box(new(x, 1.3f, z + hz + .035f), new(hx * .8f, .6f, .04f), new(.13f, .24f, .25f));
                g.Material = SurfaceMaterial.Wood;
                g.Box(new(x, .75f, z + hz + .22f), new(hx * .85f, .05f, .30f), new(.44f, .32f, .19f));
                if (lod == 0) Sign(g, new(x, 2.03f, z + hz + .12f), "ПРОДУКТЫ", hx * 1.8f,true);
            }

            return;
        }

        var district=CityNeighbourhoods.At(new(b.X,b.Z));
        int tile=district switch{
            CityNeighbourhood.Estate=>id%4==0?20:13,
            CityNeighbourhood.Courtyard=>id%3==0?15:21,
            CityNeighbourhood.Centre=>id%4==0?14:id%3==0?16:17,
            CityNeighbourhood.Industry=>id%3==0?18:21,
            _=>id%3==0?12:17};
            Vec3 tint=(id%4) switch{0=>new(.86f,.85f,.78f),1=>new(.92f,.89f,.80f),2=>new(.80f,.84f,.81f),_=>new(.93f,.92f,.85f)};
        g.Material=(SurfaceMaterial)(clinic?213:200+tile);
        g.Box(new(x, h * .5f, z), new(hx, h * .5f, hz), tint);
        g.Material = SurfaceMaterial.Concrete;
        if (lod >= 3)
        {
            // At skyline distances the parapet and roof read as one cap.
            g.Box(new(x, h + .215f, z), new(hx + .12f, .215f, hz + .12f), new(.39f, .40f, .37f));
            return;
        }
        g.Box(new(x, h + .10f, z), new(hx + .12f, .10f, hz + .12f), new(.39f, .40f, .37f));
        // Continuous parapets and base course make the building read as a constructed volume.
        g.Material=SurfaceMaterial.Concrete;
        foreach(int side in new[]{-1,1}){
            g.Box(new(x,.32f,z+side*(hz+.025f)),new(hx,.32f,.045f),new(.35f,.36f,.32f));
            g.Box(new(x+side*(hx+.025f),.32f,z),new(.045f,.32f,hz),new(.35f,.36f,.32f));
            g.Box(new(x,h+.27f,z+side*(hz-.06f)),new(hx,.16f,.09f),new(.41f,.42f,.38f));
            g.Box(new(x+side*(hx-.06f),h+.27f,z),new(.09f,.16f,hz),new(.41f,.42f,.38f));
        }
        if (lod == 2) return;
        CityCivicVisual.Building(g,b,id,lod,clinic);

        // Painted plinth, panel joints, entrance canopy and heavy metal door.
        g.Material = SurfaceMaterial.Enamel;
        g.Box(new(x, .5f, z + hz + .025f), new(hx, .5f, .03f), new(.26f, .35f, .32f));
        g.Material = SurfaceMaterial.Rubber;
        g.Box(new(x, .95f, z + hz + .07f), new(.62f, .90f, .055f), new(.13f, .20f, .21f));
        g.Material = SurfaceMaterial.Concrete;
        g.Box(new(x, 2, z + hz + .4f), new(1.05f, .09f, .6f), new(.42f, .43f, .40f));
        if (lod == 0)
        {
            g.Material = SurfaceMaterial.BrushedAluminium;
            g.Box(new(x + .43f, 1, z + hz + .14f), new(.025f, .16f, .03f), InstrumentDrawing.Steel);
            g.Material = SurfaceMaterial.Concrete;
            g.Box(new(x, .17f, z + hz + .6f), new(.9f, .17f, .65f), new(.50f, .50f, .45f));
            g.Material = SurfaceMaterial.CastMetal;
            if (id % 4 == 0)
            {
                g.Tube(new(x, h + .15f, z), new(x, h + 2.2f, z), .03f, InstrumentDrawing.Steel);

                for (int n = 0; n < 5; n++)
                {
                    g.Tube(new(x - .5f, h + 1 + n * .2f, z), new(x + .5f, h + 1 + n * .2f, z), .012f, InstrumentDrawing.Steel);
                }
            }

            // Address enamel, drainpipe and a warm entrance lamp give detail a purpose.
            g.Material=SurfaceMaterial.Enamel;
            g.Box(new(x+1.48f,1.9f,z+hz+.08f),new(.24f,.15f,.025f),new(.15f,.24f,.30f));
            Text(g,new(x+1.48f,2.0f,z+hz+.113f),(id%98+1).ToString(),.32f,new(.80f,.81f,.72f));
            g.Material=SurfaceMaterial.CastMetal;
            g.Tube(new(x-hx+.25f,.45f,z+hz+.09f),new(x-hx+.25f,h,z+hz+.09f),.048f,new(.31f,.33f,.29f));
            if (!clinic && !CityCivicSites.Office(b,id) && id % 7 == 0)
                Sign(g, new(x, 2.65f, z + hz + .13f), id % 9 == 0 ? "ПРОДУКТЫ" : id % 9 == 3 ? "КОВРЫ" : "РЕМОНТ",
                    Math.Min(6, hx * 1.6f));
        }

        for (int side = -1; side <= 1; side += 2)
        for (int wall = 0; wall < 2; wall++)
        {
            if(!clinic&&CityCivicSites.BlankEnds(b)&&wall!=CityCivicSites.WindowWall(b))continue;
            Vec3 normal = wall == 0 ? new(0, 0, side) : new(side, 0, 0), right = wall == 0 ? Vec3.UnitX : Vec3.UnitZ;
            float half = wall == 0 ? hx : hz;
            Vec3 centre = new(x, 0, z);
            centre += normal * (wall == 0 ? hz + .035f : hx + .035f);
            for (float y = 2.8f; y < h - .7f; y += 2.5f)
            for (float at = -half + 1.15f; at < half - .55f; at += 2.1f)
            {
                Vec3 p = centre + right * at + Vec3.UnitY * y;
                bool balcony=((int)(at+half))%4<2&&y>4;
                uint ac=unchecked((uint)(id*73856093+(int)(y*10)*19349663+(int)(at*10)*83492791+wall*1237+side*997));
                if(!balcony&&ac%7==0)CityCivicVisual.AirConditioner(g,p,right,normal,lod);
                g.Material = lod == 0 ? SurfaceMaterial.Paper : SurfaceMaterial.ArchitecturalGlass;
                Vec3 a = p - right * .64f - Vec3.UnitY * .54f, c = p + right * .64f + Vec3.UnitY * .54f;
                int start = g.SurfacePoints.Count;
                if (Vec3.Dot(Vec3.Cross(right, Vec3.UnitY), normal) > 0)
                    g.Quad(a, a + right * 1.28f, c, c - right * 1.28f, new(.09f, .16f, .20f));
                else g.Quad(c - right * 1.28f, c, a + right * 1.28f, a, new(.09f, .16f, .20f));
                if (lod == 0)
                    for (int v = start; v < g.SurfacePoints.Count; v++)
                    {
                        var sv = g.SurfacePoints[v];
                        g.SurfacePoints[v] = sv with
                        {
                            Local = new(Vec3.Dot(sv.Position - p, right), sv.Position.Y - p.Y, id * 3 + y + at)
                        };
                    }

                g.Material = SurfaceMaterial.Enamel;
                Vec3 thick = wall == 0 ? new(.68f, .025f, .035f) : new(.035f, .025f, .68f);
                g.Box(p + normal * .018f + Vec3.UnitY * .56f, thick, new(.66f, .67f, .61f));
                g.Box(p + normal * .035f - Vec3.UnitY*.56f,thick,new(.57f,.58f,.52f));
                if (lod == 0)
                {
                    foreach(int edge in new[]{-1,1})g.Box(p+right*(edge*.66f)+normal*.018f,
                        wall==0?new(.025f,.55f,.035f):new(.035f,.55f,.025f),new(.61f,.63f,.57f));
                    g.Box(p + normal * .018f, wall == 0 ? new(.025f, .55f, .035f) : new(.035f, .55f, .025f),
                        new(.66f, .67f, .61f));
                    if (((int)(at + half)) % 4 < 2 && y > 4) Balcony(g, p, right, normal, (id + (int)((at+half)/2.1f) + wall) % 5);

                }
            }
        }
    }

    private static void CooperativeGarage(DioramaGeometry g,float x,float z,float hx,float hz,float h,int id,int lod)
    {
        g.Material=(SurfaceMaterial)(210+id%3);g.Box(new(x,h*.5f,z),new(hx,h*.5f,hz),new(.55f,.49f,.39f));
        g.Material=SurfaceMaterial.CastMetal;g.Box(new(x,h+.04f,z),new(hx+.12f,.08f,hz+.12f),new(.25f,.29f,.27f));
        if(lod==2)return;
        g.Material=SurfaceMaterial.Enamel;g.Box(new(x,1.3f,z+hz+.03f),new(hx*.83f,1.25f,.04f),id%2==0?new(.25f,.37f,.32f):new(.35f,.29f,.24f));
        g.Material=SurfaceMaterial.CastMetal;g.Box(new(x,1.3f,z+hz+.09f),new(.025f,1.25f,.025f),new(.14f,.17f,.15f));
        if(lod==0){g.Tube(new(x-.13f,1.05f,z+hz+.12f),new(x+.13f,1.05f,z+hz+.12f),.03f,new(.52f,.51f,.43f));Text(g,new(x,2.94f,z+hz+.07f),(id%90+10).ToString(),.45f,new(.78f,.77f,.62f));}
    }
    private static void Cottage(DioramaGeometry g,float x,float z,float hx,float hz,float h,int id,int lod)
    {
        float eave=h-1.4f;
        g.Material=(SurfaceMaterial)(210+id%3);g.Box(new(x,eave*.5f,z),new(hx,eave*.5f,hz),new(.52f,.55f,.43f));
        g.Material=SurfaceMaterial.Wood;
        g.Triangle(new(x-hx,eave,z-hz),new(x,eave+1.4f,z-hz),new(x+hx,eave,z-hz),new(.43f,.35f,.26f));
        g.Triangle(new(x+hx,eave,z+hz),new(x,eave+1.4f,z+hz),new(x-hx,eave,z+hz),new(.43f,.35f,.26f));
        g.Material=SurfaceMaterial.CastMetal;var roof=new Vec3(.26f,.31f,.29f);
        g.Quad(new(x-hx-.2f,eave-.08f,z+hz+.2f),new(x,eave+1.4f,z+hz+.2f),new(x,eave+1.4f,z-hz-.2f),new(x-hx-.2f,eave-.08f,z-hz-.2f),roof);
        g.Quad(new(x,eave+1.4f,z+hz+.2f),new(x+hx+.2f,eave-.08f,z+hz+.2f),new(x+hx+.2f,eave-.08f,z-hz-.2f),new(x,eave+1.4f,z-hz-.2f),roof);
        if(lod==2)return;
        g.Material=SurfaceMaterial.Wood;g.Box(new(x,.95f,z+hz+.035f),new(.5f,.95f,.04f),new(.23f,.27f,.24f));
        void Window(Vec3 centre,Vec3 right,Vec3 normal){
            void Box(float depth,Vec3 half,Vec3 color)=>g.Box(centre+normal*depth,half,color,right,Vec3.UnitY,normal);
            g.Material=SurfaceMaterial.Enamel;Box(.045f,new(.65f,.69f,.045f),new(.72f,.77f,.66f));
            g.Material=SurfaceMaterial.ArchitecturalGlass;Box(.105f,new(.52f,.55f,.025f),new(.15f,.25f,.28f));
            g.Material=SurfaceMaterial.Wood;Box(.14f,new(.025f,.55f,.025f),new(.75f,.77f,.67f));
            if(lod==0){Box(.14f,new(.52f,.025f,.025f),new(.75f,.77f,.67f));g.Material=SurfaceMaterial.Stone;g.Box(centre+normal*.13f-new Vec3(0,.7f,0),new(.7f,.055f,.15f),new(.52f,.53f,.46f),right,Vec3.UnitY,normal);}
        }
        for(int face=-1;face<=1;face+=2)for(int n=-1;n<=1;n+=2){
            Window(new(x+n*hx*.64f,1.7f,z+face*hz),new(face,0,0),new(0,0,face));
            Window(new(x+face*hx,1.7f,z+n*hz*.56f),new(0,0,-face),new(face,0,0));
        }
        if(lod==0){
            g.Material=SurfaceMaterial.Stone;g.Box(new(x,.13f,z),new(hx+.04f,.13f,hz+.04f),new(.36f,.37f,.32f));
            g.Material=(SurfaceMaterial)210;g.Box(new(x+hx*.35f,h-.5f,z-hz*.35f),new(.26f,.46f,.27f),new(.45f,.32f,.25f));
        }
    }

    private static void Balcony(DioramaGeometry g, Vec3 p, Vec3 right, Vec3 normal, int style)
    {
        Vec3 P(float x, float y, float z) => p + right * x + Vec3.UnitY * y + normal * z;

        void Box(float x, float y, float z, Vec3 half, Vec3 color) =>
            g.Box(P(x, y, z), half, color, right, Vec3.UnitY, normal);

        g.Material = SurfaceMaterial.Concrete;
        Box(0, -.66f, .53f, new(.85f, .075f, .66f), new(.43f, .43f, .38f));
        Vec3 paint = style == 0 ? new(.25f, .37f, .31f) : style == 1 ? new(.48f, .39f, .26f) : new(.51f, .53f, .48f);
        g.Material = SurfaceMaterial.CastMetal;
        for (int side = -1; side <= 1; side += 2)
        {
            g.Tube(P(side * .79f, -.58f, .04f), P(side * .79f, .35f, .04f), .024f, paint);
            g.Tube(P(side * .79f, -.58f, 1.12f), P(side * .79f, .35f, 1.12f), .024f, paint);
            g.Tube(P(side * .79f, .35f, .04f), P(side * .79f, .35f, 1.12f), .025f, paint);
        }

        g.Tube(P(-.79f, .35f, 1.12f), P(.79f, .35f, 1.12f), .025f, paint);
        if (style == 2)
        {
            for (int n = 0; n < 10; n++)
                g.Tube(P(-.73f + n * .16f, -.56f, 1.12f), P(-.73f + n * .16f, .34f, 1.12f), .012f, paint);
        }
        else
        {
            g.Material = SurfaceMaterial.Enamel;
            Box(0, -.15f, 1.1f, new(.77f, .45f, .025f), paint);
            for (int n = 0; n < 12; n++) Box(-.74f + n * .135f, -.15f, 1.135f, new(.016f, .43f, .012f), paint * .82f);
        }

        if (style == 3)
        {
            g.Material = SurfaceMaterial.ArchitecturalGlass;
            Box(0, .83f, 1.1f, new(.77f, .48f, .016f), new(.20f, .31f, .32f));
            g.Material = SurfaceMaterial.Wood;
            for (int n = -1; n <= 1; n++) Box(n * .76f, .83f, 1.13f, new(.026f, .49f, .025f), new(.50f, .43f, .31f));
            g.Material = SurfaceMaterial.Concrete;
            Box(0, 1.37f, .58f, new(.85f, .04f, .66f), new(.43f, .44f, .4f));
        }

        if (style == 1)
        {
            g.Material = SurfaceMaterial.Paper;
            Box(.2f, .07f, 1.15f, new(.38f, .30f, .013f), new(.45f, .31f, .24f));
        }

        if (style == 4)
        {
            g.Material = SurfaceMaterial.Wood;
            Box(-.35f, -.42f, .8f, new(.32f, .16f, .18f), new(.40f, .26f, .14f));
            g.Material = SurfaceMaterial.Foliage;
            g.Ellipsoid(P(-.35f, -.12f, .8f), new(.35f, .17f, .18f), new(.25f, .34f, .15f));
        }
    }

    private static void Tree(DioramaGeometry g, Vec3 p, float h, int type, int lod)
    {
        if(!CityAmenities.TreeClear(new(p.X,p.Z)))return;
        int start = g.SurfacePoints.Count;
        TreeCore(g, p, h, type, lod);
        CityTerrainGeometry.Rigid(g, start, p.X, p.Z);
    }

    private static void TreeCore(DioramaGeometry g, Vec3 p, float h, int type, int lod)
    {
        if(type==1){CityBirch.Draw(g,p,h,lod);return;}
        if (lod == 2)
        {
            g.Material = SurfaceMaterial.Foliage;
            g.Ellipsoid(p + new Vec3(0, h * .66f, 0), new(h * .25f, h * .32f, h * .23f), new(.23f, .31f, .14f));
            return;
        }

        if (type != 2 && lod == 0)
        {
            g.Material = SurfaceMaterial.Bark;
            Vec3 top = p + new Vec3(.12f, h * .68f, .10f);
            g.Tube(p, top, h * .035f, type == 1 ? new(.78f, .77f, .67f) : new(.27f, .21f, .14f));
            for (int limb = 0; limb < 7; limb++)
            {
                float a = limb * 2.39996f + p.X * .17f;
                Vec3 branch = p + new Vec3(MathF.Sin(a) * h * .22f, h * (.50f + limb * .055f), MathF.Cos(a) * h * .22f);
                g.Material = SurfaceMaterial.Bark;
                g.Tube(p + Vec3.UnitY * h * (.32f + limb * .04f), branch, h * .012f, new(.30f, .25f, .17f));
                g.Material = SurfaceMaterial.Foliage;
                Vec3 color = Vec3.Lerp(new(.18f, .25f, .13f), new(.33f, .39f, .20f), (limb % 4) / 3f);
                // Baked leaf diamonds leave real gaps through the canopy. The
                // near crown is not a solid balloon; mid/far crowns use cheap LODs.
                for (int leaf = 0; leaf < 64; leaf++)
                {
                    float a0 = leaf * 2.399963f + limb, yy = (leaf + .5f) / 64 * 2 - 1, rr = MathF.Sqrt(1 - yy * yy);
                    Vec3 centre = branch + new Vec3(MathF.Cos(a0) * rr * h * .18f, yy * h * (type == 1 ? .25f : .18f),
                        MathF.Sin(a0) * rr * h * .18f);
                    Vec3 longAxis = new Vec3(MathF.Cos(a0 + .7f), .35f + yy * .6f, MathF.Sin(a0 + .7f)).Normalized() *
                                    (type == 1 ? .17f : .17f);
                    Vec3 across = new Vec3(-MathF.Sin(a0 + .7f), .25f, MathF.Cos(a0 + .7f)).Normalized() *
                                  (type == 1 ? .065f : .078f);
                    Vec3 c0 = centre - longAxis, c1 = centre + across, c2 = centre + longAxis, c3 = centre - across;
                    Vec3 tint = color * (.82f + .28f * ((leaf * 13) % 17) / 16);
                    g.Triangle(c0, c1, c2, tint);
                    g.Triangle(c0, c2, c3, tint);
                    g.Triangle(c0, c2, c1, tint * .92f);
                    g.Triangle(c0, c3, c2, tint * .92f);
                }

                if (type == 1)
                {
                    g.Material = SurfaceMaterial.Bark;
                    g.Tube(branch, branch - new Vec3(.08f, h * .24f, .04f), .012f, new(.31f, .27f, .18f));
                }
            }

            if (type == 1)
                for (int mark = 0; mark < 7; mark++)
                {
                    g.Material = SurfaceMaterial.Bark;
                    g.Box(p + new Vec3(h * .034f, .3f + mark * h * .066f, 0), new(.012f, .026f, h * .025f),
                        new(.17f, .18f, .15f));
                }

            return;
        }

        g.Material = SurfaceMaterial.Bark;
        g.Tube(p, p + Vec3.UnitY * h * .8f, h * .035f, type == 1 ? new(.71f, .71f, .61f) : new(.24f, .17f, .11f));
        g.Material = SurfaceMaterial.Foliage;
        if (type == 2 && lod == 0)
        {
            for (int tier = 0; tier < 9; tier++)
            for (int arm = 0; arm < 7; arm++)
            {
                float angle = arm * MathF.Tau / 7 + tier * 1.71f + p.X * .11f,
                    radius = h * (.29f - tier * .027f),
                    y = h * (.25f + tier * .073f);
                Vec3 outward = new(MathF.Cos(angle), -.18f, MathF.Sin(angle)), side = new(-outward.Z, 0, outward.X);
                for (int spray = 0; spray < 7; spray++)
                {
                    float t = (spray + .5f) / 7;
                    Vec3 centre = p + Vec3.UnitY * y + outward * (radius * t),
                        longAxis = outward * (.22f + .11f * (1 - t));
                    // Crossed, drooping needle sprays give each branch volume,
                    // including at street-level views; all geometry is baked.
                    for (int plane = 0; plane < 2; plane++)
                    {
                        Vec3 across = side * (h * .075f * (1 - t) + .065f) +
                                      Vec3.UnitY * ((plane == 0 ? 1 : -1) * h * .04f * (1 - t));
                        Vec3 a = centre - longAxis,
                            b = centre + across,
                            c = centre + longAxis,
                            d = centre - across,
                            color = new(.12f + .035f * (arm % 3), .24f + .04f * (tier % 3), .15f);
                        g.Triangle(a, b, c, color);
                        g.Triangle(a, c, d, color);
                        g.Triangle(a, c, b, color * .85f);
                        g.Triangle(a, d, c, color * .85f);
                    }
                }
            }
        }
        else if (type == 2)
        {
            for (int tier = 0; tier < 4; tier++)
            for (int n = 0; n < 10; n++)
            {
                float a = n * MathF.Tau / 10,
                    b = (n + 1) * MathF.Tau / 10,
                    r = h * (.31f - tier * .05f),
                    y = h * (.24f + tier * .17f);

                g.Triangle(p + new Vec3(r * MathF.Cos(b), y, r * MathF.Sin(b)),
                    p + new Vec3(r * MathF.Cos(a), y, r * MathF.Sin(a)), p + new Vec3(0, y + h * .4f, 0),
                    new(.12f, .23f, .15f));
            }
        }
        else
        {
            for (int n = 0; n < (lod == 0 ? 5 : 2); n++)
            {
                float a = n * 2.4f;

                g.Ellipsoid(
                    p + new Vec3(MathF.Cos(a) * .65f,h * .67f + n * .12f, MathF.Sin(a) * .65f),
                    new Vec3(h * .25f, h * .27f, h * .23f),
                    new Vec3(.31f, .39f, .15f));
            }
        }
    }
}
