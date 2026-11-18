using System.Diagnostics;
using ETS2LA.State;
namespace MautAssistent;
public sealed partial class TollPlugin
{
    readonly SteeringFeedGuard pedalFeedGuard=new();
    internal sealed record PedalFailure(DateTimeOffset Utc,double? CommandAge,long? IssuedAt,long? Epoch,string Reason) {
        public ControlTickObservation? Producer {get;init;}
    }
    PedalFailure? pendingPedalFailure,lastPedalFailure;
    bool PedalRequired()=>steeringMonitoringLive&&_IsRunning&&Volatile.Read(ref active)&&State.EnableAssists&&
        !Volatile.Read(ref preview)&&State.DrivingMode!=DrivingMode.LaneAssistOnly&&
        !(Volatile.Read(ref latestDeliveredTelemetry)?.Paused??Volatile.Read(ref paused));
    double? PedalAge(PedalCommand? command)
    {
        bool current=command!=null&&(!requireHostCompatibility||command.Epoch is long epoch&&CaptureLongitudinalEpoch()==epoch&&PedalSourcesCurrent(command));
        return current?CommandAge(command!.IssuedTicks,command.At,Stopwatch.GetTimestamp(),clock.Elapsed.TotalSeconds):null;
    }
    double? PedalProducerAge(PedalCommand? command)
    {
        // A source revoke removes physical gas immediately in the host. It is
        // not itself a failed producer Tick. Keep aging the LAST ACCEPTED work
        // origin while the next normal Tick replans; never reset this 150 ms
        // deadline or refresh it from a rejected event.
        bool current=command!=null&&(!requireHostCompatibility||command.Epoch is long epoch&&CaptureLongitudinalEpoch()==epoch);
        return current?CommandAge(command!.IssuedTicks,command.At,Stopwatch.GetTimestamp(),clock.Elapsed.TotalSeconds):null;
    }
    void CheckLongitudinalOutput()
    {
        using var operation=faultBrake.BeginOperation();
        if(!monitorSteering)return;
        lock(steeringSync) {
            if(!faultBrake.OperationCurrent)return;
            if(faultBrake.Latched){faultBrake.Refresh();return;}
            var command=Volatile.Read(ref lastAccCommand);
            var age=PedalProducerAge(command);
            if(pedalFeedGuard.CheckAge(clock.Elapsed.TotalSeconds,age,PedalRequired())!=SteeringFeedState.Lost)return;
            faultBrake.RunCurrent(()=> {
                command=Volatile.Read(ref lastAccCommand);age=PedalProducerAge(command);
                if(pedalFeedGuard.CheckAge(clock.Elapsed.TotalSeconds,age,PedalRequired())!=SteeringFeedState.Lost)return;
                var failure=new PedalFailure(DateTimeOffset.UtcNow,age,command?.IssuedTicks,command?.Epoch,
                    "Eigene Pedalausgabe ausgefallen: keine frischen Gas-/Bremsbefehle.") {
                        Producer=Volatile.Read(ref controlTickObservation)
                    };
                State.EnableAssists=false;faultBrake.Trip();
                Interlocked.Exchange(ref pendingPedalFailure,failure);
            });
        }
    }
    void DrainLongitudinalFailure()
    {
        if(Interlocked.Exchange(ref pendingPedalFailure,null) is not { } failure)return;
        if(!faultBrake.Latched)return;
        lastPedalFailure=failure;
        Disarm(failure.Reason);
    }
    void ResetLongitudinalWatchdog()
    {
        pedalFeedGuard.Reset();pendingPedalFailure=lastPedalFailure=null;
    }
}
