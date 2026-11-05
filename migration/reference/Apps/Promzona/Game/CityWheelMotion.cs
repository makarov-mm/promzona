namespace Promzona.Game;

/// <summary>Signed wheel travel. Rendering a door, shadow or second LOD cannot
/// advance it twice. Weak actor ownership prevents stale state after despawning.</summary>
internal sealed class CityWheelMotion
{
    private double _distance=double.NaN,_travel;
    internal double Sample(double distance,double speed)
    {
        if(!double.IsFinite(distance)||!double.IsFinite(speed))return _travel;
        double delta=distance-_distance;
        if(double.IsFinite(_distance)&&delta>=0&&delta<20)_travel+=delta*Math.Sign(speed);
        _distance=distance;
        return _travel;
    }
}
