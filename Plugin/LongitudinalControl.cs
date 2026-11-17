using System.Diagnostics;
using System.Reflection;
using ETS2LA.Game.Output;
using ETS2LA.State;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    internal const string LongitudinalChannelId="local.mautassistent.longitudinal";
    readonly LongitudinalController longitudinal=new();
    readonly Func<TrafficSpeedDecision>? readLongitudinalTraffic;
    internal Action? BeforeLongitudinalCommit;
    internal sealed record SpeedBound(string Source,double? Acceleration,double? TargetSpeed=null,string? Reason=null);
    internal sealed record LongitudinalSnapshot(DateTimeOffset Utc,long IssuedAt,long Epoch,double TelemetryAt,
        LongitudinalInput Input,LongitudinalOutput Output,SpeedBound[] Bounds,bool Published) {public object? TrafficEvidence{get;init;}}
    LongitudinalSnapshot? longitudinalSnapshot;
    bool throttleDeliveryWarningShown;
    public string SpeedControlStatus {get {lock(sync) {
        if(!State.EnableAssists||longitudinalSnapshot is not { } sample)return "Eigene Geschwindigkeitsregelung ausgeschaltet. Start mit F8; Spieltempomat ausgeschaltet lassen.";
        if(sample.Output.ThrottleDeliveryMissing)return "Gaseingang fehlt: Beschleunigung ausgesetzt. Ins Spiel wechseln, manuell übernehmen und erst danach neu mit F8 starten.";
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
            faultBrake.Latched||brakeHeld) {longitudinal.Reset();throttleDeliveryWarningShown=false;return;}
        if(paused){longitudinal.Pause();return;}
        bool rollback=ObserveReverseMotion(epoch);
        speedBounds.Clear();
        float safetyLimit=EvaluateAt(issuedAt,epoch);
        double now=clock.Elapsed.TotalSeconds;
        bool routeKnown=SensorsFresh(now)&&now-pathAt<.2&&safetyTrace!=null&&
            driveAction is DriveAction.Drive or DriveAction.AwaitRoute or DriveAction.Recover;
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
        if(options.CurvePreviewEnabled&&curveDecision==null) {
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
        var input=new LongitudinalInput(now,Math.Max(1,epoch),true,paused,routeKnown,speed,
            observedAcceleration,target,FiniteLimit(safetyLimit),stopDistance,physicalHold,
            Emergency:faultBrake.Latched,Pitch:pitch,ActualThrottle:physicsObservation?.GameThrottle,ActualBrake:physicsObservation?.GameBrake,
            CoastEligible:surroundings.Clear&&CanCoast()&&
                (!cruise.HonorRoadLimit||physicsObservation?.SpeedLimitMps is float cap&&speed<=cap)&&
                (cruise.MaximumSpeed==0||speed<=cruise.MaximumSpeed),StopIdentity:stopIdentity??sceneHold.OwnerKey??tracked,
            ReceivedThrottle:physicsObservation?.ReceivedThrottle,ReceivedBrake:physicsObservation?.ReceivedBrake,
            ParkingBrake:physicsObservation?.ParkingBrake==true,ReverseGear:physicsObservation?.Gear<0,RollbackDetected:rollback,
            StationaryDisplacement:reverseMotion.StationaryDisplaced);
        var result=longitudinal.Step(input);
        bool published=false;
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
                if(result.Fault) {
                    Disarm(result.DeliveryMissing?"Pedalbefehle werden im Spiel nicht wirksam – manuell übernehmen.":"Eigene Geschwindigkeitsregelung: "+result.Reason);
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
                outputSourceAt?.SetValue(definition,sourceLease==null?0L:
                    Math.Min(Math.Min(sourceLease.Traffic,sourceLease.Parked),Math.Min(sourceLease.Signals,sourceLease.Gates)));
                var command=new ControlEvent {ChannelDefinition=definition,Properties=new(){Weight=1},
                    Variables=new() {aforward=result.Throttle,abackward=-result.Brake}};
                publishOutput(command);published=true;
                // Tests and event sinks need not feed events back to this plugin.
                OnControlOutput(command);
            });
        longitudinalSnapshot=new(DateTimeOffset.UtcNow,issuedAt,epoch,telemetryAt,input,result,speedBounds.ToArray(),published){TrafficEvidence=longitudinalTrafficEvidence};
        if(!result.ThrottleDeliveryMissing)throttleDeliveryWarningShown=false;
        else if(published&&!throttleDeliveryWarningShown) {
            throttleDeliveryWarningShown=true;
            _=Task.Run(()=> {
                if(!State.EnableAssists||CaptureLongitudinalEpoch()!=epoch)return;
                try {notify("Gaseingang fehlt", "Das Spiel meldet den angeforderten Gaseingang nicht. Beschleunigung ist ausgesetzt; bitte ins Spiel wechseln und manuell übernehmen. Daraus allein wird keine Notbremsung ausgelöst.");}
                catch { /* A failed notification cannot alter driving output. */ }
            });
        }
    }
    static float Minimum(float first,float second)=>float.IsFinite(first)?float.IsFinite(second)?Math.Min(first,second):first:second;
}
