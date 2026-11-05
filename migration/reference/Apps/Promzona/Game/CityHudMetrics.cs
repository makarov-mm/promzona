namespace Promzona.Game;

public sealed class CityFrameRate
{
    private double _seconds;private int _frames;
    public double FramesPerSecond {get;private set;}
    public double Milliseconds=>FramesPerSecond>0?1000/FramesPerSecond:0;
    public void Sample(double elapsed){
        if(!double.IsFinite(elapsed)||elapsed<=0||elapsed>2){_seconds=0;_frames=0;return;}
        _seconds+=elapsed;_frames++;if(_seconds>=.5){FramesPerSecond=_frames/_seconds;_seconds=0;_frames=0;}
    }
}
public static class CityMapProjection
{
    // +Z is north. Looking north, camera-right is -X (the engine's view basis).
    // The old +X horizontal projection mirrored every relative left/right turn.
    public static CityPoint NorthUp(CityPoint delta)=>new(-delta.X,-delta.Z);
}
