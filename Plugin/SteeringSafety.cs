using ETS2LA.Game.Output;
using ETS2LA.State;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly bool monitorSteering;
    readonly object steeringSync=new();
    readonly SteeringFeedGuard steeringGuard=new();
    SteeringFeedState steeringFeedState;
    Timer? steeringTimer;
    bool steeringMonitoringLive;
    sealed record SteeringCommand(double At,float Value);
    sealed record PedalCommand(double At,float? Throttle,float? Brake);
    sealed record SteeringFailure(double At,DateTimeOffset Utc,string Reason,double? CommandAge,float? LastCommand);
    SteeringCommand? lastSteeringCommand;
    PedalCommand? lastAccCommand;
    SteeringFailure? pendingSteeringFailure,lastSteeringFailure;

    void OnControlOutput(ControlEvent output)
    {
        if(output.ChannelDefinition?.Id=="LaneAssist.SteeringCorrection" &&
            output.Variables?.steering is float command && float.IsFinite(command) && Math.Abs(command)<=1 && State.EnableAssists)
            Interlocked.Exchange(ref lastSteeringCommand,new(clock.Elapsed.TotalSeconds,command));
        if(output.ChannelDefinition?.Id=="AdaptiveCruiseControl.Acceleration")
            Interlocked.Exchange(ref lastAccCommand,new(clock.Elapsed.TotalSeconds,output.Variables?.aforward,output.Variables?.abackward));
    }

    // This lock never nests the route lock. The timer can disable and brake
    // while a route callback is busy; diagnostics are consumed afterwards.
    void CheckSteeringOutput()
    {
        if(!monitorSteering)return;
        lock(steeringSync)
        {
            // Keep an existing stop alive even while route bookkeeping is busy.
            if(faultBrake.Latched)faultBrake.Refresh();
            var state=State;var command=Volatile.Read(ref lastSteeringCommand);
            double now=clock.Elapsed.TotalSeconds;
            bool required=steeringMonitoringLive && _IsRunning && Volatile.Read(ref active) &&
                state.EnableAssists && !Volatile.Read(ref preview) && !Volatile.Read(ref paused) &&
                state.DrivingMode!=DrivingMode.AdaptiveCruiseControlOnly;
            steeringFeedState=steeringGuard.Check(now,command?.At??double.NegativeInfinity,required);
            if(steeringFeedState!=SteeringFeedState.Lost)return;
            var failure=new SteeringFailure(now,DateTimeOffset.UtcNow,"Lenkausgabe ausgefallen: keine frischen Steuerbefehle der Spurhilfe.",
                command!=null?now-command.At:null,command?.Value);
            state.EnableAssists=false;
            // FaultBrake uses the public output contract; no route or ACC lock.
            faultBrake.Trip();
            Interlocked.Exchange(ref pendingSteeringFailure,failure);
        }
    }

    void StartSteeringMonitor()
    {
        if(!monitorSteering)return;
        lock(steeringSync)
        {
            steeringGuard.Reset();steeringFeedState=SteeringFeedState.Off;
            lastSteeringCommand=null;lastAccCommand=null;pendingSteeringFailure=lastSteeringFailure=null;
            steeringMonitoringLive=true;
            steeringTimer=new Timer(_=> {
                CheckSteeringOutput();
                if(!Monitor.TryEnter(sync))return;
                try {if(subscribed)SafetyWatchdog(clock.Elapsed.TotalSeconds);}
                finally {Monitor.Exit(sync);}
            },null,50,50);
        }
    }

    void StopSteeringMonitor()
    {
        lock(steeringSync)
        {
            steeringMonitoringLive=false;State.EnableAssists=false;steeringGuard.Reset();
            steeringTimer?.Dispose();steeringTimer=null;
            pendingSteeringFailure=null;
        }
    }

    void CancelSteeringSession()
    {
        lock(steeringSync)
        {
            State.EnableAssists=false;steeringGuard.Reset();steeringFeedState=SteeringFeedState.Off;
            faultBrake.Release();
            pendingSteeringFailure=null;
        }
    }

    void DrainSteeringFailure()
    {
        if(Interlocked.Exchange(ref pendingSteeringFailure,null) is not { } failure)return;
        lastSteeringFailure=failure;
        StopDriving(failure.Reason,DriveAction.EmergencyStop);
    }

    sealed record MotionSnapshot(double Time,DateTimeOffset Utc,bool Assists,double SpeedKph,double X,double Y,double Z,
        double Heading,double TelemetryAge,double PathAge,double? LateralOffset,double? HeightOffset,
        float? Steering,double? SteeringAge,float? AccThrottle,float? AccBrake,double? AccAge,float ManualBrake);
    readonly Queue<MotionSnapshot> motionHistory=new();
    double lastMotionAt=-100;
    void RecordMotion(double now,bool force=false)
    {
        if(!force && now-lastMotionAt<.05)return;
        lastMotionAt=now;
        var steering=Volatile.Read(ref lastSteeringCommand);var acc=Volatile.Read(ref lastAccCommand);
        var projected=safetyTrace?.ProjectForTracking(position);
        if(motionHistory.Count==240)motionHistory.Dequeue();
        motionHistory.Enqueue(new(now,DateTimeOffset.UtcNow,State.EnableAssists,speed*3.6,position.X,position.Y,position.Z,
            headingTurns,now-telemetryAt,now-pathAt,projected?.Offset,projected?.HeightOffset,
            steering?.Value,steering!=null?now-steering.At:null,acc?.Throttle,acc?.Brake,acc!=null?now-acc.At:null,brakeInputValue));
    }
}
