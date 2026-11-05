using MakarovPhysics.Renderer;
namespace Promzona.Game;

internal static class CityLighting
{
    internal static Texture? TerrainMap;
    // Render-thread state is refreshed by FrameCamera for every scene, including after load.
    internal static CityAtmosphere Atmosphere = CityAtmosphere.Create(0,1,1,0);

    public static void Apply(OutdoorLighting lighting, Shader s)
    {
        s.Use();
        Atmosphere.Apply(s);
        s.SetInt("uTerrainMap", 9);
        s.SetInt("uHasTerrain", TerrainMap is null ? 0 : 1);
        TerrainMap?.Bind(9);
        s.SetFloat("uTerrainOrigin", (float)CityTerrain.Current.Origin);
        s.SetFloat("uTerrainSize", CityTerrain.Current.Size);
        int count=0;
        foreach(var tunnel in CityLayout.Current.Levels)if(tunnel.Tunnel&&count<8){
            s.SetVec3(TunnelPosition[count],new Vec3((float)tunnel.X,(float)(tunnel.Z+18),54));
            s.SetVec3(TunnelFloor[count],new Vec3((float)tunnel.CentreFloor(tunnel.X),(float)tunnel.FloorSlope,0));count++;
        }
        s.SetInt("uCityTunnelCount",count);
        s.SetFloat("uSurfaceWetness", CitySurface.Wetness);
        s.SetFloat("uDaylight", (float)lighting.Daylight);
        s.SetFloat("uStreetSpacing", lighting.StreetSpacing);
        s.SetFloat("uGridLow", lighting.GridLow);
        s.SetFloat("uGridHigh", lighting.GridHigh);
        s.SetFloat("uLampSide", CityLayout.WideRoads?8.2f:5.15f);

        for (int i = 0; i < 12; i++)
        {
            s.SetVec3(LampPosition[i], lighting.Positions[i]);
            s.SetVec3(LampColor[i], lighting.Colors[i]);
            s.SetVec3(LampDirection[i], lighting.Directions[i]);
        }
    }

    // Uniform names are fixed; building them per draw allocated ~36 strings for
    // every shader bound in a frame.
    private static readonly string[] LampPosition = Names("uLampPosition"), LampColor = Names("uLampColor"), LampDirection = Names("uLampDirection");
    private static readonly string[] TunnelPosition=Names("uCityTunnels"),TunnelFloor=Names("uCityTunnelFloors");
    private static string[] Names(string array) => [.. Enumerable.Range(0, 12).Select(i => $"{array}[{i}]")];
}
