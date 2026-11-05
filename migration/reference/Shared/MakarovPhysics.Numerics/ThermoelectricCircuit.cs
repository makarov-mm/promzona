namespace MakarovPhysics.Numerics;

/// <summary>Constant-property thermoelectric couple between prescribed reservoirs.
/// Kelvin relation Π=αT; conduction and half the Joule heat at each junction.</summary>
public class ThermoelectricCircuit
{
    public int Mode;
    public double Cold = 290, Delta = 60, Seebeck = .05, Resistance = 2, Conductance = .6, Load = 2, SupplyCurrent = 2, Time;
    public double Hot => Cold + Delta;
    public double Current => Mode == 0 ? Seebeck * Delta / (Resistance + Load) : SupplyCurrent;
    public double Voltage => Mode == 0 ? Current * Load : Seebeck * Delta + Resistance * Current;
    public double Power => Voltage * Current;
    public double ColdHeat => Mode == 0 ? Seebeck * Cold * Current + .5 * Resistance * Current * Current + Conductance * Delta : Seebeck * Cold * Current - .5 * Resistance * Current * Current - Conductance * Delta;
    public double HotHeat => Mode == 0 ? ColdHeat + Power : ColdHeat + Power;
    public double ZT => Seebeck * Seebeck * (Hot + Cold) * .5 / (Resistance * Conductance);
    public double Performance => Mode == 0 ? (HotHeat > 0 ? Power / HotHeat : 0) : (Power > 0 ? ColdHeat / Power : 0);

    public void Reset()
    {
        Time = 0;
    }

    public void Step(double dt)
    {
        Time += dt;
    }
}
