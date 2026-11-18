using System.Diagnostics;
using ETS2LA.State;
namespace MautAssistent;

public sealed partial class TollPlugin
{
    bool FocusLaneLeaseCurrent(long epoch)
    {
        if(!monitorSteering||State.DrivingMode==DrivingMode.AdaptiveCruiseControlOnly)return true;
        var command=Volatile.Read(ref lastSteeringCommand);long now=Stopwatch.GetTimestamp();
        return command?.Epoch==epoch&&command.LeaseSeconds is double lease&&double.IsFinite(lease)&&lease>0&&
            command.IssuedTicks is long issued&&issued>0&&now>=issued&&now-issued<=Math.Min(.1,lease)*Stopwatch.Frequency&&
            command.ReceivedTicks is long received&&received>0&&now>=received&&now-received<=Math.Min(.1,lease)*Stopwatch.Frequency;
    }
    PedalFocusSample? ReadPedalFocus(TelemetryFrame? frame,bool routeReady,long epoch,long? knownSourceEpoch=null)
    {
        long sourceEpoch=knownSourceEpoch??CaptureSourceEpoch();
        var context=ReadInputContext();
        if(frame==null||!frame.Active||frame.Paused||context.ProcessIdentity==null||
            context.GameForeground is not bool foreground)return null;
        // Steering and route readiness are needed for returning propulsion,
        // not evidence that a window was actually in the background.
        bool laneReady=FocusLaneLeaseCurrent(epoch);
        long now=Stopwatch.GetTimestamp();
        var sample=new PedalFocusSample(now,Stopwatch.Frequency,context.CapturedAt,frame.Origin,
            sourceEpoch,context.GamePid,context.ProcessIdentity,foreground,frame.Time,frame.Physics.SimulatedTime,
            routeReady&&laneReady&&!brakeHeld&&!faultBrake.Latched&&!frame.Physics.ParkingBrake);
        return PedalFocusRecovery.Valid(sample)?sample:null;
    }
}
