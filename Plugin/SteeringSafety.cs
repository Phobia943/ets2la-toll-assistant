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
    sealed record SteeringCommand(double At,float Value) {
        public double? ReceivedAt {get;init;}
        public long? IssuedTicks {get;init;}
        public long? ReceivedTicks {get;init;}
        public long? Epoch {get;init;}
    }
    sealed record PedalCommand(double At,float? Throttle,float? Brake) {
        public double? ReceivedAt {get;init;}
        public long? IssuedTicks {get;init;}
        public long? ReceivedTicks {get;init;}
        public long? Epoch {get;init;}
    }
    sealed record SteeringFailure(double At,DateTimeOffset Utc,string Reason,double? CommandAge,float? LastCommand,
        NativeSteeringSnapshot Native) {
        public double? CommandReceivedAge {get;init;}
        public double? TelemetryDeliveryAge {get;init;}
        public double? TripCompletedAt {get;init;}
        public long? IssuedTicks {get;init;}
        public long? ReceivedTicks {get;init;}
        public long ObservedTicks {get;init;}
        public long ClockFrequency {get;init;}
        public double InitialProbeAt {get;init;}
        public long InitialProbeTicks {get;init;}
        public DateTimeOffset InitialProbeUtc {get;init;}
    }
    SteeringCommand? lastSteeringCommand;
    PedalCommand? lastAccCommand;
    SteeringFailure? collectingSteeringFailure,pendingSteeringFailure,lastSteeringFailure;

    void OnControlOutput(ControlEvent output)
    {
        double receivedAt=clock.Elapsed.TotalSeconds;
        long receivedTicks=System.Diagnostics.Stopwatch.GetTimestamp();
        double observedAt=receivedAt;
        long? issuedTicks=null,issuedEpoch=null;
        if(requireHostCompatibility&&output.ChannelDefinition?.Id is "LaneAssist.SteeringCorrection" or "AdaptiveCruiseControl.Acceleration") {
            var definition=output.ChannelDefinition;
            if(definition.GetType().GetField("MautIssuedAt")?.GetValue(definition) is not long issued||
                NativeCommandTime.AgeSeconds(issued,System.Diagnostics.Stopwatch.GetTimestamp(),System.Diagnostics.Stopwatch.Frequency) is not double age||
                definition.GetType().GetField("MautEpoch")?.GetValue(definition) is not long epoch||
                captureOutputEpoch?.Invoke(null,null) is not long currentEpoch||epoch!=currentEpoch)return;
            // Keep the verified producer stamp in its original clock domain.
            // Subtracting this age from an earlier plugin-clock read would
            // artificially age commands if the callback was descheduled.
            issuedTicks=issued;issuedEpoch=epoch;
        }
        if(output.ChannelDefinition?.Id=="LaneAssist.SteeringCorrection" &&
            output.Variables?.steering is float command && float.IsFinite(command) && Math.Abs(command)<=1 && State.EnableAssists)
            StoreSteering(new(observedAt,command){ReceivedAt=receivedAt,IssuedTicks=issuedTicks,ReceivedTicks=receivedTicks,Epoch=issuedEpoch});
        if(output.ChannelDefinition?.Id=="AdaptiveCruiseControl.Acceleration")
            StorePedals(new(observedAt,output.Variables?.aforward,output.Variables?.abackward){ReceivedAt=receivedAt,IssuedTicks=issuedTicks,ReceivedTicks=receivedTicks,Epoch=issuedEpoch});
    }
    void StoreSteering(SteeringCommand value)
    {
        // Same short authority gate as the final watchdog check/trip. Never
        // acquire steeringSync here (the watchdog holds it before this gate).
        faultBrake.RunCurrent(()=> {
            if(!State.EnableAssists)return;
            if(requireHostCompatibility&&(value.Epoch is not long epoch||value.IssuedTicks is not long issued||
                captureOutputEpoch?.Invoke(null,null) is not long current||epoch!=current||
                NativeCommandTime.AgeSeconds(issued,System.Diagnostics.Stopwatch.GetTimestamp(),System.Diagnostics.Stopwatch.Frequency)==null))return;
            var previous=Volatile.Read(ref lastSteeringCommand);
            if(previous!=null&&(previous.IssuedTicks is long oldTicks&&value.IssuedTicks is long newTicks?oldTicks>newTicks:previous.At>value.At))return;
            Volatile.Write(ref lastSteeringCommand,value);
        });
    }
    void StorePedals(PedalCommand value)
    {
        faultBrake.RunCurrent(()=> {
            if(requireHostCompatibility&&(value.Epoch is not long epoch||value.IssuedTicks is not long issued||
                captureOutputEpoch?.Invoke(null,null) is not long current||epoch!=current||
                NativeCommandTime.AgeSeconds(issued,System.Diagnostics.Stopwatch.GetTimestamp(),System.Diagnostics.Stopwatch.Frequency)==null))return;
            var previous=Volatile.Read(ref lastAccCommand);
            if(previous!=null&&(previous.IssuedTicks is long oldTicks&&value.IssuedTicks is long newTicks?oldTicks>newTicks:previous.At>value.At))return;
            Volatile.Write(ref lastAccCommand,value);
        });
    }

    static double? CommandAge(long? issued,double? localAt,long rawNow,double localNow)=>issued is long ticks?
        NativeCommandTime.UnboundedAgeSeconds(ticks,rawNow,System.Diagnostics.Stopwatch.Frequency):localAt is double at?localNow-at:null;
    (double Now,long Ticks,double? Age) SteeringCommandAge(SteeringCommand? command)
    {
        // Epoch access can wait for the host's output gate. Take BOTH clocks
        // afterwards, otherwise that wait could rejuvenate an expired command.
        bool current=command!=null&&(!requireHostCompatibility||command.Epoch is long epoch&&
            captureOutputEpoch?.Invoke(null,null) is long actual&&epoch==actual);
        double localNow=clock.Elapsed.TotalSeconds;
        long rawNow=System.Diagnostics.Stopwatch.GetTimestamp();
        return (localNow,rawNow,current?CommandAge(command!.IssuedTicks,command.At,rawNow,localNow):null);
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
            var probe=SteeringCommandAge(command);
            var latestTelemetry=Volatile.Read(ref latestDeliveredTelemetry);
            double now=probe.Now;
            bool gamePaused=latestTelemetry?.Paused??Volatile.Read(ref paused);
            bool required=steeringMonitoringLive && _IsRunning && Volatile.Read(ref active) &&
                state.EnableAssists && !Volatile.Read(ref preview) && !gamePaused &&
                state.DrivingMode!=DrivingMode.AdaptiveCruiseControlOnly;
            long observedTicks=probe.Ticks;
            double? commandAge=probe.Age;
            steeringFeedState=steeringGuard.CheckAge(now,commandAge,required);
            if(steeringFeedState!=SteeringFeedState.Lost)return;
            failure=new SteeringFailure(now,DateTimeOffset.UtcNow,"Lenkausgabe ausgefallen: keine frischen Steuerbefehle der Spurhilfe.",
                commandAge,command?.Value,
                new(false,false,null,null,null,null,null,null,null,null,"CapturePending")) {
                    CommandReceivedAge=command?.ReceivedAt is double received?now-received:null,
                    TelemetryDeliveryAge=latestTelemetry!=null?now-latestTelemetry.Arrived:null,
                    IssuedTicks=command?.IssuedTicks,ReceivedTicks=command?.ReceivedTicks,
                    ObservedTicks=observedTicks,ClockFrequency=System.Diagnostics.Stopwatch.Frequency
                };
            if(!faultBrake.RunCurrent(()=> {
                // A fresh output or SDK pause may have arrived after the first
                // check, including while waiting for output authority. Recheck
                // immediately before committing; never extend the age limit.
                var latestCommand=Volatile.Read(ref lastSteeringCommand);
                var confirmed=SteeringCommandAge(latestCommand);
                var finalTelemetry=Volatile.Read(ref latestDeliveredTelemetry);
                double confirmedNow=confirmed.Now;
                long confirmedTicks=confirmed.Ticks;
                bool stillRequired=steeringMonitoringLive&&_IsRunning&&Volatile.Read(ref active)&&
                    state.EnableAssists&&!Volatile.Read(ref preview)&&!(finalTelemetry?.Paused??Volatile.Read(ref paused))&&
                    state.DrivingMode!=DrivingMode.AdaptiveCruiseControlOnly;
                var confirmedAge=confirmed.Age;
                steeringFeedState=steeringGuard.CheckAge(confirmedNow,confirmedAge,stillRequired);
                if(steeringFeedState!=SteeringFeedState.Lost)return;
                failure=failure with {At=confirmedNow,CommandAge=confirmedAge,LastCommand=latestCommand?.Value,
                    Utc=DateTimeOffset.UtcNow,InitialProbeAt=failure.At,InitialProbeTicks=failure.ObservedTicks,InitialProbeUtc=failure.Utc,
                    IssuedTicks=latestCommand?.IssuedTicks,ReceivedTicks=latestCommand?.ReceivedTicks,ObservedTicks=confirmedTicks,
                    CommandReceivedAge=latestCommand?.ReceivedAt is double at?confirmedNow-at:null,
                    TelemetryDeliveryAge=finalTelemetry!=null?confirmedNow-finalTelemetry.Arrived:null};
                state.EnableAssists=false;
                faultBrake.Trip();
                failure=failure with {TripCompletedAt=clock.Elapsed.TotalSeconds};
                collectingSteeringFailure=failure;
            }))return;
            if(!ReferenceEquals(collectingSteeringFailure,failure))return;
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
            lastSteeringCommand=null;lastAccCommand=null;
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
            lastSteeringCommand=null;lastAccCommand=null;
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
        PhysicsObservation? Game) {
        public double? SteeringReceivedAge {get;init;}
        public double? AccReceivedAge {get;init;}
        public float ScenePedalFloor {get;init;}
        public string? SceneHoldOwner {get;init;}
        public bool SceneHolding {get;init;}
        public bool SceneStopping {get;init;}
        public string? SceneReason {get;init;}
        public float PlannedPedalFloor {get;init;}
        public float TollAccelerationLimit {get;init;}
        public NativeAccSnapshot? NativeAcc {get;init;}
        public long? SteeringIssuedTicks {get;init;}
        public long? SteeringReceivedTicks {get;init;}
        public long? SteeringEpoch {get;init;}
        public long? AccEpoch {get;init;}
        public long ObservedTicks {get;init;}
        public long ClockFrequency {get;init;}
    }
    NativeAccSnapshot? nativeAccSnapshot;
    double nativeAccObservedAt=-100;
    object? nativeAccOwner;
    long nativeAccRequest;
    readonly Queue<MotionSnapshot> motionHistory=new();
    double lastMotionAt=-100;
    void RecordMotion(double now,bool force=false)
    {
        if(!force && now-lastMotionAt<.05)return;
        var steering=Volatile.Read(ref lastSteeringCommand);var acc=Volatile.Read(ref lastAccCommand);
        // These command references can be newer than a caller's tick clock.
        now=clock.Elapsed.TotalSeconds;
        long observedTicks=System.Diagnostics.Stopwatch.GetTimestamp();
        lastMotionAt=now;
        if(now-nativeAccObservedAt>=.1) {
            var observedOwner=bridge.DiagnosticOwner;
            if(!ReferenceEquals(observedOwner,nativeAccOwner)) {
                nativeAccOwner=observedOwner;Volatile.Write(ref nativeAccSnapshot,null);
            }
            long request=Interlocked.Increment(ref nativeAccRequest);
            // Foreign field inspection, schema validation and the cached pin's
            // first file read run ONLY on the bounded diagnostic worker.
            // The watchdog never waits for that work, a provider or filesystem.
            diagnostics.Enqueue(DiagnosticQueue.Slot.AccObservation,()=> {
                var observed=NativeAccDiagnostics.Capture(observedOwner);
                if(request==Interlocked.Read(ref nativeAccRequest)&&ReferenceEquals(bridge.DiagnosticOwner,observedOwner))
                    Volatile.Write(ref nativeAccSnapshot,observed);
            });
            nativeAccObservedAt=now;
        }
        var projected=safetyTrace?.ProjectForTracking(position);
        if(motionHistory.Count==240)motionHistory.Dequeue();
        motionHistory.Enqueue(new(now,DateTimeOffset.UtcNow,State.EnableAssists,speed*3.6,position.X,position.Y,position.Z,
            headingTurns,now-telemetryAt,now-pathAt,projected?.Offset,projected?.HeightOffset,
            steering?.Value,CommandAge(steering?.IssuedTicks,steering?.At,observedTicks,now),acc?.Throttle,acc?.Brake,
            CommandAge(acc?.IssuedTicks,acc?.At,observedTicks,now),brakeInputValue,physicsObservation) {
                SteeringReceivedAge=steering?.ReceivedAt is double received?now-received:null,
                AccReceivedAge=acc?.ReceivedAt is double accReceived?now-accReceived:null,
                ScenePedalFloor=scenePedalFloor,SceneHoldOwner=sceneHold.OwnerKey,SceneHolding=sceneHold.Holding,
                SceneStopping=sceneStopping,SceneReason=sceneStatus,PlannedPedalFloor=holdPedalFloor,
                TollAccelerationLimit=decision.MaxAcceleration,NativeAcc=Volatile.Read(ref nativeAccSnapshot),
                SteeringIssuedTicks=steering?.IssuedTicks,SteeringReceivedTicks=steering?.ReceivedTicks,
                SteeringEpoch=steering?.Epoch,AccEpoch=acc?.Epoch,
                ObservedTicks=observedTicks,ClockFrequency=System.Diagnostics.Stopwatch.Frequency
            });
    }
}
