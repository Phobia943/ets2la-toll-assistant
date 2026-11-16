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
    sealed record SteeringFailure(double At,DateTimeOffset Utc,string Reason,double? CommandAge,float? LastCommand,
        NativeSteeringSnapshot Native);
    SteeringCommand? lastSteeringCommand;
    PedalCommand? lastAccCommand;
    SteeringFailure? collectingSteeringFailure,pendingSteeringFailure,lastSteeringFailure;

    void OnControlOutput(ControlEvent output)
    {
        double observedAt=clock.Elapsed.TotalSeconds;
        if(requireHostCompatibility&&output.ChannelDefinition?.Id is "LaneAssist.SteeringCorrection" or "AdaptiveCruiseControl.Acceleration") {
            var definition=output.ChannelDefinition;
            if(definition.GetType().GetField("MautIssuedAt")?.GetValue(definition) is not long issued||
                NativeCommandTime.AgeSeconds(issued,System.Diagnostics.Stopwatch.GetTimestamp(),System.Diagnostics.Stopwatch.Frequency) is not double age||
                definition.GetType().GetField("MautEpoch")?.GetValue(definition) is not long epoch||
                captureOutputEpoch?.Invoke(null,null) is not long currentEpoch||epoch!=currentEpoch)return;
            observedAt-=age;
        }
        if(output.ChannelDefinition?.Id=="LaneAssist.SteeringCorrection" &&
            output.Variables?.steering is float command && float.IsFinite(command) && Math.Abs(command)<=1 && State.EnableAssists)
            StoreSteering(new(observedAt,command));
        if(output.ChannelDefinition?.Id=="AdaptiveCruiseControl.Acceleration")
            StorePedals(new(observedAt,output.Variables?.aforward,output.Variables?.abackward));
    }
    void StoreSteering(SteeringCommand value)
    {
        SteeringCommand? previous;
        do {previous=Volatile.Read(ref lastSteeringCommand);if(previous!=null&&previous.At>value.At)return;}
        while(!ReferenceEquals(Interlocked.CompareExchange(ref lastSteeringCommand,value,previous),previous));
    }
    void StorePedals(PedalCommand value)
    {
        PedalCommand? previous;
        do {previous=Volatile.Read(ref lastAccCommand);if(previous!=null&&previous.At>value.At)return;}
        while(!ReferenceEquals(Interlocked.CompareExchange(ref lastAccCommand,value,previous),previous));
    }

    // This lock never nests the route lock. The timer can disable and brake
    // while a route callback is busy; diagnostics are consumed afterwards.
    void CheckSteeringOutput()
    {
        using var operation=faultBrake.BeginOperation();
        if(!monitorSteering)return;
        SteeringFailure failure;
        lock(steeringSync)
        {
            if(!faultBrake.OperationCurrent)return;
            // Keep an existing stop alive even while route bookkeeping is busy.
            if(faultBrake.Latched)faultBrake.Refresh();
            var state=State;var command=Volatile.Read(ref lastSteeringCommand);
            var latestTelemetry=Volatile.Read(ref latestDeliveredTelemetry);
            double now=clock.Elapsed.TotalSeconds;
            bool gamePaused=latestTelemetry?.Paused??Volatile.Read(ref paused);
            bool required=steeringMonitoringLive && _IsRunning && Volatile.Read(ref active) &&
                state.EnableAssists && !Volatile.Read(ref preview) && !gamePaused &&
                state.DrivingMode!=DrivingMode.AdaptiveCruiseControlOnly;
            steeringFeedState=steeringGuard.Check(now,command?.At??double.NegativeInfinity,required);
            if(steeringFeedState!=SteeringFeedState.Lost)return;
            failure=new SteeringFailure(now,DateTimeOffset.UtcNow,"Lenkausgabe ausgefallen: keine frischen Steuerbefehle der Spurhilfe.",
                command!=null?now-command.At:null,command?.Value,
                new(false,false,null,null,null,null,null,null,null,null,"CapturePending"));
            if(!faultBrake.RunCurrent(()=> {
                state.EnableAssists=false;
                faultBrake.Trip();
                collectingSteeringFailure=failure;
            }))return;
        }
        // Native lookup is observational work. Neither the first stop, future
        // brake refreshes nor manual acknowledgement may wait for it. If the
        // session is canceled while collecting, discard the late diagnosis.
        var observed=failure with {Native=NativeSteeringDiagnostics.Capture(readLaneAssist)};
        lock(steeringSync)
        {
            if(!ReferenceEquals(collectingSteeringFailure,failure))return;
            collectingSteeringFailure=null;
            if(faultBrake.Latched)Interlocked.Exchange(ref pendingSteeringFailure,observed);
        }
    }

    void StartSteeringMonitor()
    {
        if(!monitorSteering)return;
        lock(steeringSync)
        {
            steeringGuard.Reset();steeringFeedState=SteeringFeedState.Off;
            lastSteeringCommand=null;lastAccCommand=null;collectingSteeringFailure=pendingSteeringFailure=lastSteeringFailure=null;
            pendingImpact=lastImpact=null;impactGuard.Reset();
            ClearBrakeResponse();
            ResetCruiseStartPreference();
            steeringMonitoringLive=true;
            ConnectCoasting();
            using var suppressed=ExecutionContext.SuppressFlow();
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
            DisconnectCoasting();
            faultBrake.Release();
            steeringTimer?.Dispose();steeringTimer=null;
            collectingSteeringFailure=pendingSteeringFailure=null;
            pendingImpact=null;impactGuard.Reset();
            ClearBrakeResponse();
            ResetCruiseStartPreference();
        }
    }

    void CancelSteeringSession()
    {
        lock(steeringSync)
        {
            CancelRollingStart();
            State.EnableAssists=false;steeringGuard.Reset();steeringFeedState=SteeringFeedState.Off;
            faultBrake.Release();
            collectingSteeringFailure=pendingSteeringFailure=null;
            pendingImpact=null;impactGuard.Reset();
            ClearBrakeResponse();
            ResetCruiseStartPreference();
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
        float? Steering,double? SteeringAge,float? AccThrottle,float? AccBrake,double? AccAge,float ManualBrake,
        PhysicsObservation? Game);
    readonly Queue<MotionSnapshot> motionHistory=new();
    double lastMotionAt=-100;
    void RecordMotion(double now,bool force=false)
    {
        if(!force && now-lastMotionAt<.05)return;
        var steering=Volatile.Read(ref lastSteeringCommand);var acc=Volatile.Read(ref lastAccCommand);
        // These command references can be newer than a caller's tick clock.
        now=clock.Elapsed.TotalSeconds;
        lastMotionAt=now;
        var projected=safetyTrace?.ProjectForTracking(position);
        if(motionHistory.Count==240)motionHistory.Dequeue();
        motionHistory.Enqueue(new(now,DateTimeOffset.UtcNow,State.EnableAssists,speed*3.6,position.X,position.Y,position.Z,
            headingTurns,now-telemetryAt,now-pathAt,projected?.Offset,projected?.HeightOffset,
            steering?.Value,steering!=null?now-steering.At:null,acc?.Throttle,acc?.Brake,acc!=null?now-acc.At:null,brakeInputValue,physicsObservation));
    }
}
