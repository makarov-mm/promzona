namespace MakarovPhysics.Numerics;
/// <summary>Idealised lower-atmosphere parcel relations, SI units. Water saturation only; no ice microphysics.</summary>
public static class AtmosphericParcel
{
 public const double Gravity=9.81,Cp=1005,Rd=287.05,Epsilon=.622,Latent=2.5e6;
 public static double Pressure(double altitude)=>101325*Math.Exp(-Math.Clamp(altitude,-500,12000)/8434);
 public static double SaturationPressure(double kelvin){double c=Math.Clamp(kelvin-273.15,-70,60);return 610.94*Math.Exp(17.625*c/(c+243.04));}
 public static double SaturationMixingRatio(double kelvin,double pressure){double e=Math.Min(pressure*.8,SaturationPressure(kelvin));return Epsilon*e/(pressure-e);}
 public static double RelativeHumidity(double q,double kelvin,double pressure){double e=q*pressure/(Epsilon+q);return e/SaturationPressure(kelvin);}
 public static double Lapse(double kelvin,double pressure,bool saturated){if(!saturated)return Gravity/Cp;double q=SaturationMixingRatio(kelvin,pressure);return Gravity*(1+Latent*q/(Rd*kelvin))/(Cp+Latent*Latent*q*Epsilon/(Rd*kelvin*kelvin));}
}
