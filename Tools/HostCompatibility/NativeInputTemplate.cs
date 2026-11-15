using ETS2LA.Game.Telemetry;
using PathLib;
namespace MautHostCompatibility;
internal static class NativeInputTemplate
{
    public static bool MautInputsKnown(GameTelemetryData? telemetry,PlannedPathData? path)
    {
        if(telemetry==null||!telemetry.sdkActive||telemetry.paused||path==null||path.HasError||path.PlannedPath.Count==0)return false;
        if(!OutputTemplate.MautNativePathKnown(path))return false;
        try {
            float factor=path.GetFactorForPoint(telemetry.truckPlacement.coordinate.ToVector3());
            return float.IsFinite(factor)&&factor>=0&&factor<=1;
        } catch {return false;}
    }
}
