namespace MautAssistent;

public readonly record struct BrakeResponseFrame(double Arrived,ulong GameTime,ulong SimulationTime,bool Enabled,bool Active,
    bool Paused,double Speed,double CommandAge,float RequestedBrake,float GameBrake);
public sealed record BrakeResponseFailure(BrakeResponseFrame Sample,double MissingFor);

/// <summary>Diagnostic evidence of requested braking absent from the game's input telemetry.</summary>
public sealed class BrakeResponseGuard
{
    BrakeResponseFrame? previous;
    double? suspectSince;
    public BrakeResponseFailure? Failure {get;private set;}
    public void Reset(){previous=null;suspectSince=null;Failure=null;}
    public BrakeResponseFailure? Observe(BrakeResponseFrame sample)
    {
        if(!sample.Enabled){Reset();return null;}
        if(Failure!=null)return null;
        if(!sample.Active||sample.Paused||!double.IsFinite(sample.Arrived)||!double.IsFinite(sample.Speed)||
            !double.IsFinite(sample.CommandAge)||sample.CommandAge is <0 or >.15||
            !float.IsFinite(sample.RequestedBrake)||!float.IsFinite(sample.GameBrake)||
            sample.RequestedBrake is <0 or >1||sample.GameBrake is <0 or >1) {previous=null;suspectSince=null;return null;}
        if(previous is { } duplicate&&sample.GameTime==duplicate.GameTime)return null;
        if(previous is { } prior && (sample.Arrived-prior.Arrived is <=0 or >.25||sample.GameTime<=prior.GameTime||
            sample.SimulationTime<=prior.SimulationTime||sample.GameTime-prior.GameTime>250000||sample.SimulationTime-prior.SimulationTime>250000))suspectSince=null;
        previous=sample;
        if(sample.RequestedBrake<.2f||sample.GameBrake>.02f||sample.Speed<2) {suspectSince=null;return null;}
        suspectSince??=sample.Arrived;
        if(sample.Arrived-suspectSince.Value<1)return null;
        Failure=new(sample,sample.Arrived-suspectSince.Value);return Failure;
    }
}
