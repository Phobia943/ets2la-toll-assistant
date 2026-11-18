using System.Diagnostics;
using System.Reflection;
using ETS2LA.Game.Output;
using ETS2LA.State;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    internal const string LongitudinalChannelId="local.mautassistent.longitudinal";
    readonly LongitudinalController longitudinal=new();
    readonly SimulationDynamics simulationDynamics=new();
    readonly Func<TrafficSpeedDecision>? readLongitudinalTraffic;
    internal Action? BeforeLongitudinalCommit;
    internal sealed record SpeedBound(string Source,double? Acceleration,double? TargetSpeed=null,string? Reason=null);
    internal sealed record LongitudinalSnapshot(DateTimeOffset Utc,long IssuedAt,long Epoch,double TelemetryAt,
        LongitudinalInput Input,LongitudinalOutput Output,SpeedBound[] Bounds,bool Published) {
            public object? TrafficEvidence{get;init;} public long? DynamicsOrigin{get;init;}
            public GameInputContextSnapshot? InputContext{get;init;}
            public object? CommitRecheckEvidence{get;init;}
            public GameInputDiagnosisResult? InputDiagnosis{get;init;}
            public PedalDeliverySample? InputSample{get;init;}}
    LongitudinalSnapshot? longitudinalSnapshot;
    bool throttleDeliveryWarningShown;
    public string SpeedControlStatus {get {lock(sync) {
        if(!State.EnableAssists||longitudinalSnapshot is not { } sample)return "Eigene Geschwindigkeitsregelung ausgeschaltet. Start mit F8; Spieltempomat ausgeschaltet lassen.";
        if(sample.Output.DeliveryMissing)return "Pedalempfang unterbrochen: Gas gesperrt, aktuelle Bremsgrenzen bleiben aktiv. Ins Spiel wechseln, mit Bremse/F9 übernehmen und erst danach neu mit F8 starten.";
        if(sample.Output.FocusSuspended)return "Fokusunterbrechung: Gas vorübergehend gesperrt; Rückkehr ins Spiel wird mit aktuellen Quellen geprüft.";
        string action=sample.Output.Holding?"Halten":sample.Output.Brake>0?"Bremsen":sample.Output.Throttle>0?"Beschleunigen":"Rollen";
        return $"Eigene Regelung: Ziel {sample.Input.TargetSpeed*3.6:F0} km/h · angefordert: {action}.";
    }}}
    readonly List<SpeedBound> speedBounds=[];
    double observedAcceleration;
    bool accelerationKnown=true;
    static readonly FieldInfo? outputIssuedAt=typeof(ControlChannelDefinition).GetField("MautIssuedAt"),
        outputEpoch=typeof(ControlChannelDefinition).GetField("MautEpoch"),
        outputSourceEpoch=typeof(ControlChannelDefinition).GetField("MautSourceEpoch"),
        outputSourceAt=typeof(ControlChannelDefinition).GetField("MautSourceAt");
    long CaptureLongitudinalEpoch()=>captureOutputEpoch?.Invoke(null,null) is long epoch?epoch:1;
    static double? FiniteLimit(float value)=>float.IsFinite(value)?value:null;
    float RecordSpeedBound(string source,float value,double? target=null,string? reason=null)
    {speedBounds.Add(new(source,FiniteLimit(value),target,reason));return value;}

    // Called exactly once by our own tick, never from native ACC callbacks.
    void RunLongitudinal(long epoch,long issuedAt)
    {
        if(!_IsRunning||!active||preview||!State.EnableAssists||State.DrivingMode==DrivingMode.LaneAssistOnly||
            faultBrake.Latched||brakeHeld) {longitudinal.Reset();simulationDynamics.Reset();throttleDeliveryWarningShown=false;return;}
        if(paused){longitudinal.Pause();simulationDynamics.Reset();return;}
        bool rollback=ObserveReverseMotion(epoch);
        speedBounds.Clear();
        curvePreviewReady=false;
        float safetyLimit=EvaluateAt(issuedAt,epoch);
        double now=clock.Elapsed.TotalSeconds;
        // Only a derivative from advancing physics frames is evidence. An
        // identical provider poll may reuse it within its ORIGINAL lease; it
        // cannot reset its deadline or manufacture a negative acceleration.
        var dynamics=simulationDynamics.Read(now,Stopwatch.GetTimestamp());
        bool needsDynamics=requireHostCompatibility||appliedTelemetry!=null;
        if(needsDynamics) {
            accelerationKnown=dynamics!=null;
            observedAcceleration=dynamics?.Acceleration??0;
        }
        // EvaluateAt just rechecked tracking at the current pose. Do not turn
        // that accepted corridor into a zero-speed target at a shorter route
        // deadline. Recovery's explicit deceleration still applies.
        bool routeKnown=CurrentRouteUsable(now);
        double target=Math.Max(0,State.DesiredSpeed);
        if(!accelerationKnown) {
            target=Math.Min(target,Math.Max(0,speed));
            safetyLimit=Minimum(safetyLimit,RecordSpeedBound("DynamicsPending",-.35f,reason:"Simulationszeit für Fahrzeugdynamik noch nicht bestätigt"));
        }
        var cruise=readCruiseLimits();
        if(cruise.MaximumSpeed>0)target=Math.Min(target,cruise.MaximumSpeed);
        if(cruise.HonorRoadLimit) {
            if(physicsObservation?.SpeedLimitMps is >0 and var roadLimit)target=Math.Min(target,roadLimit);
            else {
                // Missing map speed data is not a zero-speed command. Use a
                // bounded fallback; explicit local toll/curve/scene limits
                // below it remain in force, including a 10 km/h open passage.
                target=Math.Min(target,30/3.6);
                speedBounds.Add(new("UnknownRoadLimit",null,30/3.6,"Kein Spiel-Limit gemeldet: vorsichtige Ersatzgrenze"));
            }
        }
        if(preferredCruiseOwned&&!PreferredCorridorReady())target=Math.Min(target,baselineCruise);
        speedBounds.Add(new("Cruise",null,target));
        if(decision.TargetSpeed is double tollTarget)target=Math.Min(target,Math.Max(0,tollTarget));
        if(curveDecision is { } curve)target=Math.Min(target,Math.Max(0,curve.AllowedSpeed));
        // Unknown curvature is not permission to accelerate. The asynchronous
        // profile will replace this temporary zero bound when it is available.
        if(options.CurvePreviewEnabled&&!curvePreviewReady) {
            target=Math.Min(target,Math.Max(0,speed));
            safetyLimit=Minimum(safetyLimit,RecordSpeedBound("CurvePending",-.35f,reason:"Streckenvorschau noch nicht aktuell"));
        }
        var surroundings=readLongitudinalTraffic?.Invoke()??ReadLongitudinalSurroundings();
        var sourceLease=readLongitudinalTraffic==null?longitudinalSourceLease:null;
        long observationEpoch=readLongitudinalTraffic==null?longitudinalObservationEpoch:CaptureSourceEpoch();
        safetyLimit=Minimum(safetyLimit,RecordSpeedBound("Traffic",surroundings.Acceleration,reason:surroundings.Reason));
        double? stopDistance=surroundings.StopDistance;
        string? stopIdentity=surroundings.StopIdentity;
        if(router.PlanningStopDistance is double plannedDistance) {
            if(stopDistance==null||plannedDistance<stopDistance){stopDistance=Math.Max(0,plannedDistance);stopIdentity=router.PlanningStopKey;}
        }
        bool physicalHold=holdPedalFloor>0||scenePedalFloor>0||decision.TargetSpeed==0;
        double pitch=safetyTrace==null?0:Math.Asin(Math.Clamp(safetyTrace.DirectionAt(safetyTrace.ProjectForTracking(position).Along).Y,-1,1));
        var focus=ReadPedalFocus(appliedTelemetry,routeKnown,epoch);
        var input=new LongitudinalInput(now,Math.Max(1,epoch),true,paused,routeKnown,speed,
            observedAcceleration,target,FiniteLimit(safetyLimit),stopDistance,physicalHold,
            Emergency:faultBrake.Latched,Pitch:pitch,ActualThrottle:physicsObservation?.GameThrottle,ActualBrake:physicsObservation?.GameBrake,
            CoastEligible:surroundings.Clear&&CanCoast()&&
                (!cruise.HonorRoadLimit||physicsObservation?.SpeedLimitMps is float cap&&speed<=cap)&&
                (cruise.MaximumSpeed==0||speed<=cruise.MaximumSpeed),StopIdentity:stopIdentity??sceneHold.OwnerKey??tracked,
            ReceivedThrottle:physicsObservation?.ReceivedThrottle,ReceivedBrake:physicsObservation?.ReceivedBrake,
            ParkingBrake:physicsObservation?.ParkingBrake==true,ReverseGear:physicsObservation?.Gear<0,RollbackDetected:rollback,
            StationaryDisplacement:reverseMotion.StationaryDisplaced){Focus=focus};
        var result=longitudinal.Step(input);
        bool published=false;object? commitRecheckEvidence=null;SourceLease? commitRecheckLease=null;
        BeforeLongitudinalCommit?.Invoke(); // Isolated scheduling tests; unset in production.
            faultBrake.RunCurrent(()=> {
                // Epoch may itself wait on the host output lock. Check age and
                // latest pause/authority after that wait, not before it.
                long current=CaptureLongitudinalEpoch();
                var latest=Volatile.Read(ref latestDeliveredTelemetry);
                if(current!=epoch||NativeCommandTime.AgeSeconds(issuedAt,Stopwatch.GetTimestamp(),Stopwatch.Frequency)==null||
                    !_IsRunning||!active||preview||!State.EnableAssists||State.DrivingMode==DrivingMode.LaneAssistOnly||
                    faultBrake.Latched||brakeHeld||(latest?.Paused??paused)||
                    (latest!=null&&(!latest.Active||clock.Elapsed.TotalSeconds-latest.Arrived>=.2||!double.IsFinite(latest.Speed)))||
                    !SensorsFresh(clock.Elapsed.TotalSeconds))return;
                // Preserve the origins of the observations actually evaluated.
                // A newer frame cannot rejuvenate this old decision. An
                // already-unknown observation may still publish its no-gas
                // braking plan; a revoked clear decision must be recomputed.
                if(sourceLease!=null&&!SourceLeaseCurrent(sourceLease))return;
                if(sourceLease!=null&&result.Throttle>0&&RecheckTrafficForCommit(sourceLease,surroundings,result.TargetAcceleration,out commitRecheckEvidence,out commitRecheckLease)) {
                    // Do not advance the stateful controller a second time or
                    // invent a brake amplitude from a different observation.
                    // Keep any original braking; replan normally next Tick.
                    result=result with{Throttle=0,Reason="Neue Verkehrsbegrenzung vor Pedalausgabe: Gas gesperrt"};
                }
                // The bounded re-observation itself may take time or observe
                // a source reset. It never renews this command's original age.
                long finalEpoch=CaptureLongitudinalEpoch();
                long finalSourceEpoch=CaptureSourceEpoch();
                bool originalSourcesCurrent=sourceLease==null||SourceLeaseAtEpoch(sourceLease,finalSourceEpoch);
                // The worker identity check may invoke source providers. Finish
                // it before the final focus/steering witness, then use only a
                // lock-free local revocation check for this original result.
                if(sourceLease?.MapGate is { } checkedGate&&!MapGateLeaseCurrent(checkedGate))return;
                var finalFocus=ReadPedalFocus(Volatile.Read(ref latestDeliveredTelemetry),CurrentRouteUsable(clock.Elapsed.TotalSeconds),finalEpoch,finalSourceEpoch);
                latest=Volatile.Read(ref latestDeliveredTelemetry);
                if(!originalSourcesCurrent||finalEpoch!=epoch||sourceLease!=null&&finalSourceEpoch!=sourceLease.Epoch||
                    NativeCommandTime.AgeSeconds(issuedAt,Stopwatch.GetTimestamp(),Stopwatch.Frequency)==null||
                    !_IsRunning||!active||preview||!State.EnableAssists||State.DrivingMode==DrivingMode.LaneAssistOnly||
                    faultBrake.Latched||brakeHeld||(latest?.Paused??paused)||
                    (latest!=null&&(!latest.Active||clock.Elapsed.TotalSeconds-latest.Arrived>=.2||!double.IsFinite(latest.Speed)))||
                    !SensorsFresh(clock.Elapsed.TotalSeconds))return;
                if(finalFocus!=null)finalFocus=finalFocus with{Now=Stopwatch.GetTimestamp()};
                if(sourceLease!=null&&!SourceLeaseAtEpoch(sourceLease,finalSourceEpoch)||
                    NativeCommandTime.AgeSeconds(issuedAt,Stopwatch.GetTimestamp(),Stopwatch.Frequency)==null)return;
                if(result.Throttle>0&&!longitudinal.FocusCommitAllowed(focus,finalFocus))
                    result=result with{Throttle=0,FocusSuspended=longitudinal.FocusSuspended,Reason="Fokuskontext vor Pedalausgabe geändert: Gas gesperrt"};
                if(result.Throttle>0&&sourceLease!=null&&!SourceFramesCurrent(commitRecheckLease??sourceLease))
                    result=result with{Throttle=0,Reason="Weitere Quellenänderung während Pedal-Endprüfung: Gas gesperrt"};
                if(result.Throttle>0&&needsDynamics&&
                    (dynamics==null||simulationDynamics.Read(clock.Elapsed.TotalSeconds,Stopwatch.GetTimestamp()) is not { } currentDynamics||
                    currentDynamics.Origin!=dynamics.Value.Origin))return;
                if(sourceLease?.MapGate is { } finalGate&&(!MapGateLeaseLocalCurrent(finalGate)||
                    !SourceLeaseAtEpoch(sourceLease,finalSourceEpoch)||faultBrake.ControlEpoch!=finalGate.Epoch||
                    NativeCommandTime.AgeSeconds(issuedAt,Stopwatch.GetTimestamp(),Stopwatch.Frequency)==null||
                    !_IsRunning||!active||preview||!State.EnableAssists||faultBrake.Latched||brakeHeld))return;
                if(result.Fault) {
                    Disarm("Eigene Geschwindigkeitsregelung: "+result.Reason);
                    return;
                }
                if(result.ManualRequired) {
                    StopDriving("Rückwärtsgang gewählt – bitte manuell übernehmen.",DriveAction.RejectStart);
                    return;
                }
                // A newly delivered parking brake can arrive while the host
                // epoch lock is busy. Veto stale propulsion using that latest
                // fresh frame, without erasing a required braking command.
                if(latest?.Physics.ParkingBrake==true) {
                    result=result with{Throttle=0};
                    if(CurrentParkingStandstill(latest))SuspendNormalHoldsForParking(epoch,issuedAt);
                }
                var definition=new ControlChannelDefinition {Id=LongitudinalChannelId,Timeout=.2f};
                outputIssuedAt?.SetValue(definition,issuedAt);outputEpoch?.SetValue(definition,epoch);
                outputSourceEpoch?.SetValue(definition,observationEpoch);
                long sourceAt=sourceLease==null?0L:
                    Math.Min(Math.Min(sourceLease.Traffic,sourceLease.Parked),Math.Min(sourceLease.Signals,sourceLease.Gates));
                if(sourceLease?.MapGate is { } mapGate)sourceAt=Math.Min(sourceAt,mapGate.Result.OriginalTimestamp);
                if(result.Throttle>0&&needsDynamics&&dynamics!=null)sourceAt=Math.Min(sourceAt,dynamics.Value.Origin);
                outputSourceAt?.SetValue(definition,sourceAt);
                var command=new ControlEvent {ChannelDefinition=definition,Properties=new(){Weight=1},
                    Variables=new() {aforward=result.Throttle,abackward=-result.Brake}};
                publishOutput(command);published=true;
                // Tests and event sinks need not feed events back to this plugin.
                OnControlOutput(command);
                if(longitudinal.FocusReturnPending&&Volatile.Read(ref lastAccCommand) is { } accepted&&
                    accepted.IssuedTicks==issuedAt&&accepted.Epoch==epoch) {
                    // Output/event callbacks can wait too. This authorizes a
                    // current retry, never claims logical game receipt, and
                    // must not finish return using a pre-callback witness.
                    long returnEpoch=CaptureLongitudinalEpoch(),returnSourceEpoch=CaptureSourceEpoch();
                    var returnFocus=ReadPedalFocus(Volatile.Read(ref latestDeliveredTelemetry),
                        CurrentRouteUsable(clock.Elapsed.TotalSeconds),returnEpoch,returnSourceEpoch);
                    if(returnFocus!=null)returnFocus=returnFocus with{Now=Stopwatch.GetTimestamp()};
                    if(returnEpoch==epoch&&(sourceLease==null||SourceLeaseAtEpoch(sourceLease,returnSourceEpoch))&&
                        NativeCommandTime.AgeSeconds(issuedAt,Stopwatch.GetTimestamp(),Stopwatch.Frequency) is double returnAge&&returnAge<=.15)
                        longitudinal.AcceptFocusReturn(focus,returnFocus);
                    else longitudinal.FocusCommitAllowed(focus,null); // A late/revoked attempt cannot finish the return transaction.
                }
            });
        // Neither a vetoed request nor a completely rejected publication was
        // a physical command. Reconcile only downward; never manufacture an
        // acknowledgement or clear existing receipt-window provenance/latches.
        longitudinal.ReduceUnpublishedPedals(published?result.Throttle:0,published?result.Brake:0);
        if(result.FocusSuspended!=longitudinal.FocusSuspended)result=result with{FocusSuspended=longitudinal.FocusSuspended};
        var inputContextSnapshot=ReadInputContext();
        // Compare a previously published command to a LATER telemetry frame,
        // never the just-computed command to its earlier input observation.
        var previous=longitudinalSnapshot;PedalDeliverySample? deliverySample=null;
        if(previous?.Published==true&&previous.Epoch==epoch&&appliedTelemetry is { } actual&&actual.Origin>=previous.IssuedAt)
            deliverySample=new(actual.Origin,previous.IssuedAt,previous.Output.Throttle,previous.Output.Brake,
                actual.Physics.ReceivedThrottle,actual.Physics.ReceivedBrake,actual.Physics.GameThrottle,actual.Physics.GameBrake,
                actual.Physics.EffectiveClutch,actual.Physics.Gear,actual.Physics.DashboardGear,actual.Physics.EngineRpm);
        longitudinalSnapshot=new(DateTimeOffset.UtcNow,issuedAt,epoch,telemetryAt,input,result,speedBounds.ToArray(),published){
            TrafficEvidence=longitudinalTrafficEvidence,CommitRecheckEvidence=commitRecheckEvidence,DynamicsOrigin=dynamics?.Origin,InputContext=inputContextSnapshot,
            InputSample=deliverySample,InputDiagnosis=deliverySample==null?null:GameInputDiagnosis.Evaluate(deliverySample,inputContextSnapshot,Stopwatch.GetTimestamp(),Stopwatch.Frequency)};
        if(!result.DeliveryMissing)throttleDeliveryWarningShown=false;
        else if(published&&!throttleDeliveryWarningShown) {
            throttleDeliveryWarningShown=true;
            _=Task.Run(()=> {
                if(!State.EnableAssists||CaptureLongitudinalEpoch()!=epoch)return;
                try {notify("Pedalempfang unterbrochen", "Das Spiel übernimmt die Pedalbefehle nicht zuverlässig. Gas ist gesperrt; Bremsgrenzen der aktuellen Fahrsituation bleiben wirksam. Ins Spiel wechseln, mit Bremse/F9 übernehmen und erst danach neu mit F8 starten. Der Empfangsausfall allein löst keine Notbremsung aus.");}
                catch { /* A failed notification cannot alter driving output. */ }
            });
        }
    }
    static float Minimum(float first,float second)=>float.IsFinite(first)?float.IsFinite(second)?Math.Min(first,second):first:second;
    bool CurrentRouteUsable(double now)=>safetyTrace!=null&&SensorsFresh(now)&&
        currentDriveSample is { } sample&&DriveSupervisor.RouteUsable(sample,now);
}
