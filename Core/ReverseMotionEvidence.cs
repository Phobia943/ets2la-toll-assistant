using System.Numerics;
namespace MautAssistent;

public readonly record struct DirectionSample(ulong Stamp,double Time,long Epoch,Vector3 Position,double Heading,double Speed,bool Valid);

// Near-zero signed speed is noisy. Accumulate net directed position, not the
// sum of negative jitter steps. Faster reverse travel is checked separately.
public sealed class ReverseMotionEvidence
{
    DirectionSample? previous;
    double debt;
    double? began;
    int backwardSamples;
    Vector3 anchor;
    double anchorAt;
    int samples;
    double? stableSince;
    Vector3 stableAnchor;
    public bool Confirmed {get;private set;}
    public bool StationaryDisplaced {get;private set;}
    public double BackwardMetres=>debt;
    public void Reset(){previous=null;debt=0;began=stableSince=null;backwardSamples=samples=0;Confirmed=StationaryDisplaced=false;}
    void Begin(DirectionSample sample){previous=sample;anchor=sample.Position;anchorAt=sample.Time;samples=1;}
    public bool Observe(DirectionSample current)
    {
        if(!current.Valid||current.Stamp==0||current.Epoch<=0||!double.IsFinite(current.Time)||
            !double.IsFinite(current.Heading)||!double.IsFinite(current.Speed)||!float.IsFinite(current.Position.LengthSquared())) {
            Reset();return false;
        }
        if(previous is not { } prior){Begin(current);return false;}
        if(current.Stamp==prior.Stamp&&current.Epoch==prior.Epoch)return Confirmed;
        double elapsed=current.Time-prior.Time;
        Vector3 forward=TrackingGuard.TruckForward(prior.Heading),delta=current.Position-prior.Position;
        if(current.Epoch!=prior.Epoch||current.Stamp<prior.Stamp||elapsed is <=0 or >.25||
            Vector3.Dot(forward,TrackingGuard.TruckForward(current.Heading))<.995f||delta.Length()>.25f||
            !ForwardMotion.IsStationary(current.Speed)) {
            Reset();Begin(current);return false;
        }
        previous=current;
        samples++;
        var offset=current.Position-anchor;offset.Y=0;
        if(offset.Length()>=.05f&&current.Time-anchorAt>=.4&&samples>=3)StationaryDisplaced=true;
        if(StationaryDisplaced&&Math.Abs(current.Speed)<=.01&&delta.Length()<=.003f) {
            if(stableSince==null){stableSince=prior.Time;stableAnchor=prior.Position;}
            var stableOffset=current.Position-stableAnchor;stableOffset.Y=0;
            if(stableOffset.Length()>.003f){stableSince=current.Time;stableAnchor=current.Position;}
            if(current.Time-stableSince.Value>=.5){anchor=current.Position;anchorAt=current.Time;samples=1;StationaryDisplaced=false;stableSince=null;}
        } else stableSince=null;
        double advance=Vector3.Dot(delta,forward);
        debt=Math.Max(0,debt-advance);
        if(debt<=.002){began=null;backwardSamples=0;Confirmed=false;}
        else if(advance<-.0001&&current.Speed<-.001) {began??=prior.Time;backwardSamples++;}
        Confirmed=debt>=.05&&began is double start&&current.Time-start>=.4&&backwardSamples>=3&&current.Speed<-.005;
        return Confirmed;
    }
}
