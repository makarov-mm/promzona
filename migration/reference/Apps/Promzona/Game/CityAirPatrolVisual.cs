using MakarovPhysics.Renderer;
namespace Promzona.Game;

/// <summary>Police equipment follows the existing helicopter model and its attitude.</summary>
internal static class CityAirPatrolVisual
{
    public static void Draw(DioramaGeometry g, CityModel model, CityVisibility? visibility)
    {
        foreach (var car in model.Traffic.Cars)
        {
            if (!car.AirPatrol || car.Destroyed) continue;
            Vec3 origin = new((float)car.Position.X, (float)(car.SurfaceHeight + car.Lift), (float)car.Position.Z);
            if ((g.Eye - origin).Length() > 180 || !(visibility?.Sphere(origin + Vec3.UnitY * 2, 6) ?? true)) continue;
            int start = g.SurfacePoints.Count;
            var frame = Mat4.Translate(origin) * Mat4.RotateY((float)car.Heading) *
                Mat4.RotateX(-(float)car.Pitch) * Mat4.RotateZ(-(float)car.Roll);
            Vec3 right = frame.TransformDirection(Vec3.UnitX), up = frame.TransformDirection(Vec3.UnitY),
                forward = frame.TransformDirection(Vec3.UnitZ);
            for (int side = -1; side <= 1; side += 2)
            {
                g.Material = SurfaceMaterial.Enamel;
                g.Box(frame.TransformPoint(new(side * 1.045f, 1.45f, -.2f)), new(.035f, .19f, .92f), new(.07f, .22f, .48f), right, up, forward);
                if ((g.Eye - origin).Length() < 100)
                    CityArchitecture.Text(g, frame.TransformPoint(new(side * 1.09f, 1.47f, -.2f)), "МИЛИЦИЯ", 1.65f,
                        new(.86f, .91f, .95f), forward * -side, up);
            }
            g.Material = SurfaceMaterial.Emissive;
            float flash = Math.Sin(model.Time * 11) > 0 ? 1f : .3f;
            g.Ellipsoid(frame.TransformPoint(new(0, 2.8f, 1.05f)), new(.12f, .09f, .12f), new Vec3(.08f, .35f, 1) * flash);
            g.Material = SurfaceMaterial.Automatic;
            CityTerrainGeometry.Absolute(g, start);
        }
    }
}
