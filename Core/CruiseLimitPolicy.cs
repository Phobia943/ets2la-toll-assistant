namespace MautAssistent;

public readonly record struct CruiseLimits(double MaximumSpeed,bool HonorRoadLimit);

public static class CruiseLimitPolicy
{
    // Metres/second. Zero host maximum means no additional configured cap;
    // unknown road limits cannot authorize a higher automatic preset.
    public static double? Preferred(double requested,CruiseLimits host,double roadLimit)
    {
        if(!double.IsFinite(requested)||requested<=0||!double.IsFinite(host.MaximumSpeed)||host.MaximumSpeed<0)return null;
        double target=host.MaximumSpeed>0?Math.Min(requested,host.MaximumSpeed):requested;
        if(host.HonorRoadLimit) {
            if(!double.IsFinite(roadLimit)||roadLimit<=0)return null;
            target=Math.Min(target,roadLimit);
        }
        return target;
    }
}
