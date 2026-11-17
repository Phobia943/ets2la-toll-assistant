using System.Numerics;

namespace MautAssistent;

/// <summary>Interior of one verified model-local activation rectangle. The
/// forward axis belongs to the directed traffic curve, not the marker's sign.</summary>
public sealed record ManualTriggerZone(Vector3 Center,Vector3 Forward,double HalfLength,double HalfWidth);

/// <summary>Model-space trigger interval behind a gate, not a measured bumper clearance.</summary>
public sealed record ManualStopZone(double MinimumOffset,double MaximumOffset)
{
    public ManualTriggerZone? Trigger {get;init;}
    public double? Choose(double minimumSafeOffset)
    {
        if(!double.IsFinite(MinimumOffset)||!double.IsFinite(MaximumOffset)||!double.IsFinite(minimumSafeOffset)||
            minimumSafeOffset<4||MinimumOffset>MaximumOffset||MaximumOffset>50)return null;
        double low=Math.Max(MinimumOffset,minimumSafeOffset);
        if(low>MaximumOffset)return null;
        return Math.Clamp((MinimumOffset+MaximumOffset)/2,low,MaximumOffset);
    }
}
