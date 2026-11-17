namespace MautAssistent;

// Shared interpretation of signed simulator speed around a confirmed stand.
// This pure policy grants no stationary hold, route or source authority.
public static class ForwardMotion
{
    public const double StationarySpeed=.1;
    public const double EnterStationarySpeed=.05;
    public static bool TryNormalizeForwardSpeed(double raw,out double normalized)
    {
        normalized=0;
        if(!double.IsFinite(raw)||raw < -StationarySpeed)return false;
        normalized=Math.Max(0,raw);return true;
    }
    public static bool IsStationary(double raw)=>double.IsFinite(raw)&&Math.Abs(raw)<=StationarySpeed;
}
