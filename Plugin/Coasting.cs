using ETS2LA.Game.Output;
using ETS2LA.State;
namespace MautAssistent;
public sealed partial class TollPlugin
{
    bool CanCoast()
    {
        if(!Monitor.TryEnter(sync))return false;
        try {
            double now=clock.Elapsed.TotalSeconds;
            if(!options.CoastOnOpenRoads||!options.CurvePreviewEnabled||!_IsRunning||!active||preview||
                !State.EnableAssists||State.DrivingMode!=DrivingMode.FullSelfDriving||faultBrake.Latched||
                brakeHeld||now<takeoverUntil||driveAction!=DriveAction.Drive||!SensorsFresh(now)||now-pathAt>.2||
                controller.Tracking||router.StopBoundary.HasValue||router.HasShortChange||sceneStopping||router.ClearanceHoldRequested||
                safetyTrace is not {IdentityKnown:true,EndsAtGap:false} trace||
                candidate is { } gate&&gate.Distance<=controller.ApproachHorizon(speed))return false;
            double required=RequiredCurveCoverage;
            var profile=curvePreview.Read(trace,position,required);
            return profile?.HasFullCoverage(trace,position,required)==true&&!profile.HasSlowerCurve(position,speed,required);
        } finally {Monitor.Exit(sync);}
    }
    void ConnectCoasting()
    {
        // Only the optional, exactly verified output adapter exposes this field.
        typeof(GameOutput).GetField("MautCoastPermission")?.SetValue(null,(Func<bool>)CanCoast);
        typeof(GameOutput).GetField("MautNativePathPermission")?.SetValue(null,(Func<object,bool>)NativeCoastPathKnown);
    }
    bool NativeCoastPathKnown(object evaluated)
    {
        if(!Monitor.TryEnter(sync))return false;
        try {return ReferenceEquals(path,evaluated)&&path is {HasError:false}&&clock.Elapsed.TotalSeconds-pathAt<=.2;}
        finally {Monitor.Exit(sync);}
    }
    void DisconnectCoasting()
    {
        var field=typeof(GameOutput).GetField("MautCoastPermission");
        if(field?.GetValue(null) is Func<bool> callback&&ReferenceEquals(callback.Target,this))field.SetValue(null,null);
        var pathField=typeof(GameOutput).GetField("MautNativePathPermission");
        if(pathField?.GetValue(null) is Func<object,bool> pathCallback&&ReferenceEquals(pathCallback.Target,this))pathField.SetValue(null,null);
    }
}
