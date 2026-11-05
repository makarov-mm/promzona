namespace Promzona.Game;

internal static class CityVehicleLod
{
    internal static (int First,int Second,float Blend) Select(float distance)
    {
        if(distance<35)return(0,0,0);
        if(distance<41)return(0,1,(distance-35)/6);
        if(distance<87)return(1,1,0);
        if(distance<103)return(1,2,(distance-87)/16);
        return(2,2,0);
    }
}
