namespace MakarovPhysics.Numerics;
/// <summary>IAPWS SR1-86(1992), equation 1. Input kelvin, output MPa; triple to critical point.</summary>
public static class WaterSaturation
{
    public static double PressureMPa(double temperature)
    {
        double t=Math.Clamp(temperature,273.16,647.096),v=1-t/647.096;
        return 22.064*Math.Exp(647.096/t*(-7.85951783*v+1.84408259*Math.Pow(v,1.5)-11.7866497*Math.Pow(v,3)+22.6807411*Math.Pow(v,3.5)-15.9618719*Math.Pow(v,4)+1.80122502*Math.Pow(v,7.5)));
    }
}
