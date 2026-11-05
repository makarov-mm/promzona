namespace Promzona.Game;

/// <summary>Actor identity selects the same model, mass and dimensions at every LOD
/// and after save/load or carjacking. Emergency vehicles retain their liveries.</summary>
public static class CityFleet
{
    public readonly record struct Vehicle(string Model,double Width,double Height,double Length,double Mass,double Wheelbase);
    private static readonly Vehicle[] Sedans=[
        new("luaz",1.72,1.81,3.42,970,1.8),new("azlh-2140_sl_moskvich",1.60,1.44,4.17,1080,2.4),
        new("u469",1.92,1.92,4.02,1650,2.38),new("ukrainer_79_-_low_poly_model",1.51,1.32,3.75,840,2.16),
        new("1970_soviet_car_vaz_2101",1.63,1.33,4.17,955,2.42)];
    public static Vehicle? Get(CityCar car)
    {
        if(car.Kind==3)return new("militia_sedan",1.82,1.65,4.65,1600,2.70);
        if(car.Kind==6)return new("militia_sedan",1.82,1.65,4.65,1700,2.70);
        if(car.Kind==7)return new("rosguard_van",2.24,2.85,5.85,4200,3.65);
        if(car.Kind==13)return new("fsb_sedan",1.88,1.57,4.95,1900,2.90);
        if(car.Kind==14)return new("army_btr",2.90,2.65,7.30,13600,4.30);
        if(car.Kind==11)return new("promzona_tank",3.35,2.45,6.3,32000,3.7);
        if(car.Kind==12)return new("promzona_helicopter",2.4,3.1,8.6,2100,3.2);
        if(car.Kind==9)return new("free_soviet_tram_x-series",2.533,4.061,10.4,17000,3.8);
        if(car.Kind==10)return new("ziu_9",2.962,3.36,11.9,10500,6);
        if(car.Id<=1)return null; // Unassigned actors and the existing articulated starter car.
        if(car.Kind==0&&car.Id%7==5)return new("old_car_-_vaz_2107",1.78,1.48,4.16,1060,2.42);
        if(car.Kind==0&&car.Id%7<5)return Sedans[Math.Abs(car.Id%7)];
        if(car.Kind==1)return car.Id%2==0?new("free_zuk_3d_model",2.03,2.05,4.31,1850,2.7):new("pirozhok_scan_retopo_free_scan",1.77,1.78,4.1,1200,2.4);
        if(car.Kind==2)return car.Id%2==0?new("gaz-52_truck_old_version",2.93,2.41,6.4,6500,3.7):new("maz_500",3.14,3.21,6.67,7400,3.9);
        return null;
    }
    public static string? Model(CityCar car)=>Get(car)?.Model;
}
