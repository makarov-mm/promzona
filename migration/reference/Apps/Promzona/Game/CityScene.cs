using MakarovPhysics.Renderer;
using static MakarovPhysics.Experiments.InstrumentDrawing;

namespace Promzona.Game;

internal static class CityScene
{
    private static void Car(DioramaGeometry g, CityCar car, double time)
    {
        g.BevelBoxes = false;
        float angle = (float)car.Heading,
            roll = (float)(car.Roll + car.TerrainRoll),
            pitch = (float)(car.Pitch + car.TerrainPitch);
        Vec3 f0 = new(MathF.Sin(angle), 0, MathF.Cos(angle)), r0 = new(f0.Z, 0, -f0.X);
        Vec3 up = Vec3.UnitY * MathF.Cos(roll) + r0 * MathF.Sin(roll),
            r = r0 * MathF.Cos(roll) - Vec3.UnitY * MathF.Sin(roll);
        Vec3 f = f0 * MathF.Cos(pitch) + up * MathF.Sin(pitch);
        up = up * MathF.Cos(pitch) - f0 * MathF.Sin(pitch);
        float support = MathF.Abs(MathF.Sin((float)car.Roll)) * (float)car.HalfWidth +
                        MathF.Abs(MathF.Sin((float)car.Pitch)) * (float)car.HalfLength +
                        MathF.Abs(MathF.Cos((float)car.Roll) * MathF.Cos((float)car.Pitch)) * .7f;
        Vec3 origin = new Vec3((float)car.Position.X, (float)(car.SurfaceHeight + car.Lift) + MathF.Max(.7f, support),
            (float)car.Position.Z) - up * .7f;
        Vec3 P(float x, float y, float z) => origin + r * x + up * y + f * z;
        var bodyPaint = CityAppearance.Paint(car);
        Vec3 paint = new(bodyPaint.R, bodyPaint.G, bodyPaint.B);
        // Distant actors retain their measured footprint and moving pose; small
        // wheels need no forty-sided cylinders in a city-wide view.
        if (!car.Hero && (g.Eye - origin).Length() > 95 * CityLod.DetailScale)
        {
            float h = (float)car.Height, w = (float)car.HalfWidth, l = (float)car.HalfLength;
            g.Material = SurfaceMaterial.Enamel;
            if (car.Kind == 2)
            {
                g.Box(P(0, h * .54f, -.55f), new(w - .08f, h * .40f, l - 1),
                    car.Destroyed ? paint : new(.73f, .76f, .72f), r, up, f);
                g.Box(P(0, 1.12f, l - .72f), new(w - .12f, .66f, .60f), paint, r, up, f);
            }
            else g.Box(P(0, h * .48f, 0), new(w - .1f, h * .32f, l - .12f), paint, r, up, f);

            if (!car.Destroyed && car.PoliceVehicle)
            {
                g.Box(P(0, h * .81f, .10f), new(w - .23f, .04f, l * .38f), Dark, r, up, f);
                g.Box(P(0, h * .805f, l * .73f), new(w - .12f, .025f, l * .23f), Dark, r, up, f);
            }

            if (!car.Destroyed && car.Kind == 5)
                for (int side = -1; side <= 1; side += 2)
                    g.Box(P(side * (w - .085f), .8f, 0), new(.016f, .12f, l - .2f), new(.38f, .21f, .14f), r, up, f);
            if (!car.Destroyed && car.Kind is 3 or 4 or 6 or 7)
                for (int side = -1; side <= 1; side += 2)
                    g.Box(P(side * (w - .085f), h * .48f, 0), new(.016f, .12f, l - .2f),
                        car.PoliceVehicle ? new(.035f, .15f, .42f) : new(1, .22f, .025f), r, up, f);
            g.Material = SurfaceMaterial.ArchitecturalGlass;
            g.Box(P(0, h * .77f, .25f), new(w - .20f, h * .18f, l * .37f), new(.07f, .16f, .22f), r, up, f);
            g.Material = SurfaceMaterial.Rubber;
            for (int side = -1; side <= 1; side += 2)
            for (int axle = -1; axle <= 1; axle += 2)
                g.Box(P(side * (w - .04f), .35f, axle * (l - .6f)), new(.12f, .31f, .31f), Dark, r, up, f);
            if (car.Destroyed) return;
            g.Material = SurfaceMaterial.Emissive;
            for (int side = -1; side <= 1; side += 2)
            {
                g.Box(P(side * (w - .25f), .7f, l - .10f), new(.16f, .06f, .02f), White, r, up, f);
                g.Box(P(side * (w - .25f), .65f, -l + .10f), new(.16f, .06f, .02f), Red * (car.Braking ? 2 : .6f), r,
                    up, f);
            }

            if (car.Kind is 3 or 4 or 6 or 7)
                g.Box(P(0, h, 0), new(.58f, .08f, .12f),
                    new Vec3(.08f, .35f, 1.8f) * (Math.Sin(time * 9 + car.Id) > 0 ? 1.4f : .2f), r, up, f);
            return;
        }

        g.BevelBoxes = (g.Eye - origin).Length() < 70 * CityLod.DetailScale;

        if (car.Kind == 5)
        {
            Bus(g, car, P, r, up, f);
            return;
        }

        if (car.Kind is 1 or 8)
        {
            g.Material = SurfaceMaterial.Enamel;
            g.Box(P(0, 1.1f, 0), new(.95f, .75f, 2.05f), paint, r, up, f);
            g.Box(P(0, 1.86f, -.05f), new(.85f, .14f, 1.89f), new(.58f, .62f, .46f), r, up, f);
            g.Material = SurfaceMaterial.ArchitecturalGlass;
            for (int side = -1; side <= 1; side += 2)
            {
                g.Box(P(side * .47f, 1.48f, 2.06f), new(.40f, .30f, .014f), new(.08f, .19f, .24f), r, up, f);
                for (int win = 0; win < 3; win++)
                    g.Box(P(side * .956f, 1.48f, 1.25f - win * 1.1f), new(.014f, .29f, .39f), new(.08f, .19f, .24f), r,
                        up, f);
            }

            g.Material = SurfaceMaterial.CastMetal;
            g.Box(P(0, .75f, 2.10f), new(.30f, .16f, .025f), Dark, r, up, f);
            g.Box(P(0, .43f, 2.13f), new(.94f, .07f, .08f), Steel, r, up, f);
            for (int side = -1; side <= 1; side += 2)
            {
                g.Material = SurfaceMaterial.Emissive;
                g.Ellipsoid(P(side * .67f, .84f, 2.10f), new(.14f, .14f, .045f), White);
                for (int axle = -1; axle <= 1; axle += 2)
                {
                    var hub = P(side * .96f, .36f, axle * 1.35f);
                    g.Material = SurfaceMaterial.Rubber;
                    g.Tube(hub - r * .12f, hub + r * .12f, .35f, Dark);
                    g.Material = SurfaceMaterial.BrushedAluminium;
                    g.Tube(hub + r * side * .13f, hub + r * side * .14f, .19f, Steel);
                }
            }

            return;
        }

        if (car.Kind is 2 or 4)
        {
            float length = (float)car.HalfLength, width = (float)car.HalfWidth, height = (float)car.Height;
            g.Material = SurfaceMaterial.MachinedMetal;
            g.Box(P(0, .5f, 0), new(width - .1f, .13f, length - .1f), Dark, r, up, f);
            g.Material = SurfaceMaterial.Enamel;
            g.Box(P(0, height * .54f, -.55f), new(width - .08f, height * .40f, length - 1),
                car.Kind == 2 ? new(.73f, .76f, .72f) : paint, r, up, f);
            g.Box(P(0, 1.12f, length - .72f), new(width - .12f, .66f, .60f), paint, r, up, f);
            g.Material = SurfaceMaterial.ArchitecturalGlass;
            g.Box(P(0, 1.46f, length - .105f), new(width - .23f, .27f, .016f), new(.07f, .18f, .25f), r, up, f);
            for (int side = -1; side <= 1; side += 2)
            {
                g.Box(P(side * (width - .11f), 1.45f, length - .70f), new(.016f, .27f, .43f), new(.07f, .18f, .25f), r,
                    up, f);
            }

            if (car.Kind == 4)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    g.Material = SurfaceMaterial.Enamel;
                    g.Box(P(side * (width - .065f), 1.2f, -.6f), new(.018f, .18f, length - 1.05f), new(1, .22f, .025f),
                        r, up, f);
                    // Blue medical cross, separate from the emergency light bar.
                    g.Box(P(side * (width - .04f), 1.95f, -.6f), new(.02f, .30f, .095f), Blue, r, up, f);
                    g.Box(P(side * (width - .035f), 1.95f, -.6f), new(.025f, .095f, .30f), Blue, r, up, f);
                }

                g.Material = SurfaceMaterial.Rubber;
                g.Box(P(0, height + .03f, -.2f), new(.70f, .065f, .15f), Dark, r, up, f);
                for (int side = -1; side <= 1; side += 2)
                {
                    g.Material = SurfaceMaterial.Emissive;
                    g.Box(P(side * .46f, height + .13f, -.2f), new(.20f, .075f, .13f),
                        new Vec3(.08f, .35f, 1.8f) * (Math.Sin(time * 9 + car.Id + side) > 0 ? 1.8f : .12f), r, up, f);
                }
            }

            int axles = car.Kind == 2 ? 3 : 2;
            for (int side = -1; side <= 1; side += 2)
            for (int axle = 0; axle < axles; axle++)
            {
                float z = axle == 0 ? length - .8f : -length + .65f + (axle - 1) * .9f,
                    steer = axle == 0 ? (float)car.Steering : 0;
                Vec3 axis = r * MathF.Cos(steer) - f * MathF.Sin(steer), centre = P(side * (width - .05f), .43f, z);
                g.Material = SurfaceMaterial.Rubber;
                g.Tube(centre - axis * .12f, centre + axis * .12f, .41f, Dark);
                g.Material = SurfaceMaterial.BrushedAluminium;
                g.Tube(centre + axis * side * .125f, centre + axis * side * .14f, .23f, Steel);
            }

            g.Material = SurfaceMaterial.Emissive;
            for (int side = -1; side <= 1; side += 2)
            {
                g.Box(P(side * (width - .3f), .8f, length - .09f), new(.17f, .08f, .018f), new(1.2f, 1.1f, .8f), r, up,
                    f);
                g.Box(P(side * (width - .3f), .7f, -length + .08f), new(.18f, .06f, .02f),
                    Red * (car.Braking ? 3 : .6f), r, up, f);
            }

            return;
        }

        g.Material = SurfaceMaterial.Enamel;
        Hull(g, P, paint);
        g.Material = SurfaceMaterial.MachinedMetal;
        g.Box(P(0, .43f, 1.69f), new(.77f, .07f, .06f), Steel, r, up, f);
        g.Box(P(0, .43f, -1.69f), new(.77f, .07f, .06f), Steel, r, up, f);
        for (int side = -1; side <= 1; side += 2)
        {
            g.Box(P(side * .90f, 1.0f, .45f), new(.12f, .06f, .16f), paint, r, up, f);
            g.Material = SurfaceMaterial.ArchitecturalGlass;
            g.Box(P(side * .90f, 1.0f, .31f), new(.10f, .05f, .012f), Steel, r, up, f);
            g.Material = SurfaceMaterial.Enamel;
        }

        Vec3 a = P(-.72f, .84f, .76f), b = P(.72f, .84f, .76f), c = P(.62f, 1.28f, .25f), d = P(-.62f, 1.28f, .25f);
        Vec3 e = P(-.72f, .84f, -1.02f),
            q = P(.72f, .84f, -1.02f),
            t = P(.62f, 1.28f, -.65f),
            u = P(-.62f, 1.28f, -.65f);
        g.Quad(d, c, t, u, paint);
        g.Quad(a, d, u, e, paint);
        g.Quad(b, q, t, c, paint);
        g.Material = SurfaceMaterial.ArchitecturalGlass;
        g.Quad(a, b, c, d, new(.08f, .18f, .24f));
        g.Quad(e, u, t, q, new(.08f, .18f, .24f));
        for (int side = -1; side <= 1; side += 2)
        {
            Vec3 outwards = r * (side * .008f);
            g.Quad(P(side * .715f, .89f, .64f) + outwards, P(side * .625f, 1.23f, .24f) + outwards,
                P(side * .625f, 1.23f, -.61f) + outwards, P(side * .715f, .89f, -.91f) + outwards,
                new(.08f, .18f, .24f));
            g.Material = SurfaceMaterial.Enamel;
            g.Tube(P(side * .73f, .85f, -.18f), P(side * .63f, 1.27f, -.18f), .023f, paint);
            g.Material = SurfaceMaterial.MachinedMetal;
            g.Box(P(side * .89f, .78f, -.40f), new(.018f, .025f, .12f), Steel, r, up, f);
            g.Material = SurfaceMaterial.ArchitecturalGlass;
        }



        for (int side = -1; side <= 1; side += 2)
        for (int axle = -1; axle <= 1; axle += 2)
        {
            float steer = axle > 0 ? (float)car.Steering : 0;
            Vec3 axis = r * MathF.Cos(steer) - f * MathF.Sin(steer);
            Vec3 centre = P(side * .89f, .35f, axle * 1.15f);
            g.Material = SurfaceMaterial.Rubber;
            g.Tube(centre - axis * .10f, centre + axis * .10f, .34f, Dark);
            g.Material = SurfaceMaterial.BrushedAluminium;
            g.Tube(centre + axis * side * .105f, centre + axis * side * .12f, .20f, Steel);
        }

        g.Material = SurfaceMaterial.Rubber;
        g.Box(P(0, .50f, 1.67f), new(.68f, .08f, .02f), Dark, r, up, f);
        g.Material = SurfaceMaterial.Emissive;
        for (int side = -1; side <= 1; side += 2)
        {
            g.Box(P(side * .62f, .69f, 1.66f), new(.18f, .055f, .018f), new(1.2f, 1.15f, .9f), r, up, f);
            g.Box(P(side * .6f, .64f, -1.66f), new(.20f, .06f, .018f),
                new Vec3(.85f, .025f, .01f) * (car.Braking ? 3 : .7f), r, up, f);
        }

        if (car.PoliceVehicle)
        {
            g.Material = SurfaceMaterial.Enamel;
            for (int side = -1; side <= 1; side += 2)
            {
                g.Box(P(side * .878f, .68f, -.1f), new(.013f, .13f, 1.38f), new(.035f, .15f, .42f), r, up, f);
                for (int stripe = 0; stripe < 7; stripe++)
                    g.Box(P(side * .895f, .81f, -1.13f + stripe * .34f), new(.015f, .045f, .085f), White, r, up, f);
                g.Material = SurfaceMaterial.Brass;
                g.Box(P(side * .899f, .68f, -.2f), new(.017f, .10f, .095f), Gold, r, up, f);
            }

            g.Material = SurfaceMaterial.Rubber;
            g.Box(P(0, 1.34f, -.15f), new(.61f, .065f, .13f), Dark, r, up, f);
            for (int side = -1; side <= 1; side += 2)
            {
                g.Material = SurfaceMaterial.Emissive;
                g.Box(P(side * .41f, 1.45f, -.15f), new(.19f, .08f, .12f),
                    (side < 0 ? new Vec3(.08f, .35f, 1.8f) : new Vec3(1.8f, .05f, .025f)) *
                    (Math.Sin(time * 9 + car.Id + side) > 0 ? 1.5f : .1f), r, up, f);
            }
        }
    }

    private static void Person(DioramaGeometry g, CityPedestrian p)
    {
        float a = (float)p.Heading, phase = (float)p.Distance * 7;
        Vec3 f = new(MathF.Sin(a), 0, MathF.Cos(a)),
            r = new(f.Z, 0, -f.X),
            o = new((float)p.Position.X,
                (float)(CityRoadLevels.Ground(p) + p.FlightHeight) + (p.Crossing ? .025f : .15f),
                (float)p.Position.Z);
        float tilt = p.Dead ? MathF.PI * .5f : (float)Math.Clamp(p.KnockedFor, 0, 1) * 1.45f;
        Vec3 up = Vec3.UnitY * MathF.Cos(tilt) + f * MathF.Sin(tilt);
        Vec3 along = f * MathF.Cos(tilt) - Vec3.UnitY * MathF.Sin(tilt);
        // Raise the pivot slightly while prone so limbs stay above the road.
        o += Vec3.UnitY * (.18f * MathF.Sin(tilt));
        float build = (float)p.ShoulderWidth / .46f, stature = (float)p.Height / 1.8f;
        Vec3 P(float x, float y, float z) => o + r * (x * build) + up * (y * stature) + along * z;
        Vec3 coat = (p.Id % 8) switch
        {
            0 => Blue, 1 => Red, 2 => new(.52f, .42f, .18f), 3 => new(.22f, .39f, .29f), 5 => new(.18f, .19f, .23f),
            6 => new(.64f, .62f, .50f), 7 => new(.31f, .28f, .40f), _ => new(.42f, .20f, .39f)
        };
        Vec3 skin = Vec3.Lerp(new(.31f, .17f, .10f), new(.78f, .57f, .38f), (p.Id % 7) / 6f);
        if ((g.Eye - o).Length() > 75 * CityLod.DetailScale)
        {
            g.Material = SurfaceMaterial.Polymer;
            g.Box(P(0, 1.13f, 0), new(.19f, .29f, .12f), coat, r, up, along);
            g.Box(P(0, 1.61f, 0), new(.12f, .16f, .12f), skin, r, up, along);
            g.Box(P(-.11f, .44f, 0), new(.07f, .40f, .07f), Dark);
            g.Box(P(.11f, .44f, 0), new(.07f, .40f, .07f), Dark);
            return;
        }

        g.Material = SurfaceMaterial.Polymer;
        g.Box(P(0, 1.13f, 0), new(.19f, .29f, .12f), coat, r, up, along);
        g.Ellipsoid(P(0, 1.61f, 0), new Vec3(.135f, .17f, .135f) * stature, skin);
        g.Ellipsoid(P(0, 1.72f, -.025f), new(.14f, .08f, p.Id % 2 == 0 ? .15f : .19f),
            p.Id % 4 == 0 ? new(.52f, .35f, .16f) : new(.12f, .085f, .06f));
        if (p.Id % 3 == 0)
        {
            g.Material = SurfaceMaterial.Textile;
            g.Box(P(0, .91f, 0), new(.22f * build, .18f, .15f), coat, r, up, along);
        }

        for (int side = -1; side <= 1; side += 2)
        {
            float swing = p.Speed > .02 && p.KnockedFor <= 0 ? .24f * MathF.Sin(phase + side * MathF.PI / 2) : 0;
            Vec3 hip = P(side * .11f, .84f, 0),
                knee = P(side * .11f, .46f, swing * .6f),
                foot = P(side * .11f, .10f, swing);
            g.Tube(hip, knee, .085f, Dark);
            g.Tube(knee, foot, .07f, Dark);
            g.Box(foot + f * .04f, new(.09f, .065f, .16f), Dark, r, up, along);
            Vec3 shoulder = P(side * .23f, 1.35f, 0),
                elbow = P(side * .26f, 1.06f, -swing * .5f),
                hand = P(side * .25f, .90f, -swing);
            g.Tube(shoulder, elbow, .065f, coat);
            g.Tube(elbow, hand, .055f, coat);
            g.Ellipsoid(hand, new(.06f, .07f, .06f), skin);
        }
    }

    public static void Draw(DioramaGeometry g, CityModel m, CityVehicleModels? models = null,
        CityActorModels? actors = null, CityVisibility? visibility = null, int drawDistance=CityLod.DefaultDistance)
    {
        bool Visible(CityPoint p, float radius = 3) =>
            visibility?.Sphere(new((float)p.X, 1 + (float)CityTerrain.Ground(p), (float)p.Z), radius) ?? true;

        g.PrecisionFinish = false;
        g.BevelBoxes = false;
        foreach (var c in m.Traffic.Cars)
            if (CityLod.InHorizontalRange(c.Position.X,c.Position.Z,g.Eye.X,g.Eye.Z,CityLod.VehicleRange(drawDistance)) && Visible(c.Position, (float)c.HalfLength + 2) &&
                (g.Eye - new Vec3((float)c.Position.X, (float)c.SurfaceHeight, (float)c.Position.Z)).Length() <
                CityLod.VehicleRange(drawDistance) && !(models?.Replaces(c, g.Eye) ?? false))
                Car(g, c, m.Time);
        g.BevelBoxes = false;
        foreach (var p in m.Traffic.Pedestrians)
            if (CityLod.InHorizontalRange(p.Position.X,p.Position.Z,g.Eye.X,g.Eye.Z,CityLod.ActorRange(drawDistance)) && Visible(p.Position, 2) &&
                (g.Eye - new Vec3((float)p.Position.X, (float)CityRoadLevels.Ground(p), (float)p.Position.Z))
                .Length() < CityLod.ActorRange(drawDistance) && !(actors?.Replaces(p, g.Eye) ?? false))
                Person(g, p);
        foreach (var animal in m.Traffic.Animals)
            if (CityLod.InHorizontalRange(animal.Position.X,animal.Position.Z,g.Eye.X,g.Eye.Z,CityLod.ActorRange(drawDistance)) && Visible(animal.Position, 2) &&
                (g.Eye - new Vec3((float)animal.Position.X, (float)CityTerrain.Ground(animal.Position),
                    (float)animal.Position.Z)).Length() < CityLod.ActorRange(drawDistance))
            {
                if (!(actors?.Replaces(animal, g.Eye) ?? false)) Animal(g, animal);
                if (animal.Flying && !animal.Dead) Wings(g, animal, m.Time);
            }

        foreach(var car in m.Traffic.Cars)if(car.TransitRoute>0&&Visible(car.Position,8)&&(car.Position-m.Traffic.PlayerPosition).Length<180)CityTransitVisual.Collector(g,car);
        CityPeopleDetail.Draw(g,m,visibility,actors);
        CityScooterVisual.Draw(g,m,visibility,actors);
        CityPickupVisual.Draw(g, m, visibility);
        CityAmenitiesVisual.Dynamic(g,m,visibility);
        CityPeaceVisual.Draw(g,m,visibility);
        CityAirPatrolVisual.Draw(g,m,visibility);
        if(m.Traffic.ContractActive&&Visible(m.Traffic.JobTarget,8)&&m.Traffic.JobDistance<180){
            int start=g.SurfacePoints.Count;var at=m.Traffic.JobTarget;
            bool person=m.Traffic.JobKind==JobKind.Elimination;
            float height=person?2.7f:m.Traffic.JobStage==0?4.5f:1.3f;
            var targetPerson=person?m.Traffic.Pedestrians.Find(a=>a.Id==m.Traffic.ContractActorId):null;
            double ground=targetPerson is null?CityTerrain.Ground(at):CityRoadLevels.Ground(targetPerson)+targetPerson.FlightHeight;
            Vec3 p=new((float)at.X,(float)ground+height+.16f*MathF.Sin((float)m.Time*3),(float)at.Z);
            var color=person?new Vec3(1,.22f,.08f):new Vec3(1,.78f,.14f);g.Material=SurfaceMaterial.Emissive;
            // Solid downward-pointing square pyramid, with separately shaded faces.
            float yaw=(float)m.Time*.5f;Vec3 rx=new(MathF.Cos(yaw),0,-MathF.Sin(yaw)),fz=new(MathF.Sin(yaw),0,MathF.Cos(yaw));
            Vec3 top=p+Vec3.UnitY*.65f;
            Vec3[] corners=[top-rx*.38f-fz*.38f,top+rx*.38f-fz*.38f,top+rx*.38f+fz*.38f,top-rx*.38f+fz*.38f];
            for(int face=0;face<4;face++)g.Triangle(p,corners[face],corners[(face+1)%4],color*(.55f+face*.13f));
            g.Quad(corners[3],corners[2],corners[1],corners[0],color);
            CityTerrainGeometry.Absolute(g,start);g.Material=SurfaceMaterial.Automatic;
        }
        foreach (var cash in m.Traffic.Cash)
        {
            if (!Visible(cash.Position, 1)) continue;
            Vec3 at = new((float)cash.Position.X, .23f + (float)CityRoadLevels.Surface(cash.Position.X,cash.Position.Z,cash.RoadLevel),
                (float)cash.Position.Z);
            if ((g.Eye - at).Length() > 90) continue;
            g.Material = SurfaceMaterial.Paper;
            g.Box(at, new(.24f, .035f, .12f), new(.38f, .56f, .29f));
            g.Material = SurfaceMaterial.Emissive;
            g.Box(at + new Vec3(0, .038f, 0), new(.055f, .002f, .12f), new(.85f, .82f, .58f));
        }

        foreach(var c in m.Traffic.Cars)if((c.PoliceVehicle||c.Kind is 4 or 8)&&Visible(c.Position,5)&&!c.Destroyed&&(c.Position-m.Traffic.PlayerPosition).Length<60){
            float roll=(float)(c.Roll+c.TerrainRoll),pitch=(float)(c.Pitch+c.TerrainPitch);
            float support=Math.Max(.7f,Math.Abs(MathF.Sin((float)c.Roll))*(float)c.HalfWidth+Math.Abs(MathF.Sin((float)c.Pitch))*(float)c.HalfLength+Math.Abs(MathF.Cos((float)c.Roll)*MathF.Cos((float)c.Pitch))*.7f);
            Mat4 body=Mat4.Translate(new((float)c.Position.X,(float)(c.SurfaceHeight+c.Lift)+support,(float)c.Position.Z))*Mat4.RotateY((float)c.Heading)*Mat4.RotateX(-pitch)*Mat4.RotateZ(-roll)*Mat4.Translate(new(0,-.7f,0));
            for(int side=-1;side<=1;side+=2){
                Vec3 at=body.TransformPoint(new(side*((float)c.HalfWidth+.02f),(float)c.Height*.48f,-(float)c.HalfLength*.38f));
                CityArchitecture.Text(g,at,c.Kind==8?"ГАЗОВАЯ СЛУЖБА":c.Kind==4?"СКОРАЯ":c.Kind==7?"РОСГВАРДИЯ":"МИЛИЦИЯ",c.Kind==7?2.3f:1.4f,new(.07f,.14f,.27f),body.TransformDirection(Vec3.UnitZ*(-side)),body.TransformDirection(Vec3.UnitY));
            }
        }

        foreach (var p in m.Traffic.Pedestrians)
            if (p.Aiming && !p.Dead && Visible(p.Position) && (p.Position - m.Traffic.PlayerPosition).Length < 50)
            {
                Vec3 f = new((float)Math.Sin(p.Heading), 0, (float)Math.Cos(p.Heading)),
                    r = new(f.Z, 0, -f.X),
                    o = new((float)p.Position.X, (float)p.Height * .72f + .15f, (float)p.Position.Z);
                float handWidth = (float)p.ShoulderWidth / .51f, handDepth = .94f + (p.Id % 5) * .035f;
                // These offsets match the baked two-hand aim pose in PeopleBaked.
                o = new(o.X, (float)CityRoadLevels.Ground(p) + .15f + 1.25f * (float)p.Height / 1.72f, o.Z);
                float aim=(float)(p.IsPlayer?m.Traffic.PlayerViewPitch:Math.Atan2(CityTerrain.Ground(m.Traffic.PlayerPosition)-CityRoadLevels.Ground(p),Math.Max(1,(m.Traffic.PlayerPosition-p.Position).Length)));
                Vec3 aimForward=f*MathF.Cos(aim)+Vec3.UnitY*MathF.Sin(aim),aimUp=Vec3.UnitY*MathF.Cos(aim)-f*MathF.Sin(aim);
                CityPickupVisual.Gun(g,p.Weapon,o+aimForward*(.48f*handDepth)+r*(.16f*handWidth),r,aimForward,1,aimUp);
            }

        Street(g, m, visibility);
        int lowX=Math.Max(0,(int)Math.Floor((m.X-115-CityTraffic.Coordinate(0))/CityTraffic.Spacing));
        int highX=Math.Min(CityTraffic.Nodes-1,(int)Math.Ceiling((m.X+115-CityTraffic.Coordinate(0))/CityTraffic.Spacing));
        int lowZ=Math.Max(0,(int)Math.Floor((m.Z-115-CityTraffic.Coordinate(0))/CityTraffic.Spacing));
        int highZ=Math.Min(CityTraffic.Nodes-1,(int)Math.Ceiling((m.Z+115-CityTraffic.Coordinate(0))/CityTraffic.Spacing));
        for (int i = lowX; i <= highX; i++)
        for (int j = lowZ; j <= highZ; j++)
        for (int d = 0; d < 4; d++)
        {
            if (Math.Abs(CityTraffic.Coordinate(i) - m.X) > 115 ||
                Math.Abs(CityTraffic.Coordinate(j) - m.Z) > 115) continue;
            if (!CityTraffic.Signalised(i, j) || !CityTraffic.HasRoad(i, j, (d + 2) % 4)) continue;
            var ff = CityTraffic.Forward(d);
            Vec3 f = new((float)ff.X, 0, (float)ff.Z),
                r = new(-f.Z, 0, f.X),
                c = new((float)CityTraffic.Coordinate(i), 0, (float)CityTraffic.Coordinate(j));
            Vec3 pole = c - f * 9.8f + r * (CityLayout.WideRoads?8.2f:5.2f);
            if(!CityWater.UrbanSite(new(pole.X,pole.Z),.4))continue;
            pole = CityTerrainGeometry.Ground(pole);
            if (!(visibility?.Sphere(pole + Vec3.UnitY * 2, 3) ?? true)) continue;
            g.Material = SurfaceMaterial.MachinedMetal;
            g.Tube(pole, pole + Vec3.UnitY * 3.5f, .055f, Steel);
            g.Material = SurfaceMaterial.Rubber;
            g.Box(pole + Vec3.UnitY * 3.1f, new(.17f, .43f, .13f), Dark, r, Vec3.UnitY, f);
            bool green = m.Traffic.Green(i, j, d);
            double phase = CityTraffic.SignalPhase(m.Time, i, j);
            bool amber = CityTraffic.Amber(m.Time, i, j, d);
            for (int lamp = 0; lamp < 3; lamp++)
            {
                bool on = lamp == 0 ? !green && !amber : lamp == 1 ? amber : green;
                g.Material = on ? SurfaceMaterial.Emissive : SurfaceMaterial.Rubber;
                Vec3 color = lamp == 0 ? Red : lamp == 1 ? Gold : new(.07f, .9f, .20f);
                Vec3 lens = pole + Vec3.UnitY * (3.36f - .25f * lamp) - f * .145f;
                for (int arc = 0; arc < 12; arc++)
                {
                    float a = arc * MathF.Tau / 12, b = (arc + 1) * MathF.Tau / 12;
                    g.Triangle(lens, lens + (r * MathF.Cos(b) + Vec3.UnitY * MathF.Sin(b)) * .10f,
                        lens + (r * MathF.Cos(a) + Vec3.UnitY * MathF.Sin(a)) * .10f, color * (on ? 2 : .1f));
                }
            }
        }

        // Lamp heads within 115 m: the nine-by-nine block window around the player
        // (4 x 36 m > 115 m + a lamp's own offset) through the furniture buckets,
        // instead of a walk over all ~15 000 lamps and bins of the island per frame.
        double origin = CityTraffic.Coordinate(0);
        int ci = (int)Math.Floor((m.X - origin) / 36), cj = (int)Math.Floor((m.Z - origin) / 36);
        for (int bj = cj - 4; bj <= cj + 4; bj++)
        for (int bi = ci - 4; bi <= ci + 4; bi++)
        foreach (var lamp in CityLayout.Current.FurnitureInBlock(bi, bj))
            if (lamp.Height > 4 && Math.Abs(lamp.X - m.X) < 115 && Math.Abs(lamp.Z - m.Z) < 115 &&
                (visibility?.Sphere(new((float)lamp.X, 4 + (float)CityTerrain.Ground(lamp.X, lamp.Z), (float)lamp.Z),
                    2) ?? true))
            {
                float night = 1 - (float)m.Daylight;
                g.Material = night > .01f ? SurfaceMaterial.Emissive : SurfaceMaterial.Ceramic;
                g.Box(new((float)lamp.X + .9f, 4.055f + (float)CityTerrain.Ground(lamp.X, lamp.Z), (float)lamp.Z),
                    new(.29f, .012f, .14f), Vec3.Lerp(new(.65f, .68f, .64f), new(2.3f, 1.9f, 1.2f), night));
            }
    }

    private static void Animal(DioramaGeometry g, CityAnimal a)
    {
        Vec3 at = new((float)a.Position.X, (float)(CityTerrain.Ground(a.Position) + a.Height) + .16f,
            (float)a.Position.Z);
        g.Material = SurfaceMaterial.Textile;
        float h = a.Kind == AnimalKind.Bird ? .105f :
            a.Kind == AnimalKind.Pigeon ? .18f :
            a.Kind == AnimalKind.Cat ? .26f : .43f;
        g.Box(at + Vec3.UnitY * h, new(h * .55f, h * .6f, h), new(.35f, .32f, .28f));
    }

    private static void Wings(DioramaGeometry g, CityAnimal a, double time)
    {
        Vec3 o = new((float)a.Position.X, (float)(CityTerrain.Ground(a.Position) + a.Height) + .35f,
            (float)a.Position.Z);
        Vec3 f = new((float)Math.Sin(a.Heading), 0, (float)Math.Cos(a.Heading)), r = new(f.Z, 0, -f.X);
        float wing = a.Kind == AnimalKind.Bird ? .58f : 1;
        float beat = .45f * wing * MathF.Sin((float)time * 23 + a.Id);
        g.Material = SurfaceMaterial.Textile;
        for (int side = -1; side <= 1; side += 2)
            g.Triangle(o - f * .15f, o + r * (side * .50f * wing) + Vec3.UnitY * beat, o + f * .19f,
                new(.43f, .47f, .48f));
    }

    private static void Hull(DioramaGeometry g, Func<float, float, float, Vec3> p, Vec3 color)
    {
        (float Z, float W, float H)[] rings =
        [
            (-1.76f, .66f, .58f), (-1.57f, .83f, .76f), (-1.05f, .87f, .83f), (.72f, .87f, .83f), (1.48f, .81f, .73f),
            (1.72f, .66f, .58f)
        ];

        Vec3 Q(int i, int j)
        {
            var q = rings[i];
            float a = j * MathF.Tau / 12;
            return p(q.W * MathF.Cos(a), .52f + (q.H - .52f) * MathF.Sin(a), q.Z);
        }

        for (int i = 0; i < rings.Length - 1; i++)
        for (int j = 0; j < 12; j++)
            g.Quad(Q(i, j), Q(i + 1, j), Q(i + 1, j + 1), Q(i, j + 1), color);
        for (int j = 0; j < 12; j++)
        {
            g.Triangle(p(0, .52f, rings[0].Z), Q(0, j + 1), Q(0, j), color);
            g.Triangle(p(0, .52f, rings[^1].Z), Q(rings.Length - 1, j), Q(rings.Length - 1, j + 1), color);
        }
    }

    private static void Bus(DioramaGeometry g, CityCar car, Func<float, float, float, Vec3> p, Vec3 r, Vec3 up, Vec3 f)
    {
        g.BevelBoxes = true;
        g.Material = SurfaceMaterial.Enamel;
        g.Box(p(0, 1.55f, 0), new(1.09f, 1.08f, 4.48f), new(.71f, .66f, .42f), r, up, f);
        g.Box(p(0, 2.7f, 0), new(1.02f, .21f, 4.27f), new(.81f, .78f, .61f), r, up, f);
        g.Box(p(0, .86f, 0), new(1.12f, .15f, 4.45f), new(.38f, .21f, .14f), r, up, f);
        g.Material = SurfaceMaterial.ArchitecturalGlass;
        for (int side = -1; side <= 1; side += 2)
        for (int w = 0; w < 7; w++)
            g.Box(p(side * 1.105f, 2.03f, -3.65f + w * 1.17f), new(.012f, .46f, .48f), new(.10f, .19f, .22f), r, up, f);
        g.Box(p(0, 2.04f, 4.49f), new(.98f, .51f, .015f), new(.09f, .18f, .23f), r, up, f);
        g.Box(p(0, 2.04f, -4.49f), new(.98f, .48f, .015f), new(.09f, .18f, .23f), r, up, f);
        g.Material = SurfaceMaterial.Rubber;
        for (int door = 0; door < 2; door++)
        {
            float z = door == 0 ? 3.5f : -.6f;
            g.Box(p(-1.12f, 1.47f, z), new(.02f, .92f, .48f), Dark, r, up, f);
            g.Material = SurfaceMaterial.ArchitecturalGlass;
            g.Box(p(-1.145f, 1.90f, z), new(.015f, .42f, .40f), new(.13f, .24f, .25f), r, up, f);
            g.Material = SurfaceMaterial.Rubber;
        }

        for (int side = -1; side <= 1; side += 2)
        for (int axle = -1; axle <= 1; axle += 2)
        {
            Vec3 c = p(side * 1.1f, .49f, axle * 2.8f);
            g.Material = SurfaceMaterial.Rubber;
            g.Tube(c - r * .12f, c + r * .12f, .47f, Dark);
            g.Material = SurfaceMaterial.BrushedAluminium;
            g.Tube(c + r * side * .13f, c + r * side * .15f, .26f, Steel);
        }

        g.Material = SurfaceMaterial.Emissive;
        for (int side = -1; side <= 1; side += 2)
        {
            g.Box(p(side * .85f, .93f, 4.51f), new(.16f, .11f, .02f), new(1.2f, 1.1f, .8f), r, up, f);
            g.Box(p(side * .85f, .93f, -4.51f), new(.13f, .11f, .02f), Red * (car.Braking ? 3 : .6f), r, up, f);
        }

        g.BevelBoxes = false;
    }

    private static void Street(DioramaGeometry g, CityModel m, CityVisibility? visibility)
    {
        foreach (var p in m.Traffic.PropsInBox(g.Eye.X - 112, g.Eye.Z - 112, g.Eye.X + 112, g.Eye.Z + 112))
        {
            var at = p.Position;
            Vec3 o = new((float)at.X, (float)(CityTerrain.Ground(at) + p.Height) + .16f, (float)at.Z);
            if ((g.Eye - o).Length() > 110 || !(visibility?.Sphere(o + Vec3.UnitY, 2) ?? true)) continue;
            float a = (float)(p.Body?.Angle ?? 0), tilt = (float)p.Tilt;
            Vec3 right = new(MathF.Cos(a), 0, MathF.Sin(a)), f = new(-right.Z, 0, right.X);
            Vec3 up = Vec3.UnitY * MathF.Cos(tilt) + f * MathF.Sin(tilt);
            f = f * MathF.Cos(tilt) - Vec3.UnitY * MathF.Sin(tilt);
            o += Vec3.UnitY * (MathF.Abs(MathF.Sin(tilt)) * .27f);
            Vec3 P(float x, float y, float z) => o + right * x + up * y + f * z;
            switch (p.Kind)
            {
                case StreetKind.PhoneBooth:
                    g.Material = SurfaceMaterial.Enamel;
                    g.Box(P(0, 2.18f, 0), new(.65f, .11f, .65f), new(.25f, .42f, .46f), right, up, f);
                    for (int sx = -1; sx <= 1; sx += 2)
                    for (int sz = -1; sz <= 1; sz += 2)
                        g.Box(P(sx * .56f, 1.05f, sz * .56f), new(.035f, 1.05f, .035f), new(.28f, .40f, .38f), right,
                            up, f);
                    g.Material = SurfaceMaterial.ArchitecturalGlass;
                    for (int side = -1; side <= 1; side += 2)
                        g.Box(P(side * .56f, 1.15f, 0), new(.015f, .83f, .54f), new(.16f, .25f, .25f), right, up, f);
                    g.Material = SurfaceMaterial.Enamel;
                    g.Box(P(0, 1.30f, -.43f), new(.23f, .34f, .12f), new(.25f, .31f, .30f), right, up, f);
                    g.Material = SurfaceMaterial.Rubber;
                    g.Tube(P(-.14f, 1.25f, -.27f), P(-.14f, 1.57f, -.27f), .045f, Dark);
                    g.Tube(P(-.14f, 1.26f, -.27f), P(-.18f, .87f, -.3f), .013f, Dark);
                    g.Material = SurfaceMaterial.BrushedAluminium;
                    g.Box(P(.08f, 1.38f, -.30f), new(.09f, .10f, .012f), Steel, right, up, f);
                    break;
                case StreetKind.DrinkMachine:
                    g.Material = SurfaceMaterial.Enamel;
                    g.Box(P(0, .90f, 0), new(.46f, .9f, .42f), new(.24f, .40f, .39f), right, up, f);
                    g.Box(P(0, 1.40f, .43f), new(.39f, .26f, .018f), new(.74f, .70f, .56f), right, up, f);
                    g.Material = SurfaceMaterial.BrushedAluminium;
                    g.Box(P(0, .70f, .44f), new(.27f, .25f, .02f), Steel, right, up, f);
                    g.Material = SurfaceMaterial.Rubber;
                    g.Box(P(0, .72f, .47f), new(.20f, .19f, .01f), Dark, right, up, f);
                    g.Material = SurfaceMaterial.ArchitecturalGlass;
                    g.Tube(P(0, .56f, .5f), P(0, .73f, .5f), .07f, new(.57f, .67f, .65f));
                    break;
                case StreetKind.Mailbox:
                    g.Material = SurfaceMaterial.CastMetal;
                    g.Tube(P(0, 0, 0), P(0, .8f, 0), .065f, Steel);
                    g.Material = SurfaceMaterial.Enamel;
                    g.Box(P(0, 1.08f, 0), new(.32f, .38f, .21f), new(.15f, .27f, .40f), right, up, f);
                    g.Material = SurfaceMaterial.Rubber;
                    g.Box(P(0, 1.28f, .215f), new(.23f, .025f, .008f), Dark, right, up, f);
                    break;
                case StreetKind.Dumpster:
                    g.Material = (SurfaceMaterial)230;
                    g.Box(P(0, .62f, 0), new(.75f, .57f, .55f), new(1, 1, 1), right, up, f);
                    g.Box(P(0, 1.20f, 0), new(.79f, .055f, .59f), new(1, 1, 1), right, up, f);
                    g.Material = SurfaceMaterial.CastMetal;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        g.Tube(P(side * .80f, .8f, -.25f), P(side * .80f, .8f, .25f), .035f, Steel);
                        g.Box(P(side * .55f, .05f, 0), new(.07f, .08f, .45f), Dark, right, up, f);
                    }

                    break;
                case StreetKind.Bin:
                    g.Material = SurfaceMaterial.Enamel;
                    g.Tube(P(0, .02f, 0), P(0, .73f, 0), .28f, new(.16f, .29f, .22f));
                    g.Material = SurfaceMaterial.Rubber;
                    g.Tube(P(0, .74f, 0), P(0, .77f, 0), .255f, Dark);
                    g.Material = SurfaceMaterial.CastMetal;
                    for (int n = 0; n < 8; n++)
                    {
                        float ang = n * MathF.Tau / 8;
                        g.Tube(P(.28f * MathF.Cos(ang), .06f, .28f * MathF.Sin(ang)),
                            P(.28f * MathF.Cos(ang), .70f, .28f * MathF.Sin(ang)), .015f, Steel);
                    }

                    break;
                case StreetKind.Hydrant:
                    g.Material = SurfaceMaterial.Enamel;
                    g.Tube(P(0, .02f, 0), P(0, .65f, 0), .15f, new(.60f, .16f, .09f));
                    g.Ellipsoid(P(0, .66f, 0), new(.17f, .11f, .17f), Red);
                    g.Tube(P(-.3f, .42f, 0), P(.3f, .42f, 0), .10f, Red);
                    g.Material = SurfaceMaterial.Brass;
                    g.Box(P(0, .80f, 0), new(.06f, .04f, .06f), Gold, right, up, f);
                    break;
                case StreetKind.Bench:
                    g.Material = SurfaceMaterial.CastMetal;
                    for (int side = -1; side <= 1; side += 2)
                    {
                        g.Tube(P(side * .8f, 0, 0), P(side * .8f, .95f, -.2f), .035f, Dark);
                    }

                    g.Material = SurfaceMaterial.Wood;
                    for (int n = 0; n < 4; n++)
                        g.Box(P(0, .48f, -.20f + n * .13f), new(1, .035f, .05f), new(.48f, .32f, .16f), right, up, f);
                    for (int n = 0; n < 3; n++)
                        g.Box(P(0, .67f + n * .14f, -.24f), new(1, .05f, .03f), new(.48f, .32f, .16f), right, up, f);
                    break;
                case StreetKind.GarbageBag:
                    g.Material=SurfaceMaterial.Polymer;
                    g.Ellipsoid(P(0,.25f,0),new(.28f,.25f,.23f),new(.035f,.045f,.040f));
                    g.Ellipsoid(P(.025f,.47f,0),new(.09f,.06f,.075f),new(.055f,.06f,.045f));
                    g.Tube(P(.02f,.50f,0),P(.07f,.58f,.03f),.025f,Dark);
                    for(int n=0;n<7;n++){float angleBag=n*MathF.Tau/7;g.Tube(P(.24f*MathF.Cos(angleBag),.16f,.20f*MathF.Sin(angleBag)),P(.07f*MathF.Cos(angleBag),.45f,.06f*MathF.Sin(angleBag)),.008f,new(.07f,.075f,.065f));}
                    break;
                case StreetKind.Bag:
                    g.Material=SurfaceMaterial.Polymer;
                    for(int side=-1;side<=1;side+=2)for(int n=0;n<6;n++){
                        float angleBag=-.16f+n*.055f,b=angleBag+.055f,wa=MathF.Sin(n*2.1f+p.Id)*.02f,wb=MathF.Sin((n+1)*2.1f+p.Id)*.02f;
                        g.Quad(P(angleBag,.02f,side*.04f+wa),P(b,.02f,side*.04f+wb),P(b,.25f,side*.025f+wb),P(angleBag,.25f,side*.025f+wa),new(.72f,.75f,.66f));
                    }
                    foreach(int side in new[]{-1,1})for(int n=0;n<8;n++){
                        float angleBag=n*MathF.PI/8,b=(n+1)*MathF.PI/8;
                        g.Tube(P(side*.08f+.045f*MathF.Cos(angleBag),.24f+.095f*MathF.Sin(angleBag),0),P(side*.08f+.045f*MathF.Cos(b),.24f+.095f*MathF.Sin(b),0),.009f,White);
                    }
                    break;
                case StreetKind.Can:
                    g.Material = SurfaceMaterial.BrushedAluminium;
                    g.Tube(P(-.055f, .035f, 0), P(.055f, .035f, 0), .034f, new(.64f, .65f, .59f));
                    g.Material = SurfaceMaterial.Enamel;
                    g.Tube(P(-.035f, .035f, 0), P(.035f, .035f, 0), .035f,
                        p.Id % 2 == 0 ? new(.49f, .14f, .08f) : new(.19f, .31f, .22f));
                    break;
                case StreetKind.Bottle:
                    g.Material = SurfaceMaterial.ArchitecturalGlass;
                    g.Tube(P(-.09f, .04f, 0), P(.07f, .04f, 0), .045f, new(.12f, .27f, .13f));
                    g.Tube(P(.07f, .04f, 0), P(.13f, .04f, 0), .025f, Gold);
                    break;
                default:
                    g.Material = SurfaceMaterial.Paper;
                    g.Box(P(0, .035f, 0), new(.055f, .035f, .085f), new(.77f, .73f, .59f), right, up, f);
                    g.Material = SurfaceMaterial.Enamel;
                    g.Box(P(0, .072f, .04f), new(.05f, .003f, .035f), Red, right, up, f);
                    break;
            }
        }
    }
}
