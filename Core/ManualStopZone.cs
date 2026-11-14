namespace MautAssistent;

/// <summary>Model-space trigger interval behind a gate, not a measured bumper clearance.</summary>
public sealed record ManualStopZone(double MinimumOffset,double MaximumOffset)
{
    public double? Choose(double minimumSafeOffset)
    {
        if(!double.IsFinite(MinimumOffset)||!double.IsFinite(MaximumOffset)||!double.IsFinite(minimumSafeOffset)||
            minimumSafeOffset<4||MinimumOffset>MaximumOffset||MaximumOffset>50)return null;
        double low=Math.Max(MinimumOffset,minimumSafeOffset);
        if(low>MaximumOffset)return null;
        return Math.Clamp((MinimumOffset+MaximumOffset)/2,low,MaximumOffset);
    }
}
