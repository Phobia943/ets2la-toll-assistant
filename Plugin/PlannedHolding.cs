using System.Reflection;
using ETS2LA.Game.Output;
using ETS2LA.State;
using PathLib;
namespace MautAssistent;
public sealed partial class TollPlugin
{
    readonly PlannedHoldBrake holdBrake=new();
    static readonly MethodInfo? setPlannedHold=typeof(GameOutput).GetMethod("MautSetPlannedHold");
    static readonly MethodInfo? captureOutputEpoch=typeof(GameOutput).GetMethod("MautCaptureEpoch");
    float holdPedalFloor;
    float scenePedalFloor;
    long holdGeneration=-1;
    TollDecision EvaluatePlannedStop(PlannedPathData data)
    {
        double remaining=router.UpdateStopDistance(data,position,speed)!.Value;
        var result=approachStop.Step(router.StopKey!,speed,remaining);
        double now=clock.Elapsed.TotalSeconds;
        bool permitted=faultBrake.OperationCurrent&&_IsRunning&&active&&!preview&&State.EnableAssists&&State.DrivingMode!=DrivingMode.LaneAssistOnly&&
            !faultBrake.Latched&&!brakeHeld&&!paused&&now>=takeoverUntil&&SensorsFresh(now)&&now-pathAt<.2&&
            approachStop.Holding&&approachStop.StopKey==router.StopKey;
        holdPedalFloor=holdBrake.Step(router.StopKey!,now,speed,remaining,permitted);
        return result;
    }
    void PublishPlannedHold(long epoch,string? key,float floor,long issuedAt)
    {
        if(setPlannedHold==null||setPlannedHold.GetParameters().Length!=4||captureOutputEpoch==null)return;
        faultBrake.RunCurrent(()=>setPlannedHold.Invoke(null,[epoch,key,floor,issuedAt]));
    }
    void CommitPlannedHolds(long epoch,long issuedAt)
    {
        double now=clock.Elapsed.TotalSeconds;
        float floor=_IsRunning&&active&&!preview&&State.EnableAssists&&!faultBrake.Latched&&!brakeHeld&&
            !paused&&now>=takeoverUntil&&driveAction==DriveAction.Drive&&SensorsFresh(now)&&now-pathAt<.2&&
            State.DrivingMode!=DrivingMode.LaneAssistOnly?Math.Max(holdPedalFloor,scenePedalFloor):0;
        PublishPlannedHold(epoch,floor>0?"planned-combined":null,floor,issuedAt);
    }
    void ClearPlannedHold()
    {
        long epoch=captureOutputEpoch==null?0:(long)captureOutputEpoch.Invoke(null,null)!;
        holdBrake.Reset();holdPedalFloor=scenePedalFloor=0;sceneHold.Reset();sceneStopping=false;holdGeneration=-1;
        PublishPlannedHold(epoch,null,0,System.Diagnostics.Stopwatch.GetTimestamp());
    }
}
