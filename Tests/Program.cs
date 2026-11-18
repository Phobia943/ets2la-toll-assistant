using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Collections;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using MautAssistent;

if(args is ["--brake-replay",var brakeCapture,"--assume-fresh-legacy-command"])
    return GeometryBrakeTests.Replay(brakeCapture);

if (args is ["--replay", var file])
{
    using var report = JsonDocument.Parse(File.ReadAllText(file));
    var recording = report.RootElement.GetProperty("driveRecording").Deserialize<DriveRecording>(
        new JsonSerializerOptions { NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals })!;
    var replay = new DriveSupervisor(recording.InitialState);
    int mismatches = 0;
    foreach (var sample in recording.Samples)
    {
        var actual = replay.Step(sample.Frame);
        if (actual == sample.Action) continue;
        Console.WriteLine($"{sample.Frame.Time:F3}s: recorded {sample.Action}, replayed {actual}");
        mismatches++;
    }
    Console.WriteLine($"{recording.Samples.Length} decisions replayed, {mismatches} differences. " +
        $"Incident: {report.RootElement.GetProperty("action").GetString()} ({report.RootElement.GetProperty("reason").GetString()})");
    return recording.Samples.Length > 0 && mismatches == 0 ? 0 : 1;
}

#if INTEGRATION
var etsRoot=Environment.GetEnvironmentVariable("EtsRoot") ?? throw new InvalidOperationException("Set EtsRoot to the ETS2LA installation directory.");
var references=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"../../../../References"));
AssemblyLoadContext.Default.Resolving += (_,name) => {
    foreach(var root in new[]{references,etsRoot}) {
        var file=Path.Combine(root,name.Name+".dll");
        if(File.Exists(file)) return AssemblyLoadContext.Default.LoadFromAssemblyPath(file);
    }
    return null;
};
#endif
Run();
return Environment.ExitCode;

[MethodImpl(MethodImplOptions.NoInlining)]
void Run()
{
    var results=new List<string>();
    void Check(string name, Action test) { if(args is ["--filter",var filter]&&!name.Contains(filter,StringComparison.OrdinalIgnoreCase))return;try {test(); results.Add("PASS "+name);} catch(Exception ex) {results.Add("FAIL "+name+": "+ex); } }
    void Assert(bool v,string message="Assertion failed") {if(!v)throw new Exception(message);}
#if INTEGRATION
    Check("Static road worker resolves installer layout after host shadow copy",()=>StaticRoadWorkerReviewTests.ShadowCopyDirectory(Assert));
    Check("Control tick diagnosis freezes the original blocked producer at pedal failure",()=>ControlTickDiagnosticReviewTests.BlockedProducer(Assert));
    Check("Control tick diagnosis cannot refresh expired decision authority",()=>ControlTickDiagnosticReviewTests.LateDecision(Assert));
    Check("Control tick diagnosis preserves older completion while a newer Tick runs",()=>ControlTickDiagnosticReviewTests.OverlappingTicks(Assert));
    Check("Static road worker preserves original authority across equivalent route refreshes",()=>StaticRoadWorkerClientReviewTests.EquivalentRefresh(Assert));
    Check("Static road worker rejects changed sources routes epochs and expired results",()=>StaticRoadWorkerClientReviewTests.RevocationAndLifecycle(Assert));
    Check("Static road worker revokes longer in-flight corridors across shorter new requests",()=>StaticRoadWorkerClientReviewTests.InFlightLongerHorizon(Assert));
    Check("Static road worker original hit progress survives pruning and clears behind the truck",()=>StaticRoadWorkerClientReviewTests.OriginalHitProgress(Assert));
    Check("Static road worker framed protocol retains original timestamps and identifiers",()=>StaticRoadWorkerReviewTests.FramesAndStamps(Assert));
    Check("Static road worker launch failure stays unknown without host fallback",()=>StaticRoadWorkerReviewTests.ThreadFailure(Assert));
    Check("Static road model selection requires an active collision part",()=>StaticRoadModelReviewTests.Run(Assert));
    Check("Static road source invalidates obsolete map and regional workers",()=>StaticRoadSourceReviewTests.SourceAndRegion(Assert));
    Check("Static road instances reject conditional layers and unknown flags",()=>StaticRoadSourceReviewTests.InstanceEligibility(Assert));
    Check("Static road source retries transient failures and revalidates original snapshots",()=>StaticRoadSourceReviewTests.RetryAndFinalRecheck(Assert));
    Check("Static road auxiliary reads reject incomplete conditional layer data",()=>StaticRoadLayerReviewTests.Run(Assert));
    Check("Static road registry index preserves model identity without full SII graphs",()=>StaticRoadRegistryReviewTests.Run(Assert));
    Check("Static road control uses only positive evidence on roads without toll prefabs",()=>StaticRoadControlReviewTests.PositiveAndUnknown(Assert));
    Check("Static road control rechecks source route and manual override at commit",()=>StaticRoadControlReviewTests.Revocation(Assert));
    Check("Accepted route delay does not create an artificial stop in actual Tick",()=>RouteDecisionIntegration.RecordedDelay(Assert));
    Check("Shared route decisions preserve original deadlines and revocation",()=>RouteDecisionIntegration.Deadlines(Assert));
    Check("Current status never reserializes full historical faults",()=>DiagnosticDeliveryIntegration.CompactFault(Assert));
    Check("Completed straight-road curve preview allows actual Tick propulsion",()=>CurveReadinessReviewTests.StraightRoad(Assert));
    Check("Curve preview distinguishes clear road, finite curve and invalid evaluation",()=>CurveReadinessReviewTests.DistinctOutcomes(Assert));
    Check("Independent route review preserves recovery braking and takeover",()=>RouteDecisionReviewTests.RecoverAndTakeover(Assert));
    Check("Independent route review expires retained planner-pause geometry",()=>RouteDecisionReviewTests.BoundedPause(Assert));
    Check("Independent route review keeps newly completed curve constraints during recovery",()=>RouteDecisionReviewTests.RecoveryUsesCompletedCurve(Assert));
    Check("Accepted route age does not discard fresh open toll evidence",()=>BarrierDecisionAgeReviewTests.ActualTick(Assert));
    Check("Accepted route age preserves planned stationary hold and manual release",()=>BarrierDecisionAgeReviewTests.PlannedStationaryHold(Assert));
    Check("Unissued payment reservation survives only transient eligibility loss",()=>PaymentReservationReviewTests.BeforeFirstIntent(Assert));
    Check("Issued payment and revoked sessions never regain payment permission",()=>PaymentReservationReviewTests.IssuedAndSessionRevocation(Assert));
#endif
    Check("Own longitudinal controller preserves safety, epochs and pedal ownership",()=>LongitudinalControllerTests.SafetyAndOwnership(Assert));
    Check("Nominal static corridor detects positive surfaces without neighbour or ground phantoms",()=>NominalStaticCorridorReviewTests.Geometry(Assert));
    Check("Nominal static corridor preserves slopes and world transforms",()=>NominalStaticCorridorReviewTests.GradesAndWorldPose(Assert));
    Check("Nominal static corridor follows only selected directed route slices",()=>NominalStaticCorridorReviewTests.SelectedRoute(Assert));
    Check("Nominal static corridor bounds work without inventing collision evidence",()=>NominalStaticCorridorReviewTests.WorkAndUnknown(Assert));
    Check("Own longitudinal controller coasts, holds, resumes and rejects missing delivery",()=>LongitudinalControllerTests.CoastStopsAndFeedback(Assert));
    Check("Pedal receipt loss cannot invent an emergency or replay expired braking",()=>DeliveryInterruptionReviewTests.Run(Assert));
    Check("Own longitudinal controller drives loaded and downhill vehicle simulations",()=>LongitudinalControllerTests.Dynamics(Assert));
    Check("Own traffic controller separates lanes, levels, stopped traffic and closing vehicles",()=>TrafficSpeedTests.Run(Assert));
    Check("Independent longitudinal review rejects oncoming and repeated-route geometry errors",()=>OwnLongitudinalReviewTests.TrafficGeometry(Assert));
    Check("Independent longitudinal review detects unapplied holding and full braking",()=>OwnLongitudinalReviewTests.DeliveryDuringHold(Assert));
    Check("Independent throttle receipt review separates simulation cuts, transport loss and pause acknowledgement",()=>OwnLongitudinalReviewTests.ThrottleReceiptAndEffect(Assert));
    Check("Independent parking review separates noisy stand, release, hazards and coherent rollback",()=>OwnLongitudinalReviewTests.ParkingAndReverseEvidence(Assert));
    Check("Native command ages keep producer and observer clock domains separate",()=>NativeCommandTimingTests.ClockDomains(Assert));
    Check("Native command timestamp bounds retain admission and full diagnostic ages",()=>NativeCommandTimingTests.Bounds(Assert));
    Check("Native age watchdog preserves real failure and startup deadlines",()=>NativeCommandTimingTests.GuardContract(Assert));
    Check("Standard exit slicing accepts numerical item handoffs but rejects physical gaps and reversals",()=>StandardSliceHandoffIntegration.NumericalItemBoundaries(Assert));
    Check("Curve planning and supervision share stationary signed-speed policy without losing completed profiles",()=>CurveSpeedTests.StationarySignedSpeed(Assert));
    Check("Simulation dynamics preserve original evidence age across identical provider repetitions",()=>SimulationDynamicsTests.Run(Assert));
    Check("Manual payment intents require exact current zones and cannot repeat consumed gate attempts",()=>ManualPaymentTests.Run(Assert));
    Check("Recorded stand jitter preserves completed synthetic curves and counterfactual parking neutrality",()=>StandstillRegressionTests.Run(Assert));
    Check("Curve geometry excludes grades, item seams and isolated interpolation spikes",()=>CurveSpeedTests.Geometry(Assert));
    Check("Curve speed preview slows ahead without increasing native bounds or stopping a slow truck",()=>CurveSpeedTests.Limits(Assert));
    Check("Delayed 90 km/h approach simulation slows before the curve without hard braking",()=>CurveSpeedTests.ApproachSimulation(Assert));
    Check("Curve preview work is bounded, coalesced and invalidated on takeover",()=>CurveSpeedTests.Worker(Assert));
    Check("Curve preview remains available during faster identical route refreshes",()=>CurveSpeedTests.ContinuousUpdates(Assert));
    Check("Curve preview reuse proves lane, level, future branches, seams and remaining coverage",()=>CurveSpeedTests.CorridorCoverage(Assert));
    Check("Planned toll holds separate observed soft-target incidents from genuine emergencies",()=>ApproachStopTests.RecordedCases(Assert));
    Check("Planned hold survives host response delay and low-speed output dead band",()=>ApproachStopTests.DelayedHold(Assert));
    Check("Physical planned hold is keyed, bounded and revoked on missing authority",()=>PlannedHoldTests.Authority(Assert));
    Check("PMC enclosures cover all variants and reject malformed or unknown collision data",()=>PmcEnvelopeTests.Bounds(Assert));
    Check("Parking separation uses conservative cubic hulls including own-rig reserve",()=>CubicSeparationTests.Bounds(Assert));
    Check("Scene wire authority rejects stale paused malformed and foreign producer frames",()=>SceneSnapshotAdversarialTests.WireAuthority(Assert));
    Check("Scene body and count validation rejects incomplete invalid enclosures",()=>SceneSnapshotAdversarialTests.BodiesAndCounts(Assert));
    Check("Scene coverage binds wanted loaded UID and actual omitted surface range",()=>SceneSnapshotAdversarialTests.LocalScopeAndOverflow(Assert));
    Check("Scene progress cannot be renewed by copying a frozen frame",()=>SceneSnapshotAdversarialTests.Progression(Assert));
    Check("Scene cubic OBB clearance covers continuous curves and rotated parked bodies",()=>SceneSnapshotAdversarialTests.Clearance(Assert));
    Check("Scene replaced sessions and mutated copies cannot resurrect old evidence",()=>SceneSnapshotAdversarialTests.SourceResurrection(Assert));
    Check("Scene rig sphere encloses true hook-coupled full 3D articulations",()=>SceneSnapshotAdversarialTests.CoupledRigBound(Assert));
    Check("Scene physical hold ignores target changes and detects actual vehicle creep",()=>SceneSnapshotAdversarialTests.PhysicalSceneHold(Assert));
    Check("Scene partial current evidence can protect without granting absence clearance",()=>SceneSnapshotAdversarialTests.PartialSafety(Assert));
    Check("Scene real acute hazards and conservative adjacent parking use different braking policies",()=>SceneEmergencyTests.AcuteVersusPrecaution(Assert));
    Check("Prepared unpadded truck corridors retain positive physical intrusion semantics without per-actor sampling",()=>MeasuredTruckCorridorTests.Bounds(Assert));
    Check("Early toll lane planning preserves a normal stop until real barrier binding",()=>TollBarrierBindingTests.Policy(Assert));
    Check("Stationary road geometry cannot renew sources or survive changed pose, navigation, assets or native session",()=>StationaryGroundAdversarialTests.Matching(Assert));
    Check("Trailer axle evidence uses actual fixed contact-wheel pairs and rejects guessed or unsupported geometry",()=>WheelAxleTests.Evidence(Assert));
    Check("Separate truck and trailer predictions enclose the explicit continuous fixed-axle model without an all-articulation disk",()=>ArticulatedRigPredictionTests.Bounds(Assert));
    Check("Parked motion uses actual or observed local sources without global unknown-motion stops",()=>SceneMotionPredictionTests.Bounds(Assert));
    Check("Rig monitor verifies old trajectories, directed phase and contact grace before replacement",()=>RigModelMonitorTests.Bounds(Assert));
    Check("Runtime motion source freshness rejects mutated or discontinuous evidence",()=>RuntimeMotionAdversarialTests.MotionSources(Assert));
    Check("Runtime motion identities cannot revive or mutate old history",()=>RuntimeMotionAdversarialTests.MotionIdentityMutation(Assert));
    Check("Runtime model lifecycle retains cancellation and checked source epochs",()=>RuntimeMotionAdversarialTests.ModelLifecycle(Assert));
    Check("Runtime static sweeps retain narrow walls, real obstacles and conservative work bounds",()=>RuntimeMotionAdversarialTests.StaticSweepBoundary(Assert));
    Check("Observed native pedal equilibrium stops under delayed stronger physical braking",()=>PlannedHoldTests.ObservedCreep(Assert));
    Check("Preferred cruise target obeys configured maxima and known road rules",()=>CruisePolicyTests.Limits(Assert));
    Check("Vehicle bounds require coherent fresh frames and never grant clearance",()=>GeometryBrakeTests.Protocol(Assert));
    Check("Missing brake response requires one second of coherent effective commands",()=>GeometryBrakeTests.Brake(Assert));
    if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MAUT_GEOMETRY_FIXTURE")))
        Check("Native C geometry fixture matches the managed protocol",()=>GeometryBrakeTests.NativeFixture(Assert));
    Check("Manual trigger targets retain the existing safe distance and cannot modify automatic lanes",()=>{
        Assert(new ManualStopZone(11,19).Choose(8)==15);
        Assert(new ManualStopZone(-4,4).Choose(8)==null,"A closer payment zone cannot reduce the safe stop gap");
        Assert(new ManualStopZone(6,12).Choose(8)==9);
        Assert(new ManualStopZone(12,4).Choose(8)==null&&new ManualStopZone(4,double.NaN).Choose(8)==null);
        var options=new TollOptions();
        var normal=new TollController(options).Step(new(0,0,true,true,new("gate",15,BarrierState.Closed)));
        var manual=new TollController(options).Step(new(0,0,true,true,new("gate",15,BarrierState.Closed),true,15));
        Assert(normal.TargetSpeed>0&&manual.TargetSpeed==0,"A validated upstream payment zone stops earlier");
        var retained=new TollController(options);
        retained.Step(new(0,0,true,true,new("gate",15,BarrierState.Closed),true,15));
        Assert(retained.Step(new(.1,0,true,true,new("gate",15,BarrierState.Closed),true)).TargetSpeed==0,
            "Disappearing optional trigger metadata cannot restart creep toward a closed gate");
        var unsafeTarget=new TollController(options).Step(new(0,0,true,true,new("gate",8,BarrierState.Closed),true,2));
        Assert(unsafeTarget.TargetSpeed==0,"Unsafe closer targets must be ignored");
        var auto=new TollController(options).Step(new(0,0,true,true,new("gate",15,BarrierState.Closed),false,15));
        Assert(auto==normal,"Manual metadata cannot change the automatic payment controller");
    });
    Check("Collision cutout requires a fresh local impact and contemporaneous body damage",()=>{
        ImpactFrame Frame(double t,double speed,float damage=.1f)=>new(t,(ulong)(t*1000000),(ulong)(t*1000000),true,false,true,speed,0,0,t*10,new(damage,.1f));
        var guard=new ImpactGuard();
        Assert(guard.Observe(Frame(1,22))==null);
        Assert(guard.Observe(Frame(1.06,.08f,.11f)) is {SpeedLoss:>20},"A physical abrupt stop with new body damage must trip");
        guard.Reset();guard.Observe(Frame(1,22));
        Assert(guard.Observe(Frame(1.06,0))==null,"Existing damage alone cannot turn a stop into a new collision");
        guard.Reset();guard.Observe(Frame(1,22));
        Assert(guard.Observe(Frame(1.06,21.5f,.11f))==null,"Damage without abrupt loss is not this impact signature");
        foreach(var frame in new[]{Frame(2,0,.11f),Frame(1.06,0,.11f) with {X=100},
            Frame(1.06,0,.11f) with {Paused=true},Frame(1.06,0,.11f) with {Sdk=false},
            Frame(1.06,0,.11f) with {Enabled=false},Frame(1.06,0,.11f) with {SimulatedTime=1000000},
            Frame(1.06,0,.11f) with {Damage=new(.11f,float.NaN)}}) {
            guard.Reset();guard.Observe(Frame(1,22));Assert(guard.Observe(frame)==null,"Paused, disabled, stale, invalid or teleport-like frames cannot prove impact");
        }
        guard.Reset();guard.Observe(Frame(1,22));
        Assert(guard.Observe(Frame(1.03,0,.11f) with {GameTime=1000000})==null,"Duplicate SDK sample cannot invent impact");
        Assert(guard.Observe(Frame(1.06,0,.11f))!=null,"Duplicate delivery must not erase the last distinct source sample");
        guard.Reset();
        for(int i=0;i<21;i++)Assert(guard.Observe(Frame(1+i*.05,22-i*.7f,.1f+i*.00001f))==null,"Strong conventional braking must not trip");
    });
    Check("Diagnostic storage coalesces bounded slots, resumes in order and survives write failures",()=>{
        using var entered=new ManualResetEventSlim();using var release=new ManualResetEventSlim();
        var writes=new List<int>();int failures=0;
        var queue=new DiagnosticQueue(_=>Interlocked.Increment(ref failures));
        queue.Enqueue(DiagnosticQueue.Slot.Status,()=>{entered.Set();release.Wait(TimeSpan.FromSeconds(5));lock(writes)writes.Add(-1);});
        Assert(entered.Wait(TimeSpan.FromSeconds(2)),"The first actual write must be held");
        try {
            for(int i=0;i<1000;i++){int sample=i;queue.Enqueue(DiagnosticQueue.Slot.Status,()=>{lock(writes)writes.Add(sample);});}
            queue.Enqueue(DiagnosticQueue.Slot.Fault,()=>throw new IOException("Simulated full disk"));
            queue.Enqueue(DiagnosticQueue.Slot.Preview,()=>{lock(writes)writes.Add(1001);});
        } finally {release.Set();}
        Assert(SpinWait.SpinUntil(()=>queue.IsIdle,TimeSpan.FromSeconds(3)),"The single worker must drain after storage recovers");
        Assert(writes.SequenceEqual(new[]{-1,999,1001})&&failures==1,"Keep only the newest pending status; a failed fault write cannot strand later work");
        entered.Reset();release.Reset();
        queue.Enqueue(DiagnosticQueue.Slot.Status,()=>{entered.Set();release.Wait(TimeSpan.FromSeconds(5));});
        Assert(entered.Wait(TimeSpan.FromSeconds(2)));
        try {
            queue.Enqueue(DiagnosticQueue.Slot.Status,()=>writes.Add(2000));
            queue.Pause();queue.Enqueue(DiagnosticQueue.Slot.Status,()=>writes.Add(3000));
            queue.Resume();queue.Enqueue(DiagnosticQueue.Slot.Status,()=>writes.Add(4000));
        } finally {release.Set();}
        Assert(SpinWait.SpinUntil(()=>queue.IsIdle,TimeSpan.FromSeconds(3)));
        Assert(writes.SequenceEqual(new[]{-1,999,1001,4000}),"Disable discards queued writes; resume cannot let an old worker overtake the new session");
    });
    Check("Continuous status replacement cannot starve a queued fault report",()=>{
        using var first=new ManualResetEventSlim();using var releaseFirst=new ManualResetEventSlim();
        using var second=new ManualResetEventSlim();using var releaseSecond=new ManualResetEventSlim();
        var writes=new List<int>();var queue=new DiagnosticQueue();
        queue.Enqueue(DiagnosticQueue.Slot.Status,()=>{first.Set();releaseFirst.Wait(TimeSpan.FromSeconds(5));writes.Add(1);});
        try {
            Assert(first.Wait(TimeSpan.FromSeconds(2)));
            queue.Enqueue(DiagnosticQueue.Slot.Status,()=>{second.Set();releaseSecond.Wait(TimeSpan.FromSeconds(5));writes.Add(2);});
            queue.Enqueue(DiagnosticQueue.Slot.Fault,()=>writes.Add(3));
            releaseFirst.Set();Assert(second.Wait(TimeSpan.FromSeconds(2)));
            queue.Enqueue(DiagnosticQueue.Slot.Status,()=>writes.Add(4));
        } finally {releaseFirst.Set();releaseSecond.Set();}
        Assert(SpinWait.SpinUntil(()=>queue.IsIdle,TimeSpan.FromSeconds(3)));
        Assert(writes.SequenceEqual(new[]{1,2,3,4}),"A newly arriving status must follow the already waiting fault report");
    });
    Check("Steering supervision has bounded startup, immediate stale-feed loss and explicit reset",()=>{
        var g=new SteeringFeedGuard();
        Assert(g.Check(0,double.NegativeInfinity,true)==SteeringFeedState.Starting);
        Assert(g.Check(.34,double.NegativeInfinity,true)==SteeringFeedState.Starting);
        Assert(g.Check(.36,double.NegativeInfinity,true)==SteeringFeedState.Lost);
        Assert(g.Check(.4,double.NegativeInfinity,false)==SteeringFeedState.Off);
        Assert(g.Check(.5,.5,true)==SteeringFeedState.Healthy);
        Assert(g.Check(.64,.5,true)==SteeringFeedState.Healthy);
        Assert(g.Check(.66,.5,true)==SteeringFeedState.Lost,"Loss after a healthy frame cannot reuse startup grace");
        Assert(g.Check(.7,.7,false)==SteeringFeedState.Off);
        Assert(g.Check(1,double.NaN,true)==SteeringFeedState.Starting);
        Assert(g.Check(double.NaN,1,true)==SteeringFeedState.Lost);
    });
    Check("Connector evidence distinguishes approaching lanes and expires after a stop", () =>
    {
        var memory = new ConnectorHistory();
        for (int i = 1; i <= 12; i++) memory.Observe(i, new System.Numerics.Vector3(0, 0, i), 1);
        var trace = memory.Snapshot(20, new System.Numerics.Vector3(0, 0, 12));
        Assert(trace.Length == 12);
        var sameLane = new RouteTrace([new(0,0,0), new(0,0,30)]);
        var otherLane = new RouteTrace([new(3,0,0), new(0,0,14), new(0,0,30)]);
        Assert(ConnectorHistory.Fit(sameLane, trace) == 0);
        Assert(ConnectorHistory.Fit(otherLane, trace) > 1);
        Assert(double.IsPositiveInfinity(ConnectorHistory.Fit(sameLane, trace.Reverse().ToArray())));
        Assert(memory.Snapshot(110, new System.Numerics.Vector3(0, 0, 12)).Length == 0);
        memory.Observe(25, new System.Numerics.Vector3(0,0,11), -1);
        Assert(memory.Snapshot(25, new System.Numerics.Vector3(0,0,11)).Length == 0);
    });
    Check("Unknown toll approach decelerates by available distance without emergency output",()=>{
        var d=ApproachStop.Evaluate(90/3.6,300);
        Assert(d.MaxAcceleration<-.9f && d.MaxAcceleration>-1.3f);
        Assert(ApproachStop.Evaluate(90/3.6,200).MaxAcceleration<d.MaxAcceleration);
        Assert(ApproachStop.Evaluate(0,277).MaxAcceleration>0,"A verified approach may continue far before the stop boundary");
        Assert(ApproachStop.Evaluate(0,3).MaxAcceleration<0,"Unknown lane must hold at the stop boundary");
        Assert(!d.RequiresTakeover,"Planned halt must not enter fatal tracking path");
    });
    Check("Planned approach stops before the branch and stays stopped",()=>{
        double speed=25,distance=300;
        for(int i=0;i<1200;i++) {
            var d=ApproachStop.Evaluate(speed,distance);
            double next=Math.Max(0,speed+d.MaxAcceleration*.05);
            distance-=(speed+next)*.025;speed=next;
            Assert(distance>0,"Crossed stop line");
        }
        Assert(speed<.1 && distance<30,"Must reach a controlled standstill before the branch");
        Assert(ApproachStop.Evaluate(speed,distance).MaxAcceleration<0,"Hold must persist");
    });
    Check("Too little approach distance or invalid geometry cannot relax the emergency stop",()=>{
        foreach(var scenario in new[]{(25d,60d),(2d,-6d),(0d,-7.01d),(double.NaN,200d),(0d,double.NaN)}) {
            bool rejected=false;
            try {ApproachStop.Evaluate(scenario.Item1,scenario.Item2);}catch(InvalidOperationException){rejected=true;}
            Assert(rejected);
        }
    });
    #if INTEGRATION
    Check("Own Tick drives without native ACC and yields on physical brake and F9",()=>LongitudinalIntegration.TickAndTakeover(Assert));
    Check("Own dynamics use simulation time through actual frame draining",()=>LongitudinalIntegration.SimulationClock(Assert));
    Check("Repeated physics does not pulse brake and expired dynamics cannot publish gas",()=>LongitudinalIntegration.RepeatedPhysics(Assert));
    Check("Own Tick rejects late observation work and cancellation",()=>LongitudinalIntegration.LateWork(Assert));
    Check("Independent own pedal watchdog brakes on a stalled controller with healthy steering",()=>LongitudinalIntegration.PedalDeadman(Assert));
    Check("Open local passage survives missing road speed data while game cruise excludes own takeover",()=>LongitudinalIntegration.RoadLimitAndGameCruise(Assert));
    Check("SDK byte orientations and fixed empty slots preserve real traffic geometry",()=>TrafficWireIntegration.Run(Assert));
    Check("Traffic light instances expose verified spawn flags without defaulting unreadable data to off",()=>TrafficSignalEvidenceTests.InstanceFlags(Assert));
    Check("Unspawned map lights cannot create ghost stops or erase live and canonical barrier evidence",()=>TrafficSignalEvidenceTests.Policy(Assert));
    Check("Actual own Tick uses instance-aware traffic light policy for gas and service brake",()=>TrafficSignalEvidenceTests.TickPolicy(Assert));
    Check("Rail crossing groups require complete unambiguous remapped live post evidence",()=>RailCrossingEvidenceTests.Binding(Assert));
    Check("Rail crossing profiles preserve inheritance arrays and country binding semantics",()=>RailCrossingEvidenceTests.Parser(Assert));
    Check("Rail crossing policy reaches actual Tick without technical fault braking",()=>RailCrossingEvidenceTests.TickPolicy(Assert));
    Check("Actual toll Tick binds open and closed canonical gates independently of the opposite lane",()=>CanonicalGateTickReview.Run(Assert));
    Check("Exit continuation carries a selected toll lane through the next road",()=>RoutingContinuationIntegration.RoadExit(Assert));
    Check("Exit continuation chooses a real directed connector alternative",()=>RoutingContinuationIntegration.ConnectorAlternatives(Assert));
    Check("Exit continuation rejects unresolved downstream joins transactionally",()=>RoutingContinuationIntegration.StrictContinuation(Assert));
    Check("Unconfirmed toll approach retains maneuver space before the physical stop boundary",()=>RoutingContinuationIntegration.AdvanceHold(Assert));
    Check("Early approach hold follows directed curved road arclength",()=>RoutingContinuationIntegration.CurvedHold(Assert));
    Check("Unknown automatic payment evidence is not reported as manual-only",()=>RoutingContinuationIntegration.PaymentStatus(Assert));
    Check("Late automatic entry replaces an early manual fallback only with a valid current maneuver",()=>RoutingContinuationIntegration.LateAutomaticUpgrade(Assert));
    Check("Retained automatic topology loses authority on source changes",()=>RoutingContinuationIntegration.UpgradeSourceRevocation(Assert));
    Check("Own speed regulation prepares and reaches a valid automatic entry in a closed plant",()=>RoutingContinuationIntegration.AutomaticPreparationPlant(Assert));
    Check("Unavailable automatic entries cannot create an endless preparation hold",()=>RoutingContinuationIntegration.AutomaticPreparationUnavailable(Assert));
    Check("Old fault work and publishing refresh cannot outlive manual takeover",()=>FaultLeaseIntegration.Run(Assert));
    Check("Exact host package identity rejects originals and preserves verified Lane diagnostics",()=>HostCompatibilityIntegration.Run(Assert));
    Check("Cruise preset requires sufficient preview and preserves SET/off behaviour",()=>CruiseOptionsIntegration.Cruise(Assert));
    Check("Actual settings UI roundtrip preserves non-displayed options",()=>CruiseOptionsIntegration.UiRoundTrip(Assert));
    Check("Actual ACC curve bound preserves stronger stops, lane-only mode and F9",()=>CurveSpeedIntegration.ActiveBound(Assert));
    Check("Held curve preview cannot block ACC, route delivery or manual takeover",()=>CurveSpeedIntegration.BlockedWorker(Assert));
    Check("Async model snapshots retain detached payment signal geometry",()=>ManualTriggerIntegration.WorkerSnapshot(Assert));
    Check("Missing brake response warns without interrupting control or waiting for routing",()=>BrakeGeometryIntegration.BrakeWarning(Assert));
    Check("Geometry reader is optional, read-only, coherent and session-bound",()=>BrakeGeometryIntegration.Reader(Assert));
    Check("Blocked status geometry cannot stall driving callbacks or manual takeover",()=>DiagnosticDeliveryIntegration.BlockedGeometry(Assert));
    Check("Manual trigger rectangles are matched to a straight gate lane with conservative bounds",()=>ManualTriggerIntegration.Run(Assert));
    Check("Manual payment zones retain exact directed gate identity and world geometry without reducing stop clearance",()=>ManualPaymentTests.RouteZone(Assert));
    Check("Game input context and receipt diagnostics preserve process identity and observation origins",()=>GameInputContextIntegration.Run(Assert));
    Check("Payment journal persists off the driving thread and binds durable acknowledgements to requests",()=>PaymentJournalTests.Run(Assert));
    Check("Actual payment runtime waits off-thread and revokes stale context before its semantic host request",()=>ManualPaymentRuntimeIntegration.Run(Assert));
    Check("Native payment route probes preserve the nonblocking driving boundary",()=>PaymentReviewRegressionTests.RouteProbeKeepsDrivingLockAvailable(Assert));
    Check("Older route callbacks cannot borrow a newer applied payment generation",()=>PaymentReviewRegressionTests.OlderArrivingPathCannotBorrowNewerAppliedGeneration(Assert));
    Check("Legacy toll model registry includes multiple country folders without granting unknown payment",()=>ModelRegistryIntegration.Run(Assert));
    Check("Toll units distinguish legacy manual gates, automatic triggers and uninspected child models",()=>TollDefinitionIntegration.Run(Assert));
    Check("Confirmed impact cuts output before route work and manual takeover cancels pending braking",()=>ImpactSafetyIntegration.Run(Assert));
    Check("The inspected native event bus preserves synchronous route handoff and accepts the current host combination",()=>NativeEventBusIntegration.Run(Assert));
    Check("An empty native error retains detached prior route evidence without relaxing fault braking",()=>RouteFaultEvidenceIntegration.Run(Assert));
    Check("Verified native steering diagnostics are read-only, build-pinned and safe on lookup failures",()=>NativeSteeringDiagnosticsIntegration.Run(Assert));
    Check("Native settings diagnostics read only initialized values and never invoke their factory",()=>NativeSteeringDiagnosticsIntegration.SettingsWithoutInitialization(Assert));
    Check("Valid routes cannot hide missing steering; braking bypasses route locks and manual takeover releases it",()=>SteeringSafetyIntegration.Run(Assert));
    Check("Live steering commands remain visible during busy route work without false dropout braking",()=>SteeringSafetyIntegration.BusyHealthyFeed(Assert));
    Check("A newly delivered game pause is honored even before busy route bookkeeping drains it",()=>SteeringSafetyIntegration.PauseDeliveryWhileBusy(Assert));
    Check("Native steering watchdog uses raw producer authority and rejects stale or future origins",()=>SteeringCommandClockIntegration.RawAuthority(Assert));
    Check("Fresh steering received before trip commit invalidates a stale watchdog snapshot",()=>SteeringCommandClockIntegration.RecheckBeforeTrip(Assert));
    Check("Native acceleration diagnostics are bounded read-only observations off the watchdog",()=>NativeAccDiagnosticsIntegration.BoundedReadOnly(Assert));
    Check("Standard scene tolerates measured stationary signed-speed noise without waiving obstacles",()=>StandardSceneAdmissionIntegration.StationarySignedSpeed(Assert));
    Check("Planner pause handoff handles both message orders without a false emergency latch",()=>PlannerPauseIntegration.Handoff(Assert));
    Check("Planner pause handoff cannot renew stale route authority or conceal real failures",()=>PlannerPauseIntegration.Refusals(Assert));
    Check("Manual brake releases even a pending steering fault before route bookkeeping is available",()=>SteeringSafetyIntegration.BusyManualRelease(Assert));
    Check("Slow native diagnostics cannot delay fault braking, brake refresh or manual acknowledgement",()=>SteeringSafetyIntegration.SlowDiagnostics(Assert));
    Check("Blocked diagnostic storage cannot hold the driving tick, ACC or route delivery",()=>DiagnosticDeliveryIntegration.BlockedStorage(Assert));
    Check("Diagnostic source ages include commands delivered after the driving tick starts",()=>DiagnosticDeliveryIntegration.ConsistentAges(Assert));
    Check("Short item seams do not create false opposite road headings",()=>DirectionSeamIntegration.Run(Assert));
    Check("Automatic preparation attaches only at standstill and preserves fault takeover",()=>AutomaticPreparationIntegration.Run(Assert));
    Check("Rolling F8 validates the latest pose before handoff and can resume after F9",()=>RollingStartIntegration.StartAndTakeover(Assert));
    Check("Unsafe rolling starts leave manual control without emergency braking",()=>RollingStartIntegration.Refusals(Assert));
    Check("Pending rolling starts are cancelled by F9 and cannot commit from foreign threads",()=>RollingStartIntegration.CancelDuringProbe(Assert));
    Check("Physical brake cancels active and pending assists without automatic restart",()=>RollingStartIntegration.BrakeOverride(Assert));
    Check("Telemetry keeps received and effective pedals, clutch and driveline fields distinct",()=>TelemetryPedalMappingIntegration.Run(Assert));
    Check("SDK delivery remains live through slow toll work without weakening actual telemetry-loss braking",()=>InputDeliveryIntegration.Run(Assert));
    Check("Physical brake releases assist outputs immediately even during route computation",()=>InputDeliveryIntegration.BrakeWhileBusy(Assert));
    Check("Duplicate signal objects preserve physical booth identity, payment and conservative clearance",()=>SignalIdentityIntegration.Run(Assert));
    Check("Wide passages require every distinct arm to open, including after route pruning",()=>SignalIdentityIntegration.WidePassage(Assert));
    Check("Static height and width checks distinguish real triangle intersections from bounding boxes",()=>ModelClearanceIntegration.Geometry(Assert));
    Check("Child model LODs, variants and nested transforms retain real obstacles",()=>ChildGeometryIntegration.LodsAndTransforms(Assert));
    Check("Mover sweeps cover full per-LOD skeletal chains, clip blends and malformed data",()=>MoverEnvelopeIntegration.Bounds(Assert));
    Check("Wheel telemetry snapshots preserve reported source geometry independently of later provider mutations",()=>WheelTelemetryIntegration.Snapshot(Assert));
    Check("Runtime rig factory binds the actual final route, moving source phase and measured geometry",()=>RigPredictionRuntimeTests.Factory(Assert));
    Check("Runtime rig cache exposes only current cargo-expanded local planning evidence",()=>RigPredictionRuntimeTests.ExpandedCache(Assert));
    Check("Actual toll control passes narrow moving lanes and recovers local planned holds",()=>RigControlIntegration.MovingAndRecovery(Assert));
    Check("Actual toll control preserves cancellation and ordinary fast approach slowdown",()=>RigControlIntegration.CancellationAndSpeed(Assert));
    Check("Standard toll control decouples unknown cargo from real obstacles and takeover",()=>RigControlIntegration.StandardPolicy(Assert));
    Check("Standard policy migration and stopped changes retire obsolete rig holds",()=>RigControlIntegration.StandardSettings(Assert));
    Check("Standard source transactions retain original freshness and producer epochs",()=>StandardPolicyReviewTests.SourceTransactions(Assert));
    Check("Standard policy isolates old rig faults without weakening other holds or F9",()=>StandardPolicyReviewTests.DetailIsolationAndCancellation(Assert));
    Check("Standard guidance accepts ordinary slopes and retains observed actor motion",()=>StandardPolicyReviewTests.PracticalScopeAndMotion(Assert));
    Check("Standard local scene checks bind current raw sources and real blockers",()=>StandardSceneAdmissionIntegration.CurrentEvidence(Assert));
    Check("Standard route slices retain vertices and grades without extrapolation",()=>StandardSceneAdmissionIntegration.StandardTraceContract(Assert));
    Check("Payment model recognition survives missing detailed clearance",()=>StandardModelEvidenceIntegration.PaymentWithoutClearance(Assert));
    Check("Model metadata cache is independent of cargo profile but bound to assets",()=>StandardModelEvidenceIntegration.ProfileIndependentCache(Assert));
    Check("Failed static geometry cannot grant passage through metadata",()=>StandardModelEvidenceIntegration.FailedGeometryStaysClosed(Assert));
    Check("Metadata reuse stays bound to variant parts special job and descriptor",()=>StandardModelEvidenceIntegration.MetadataIdentity(Assert));
    Check("Current parked scene admission never reuses a prior empty scene as driving permission",()=>ParkedSceneAdmissionIntegration.CurrentEvidence(Assert));
    Check("Early lane choices require model proof and bind real barriers only to the directed selected item",()=>EarlyTollPlanningIntegration.Run(Assert));
    Check("Manual payment zones follow directed navigation geometry rather than backward host metadata",()=>ManualDirectionIntegration.Run(Assert));
    Check("Passed and unbound selected booths preserve correct real controller ownership transitions",()=>BarrierLifecycleIntegration.Transitions(Assert));
    Check("Unknown scene inputs cannot hide an independently measured acute obstacle",()=>ParkedSceneAdmissionIntegration.PositiveHazardsSurviveUnknown(Assert));
    Check("Refined route identities preserve directed canonical curves and reject ambiguous current occurrences",()=>ParkedSceneAdmissionIntegration.CanonicalRouteIdentity(Assert));
    Check("Only completed dominating model certificates can prove smaller unchanged corridors",()=>ModelCertificateAdversarialIntegration.DominanceAndRevocation(Assert));
    Check("A far unknown toll beyond the accepted route gap cannot create an immediate branch stop",()=>EarlyPlanningGapIntegration.RemoteUnknownPlaza(Assert));
    Check("Parking catalogue resolves all same-category extensions, accessories and exclusions",()=>ParkedCatalogIntegration.Selection(Assert));
    Check("Parking passenger closure, transforms, selected masks and cycles retain geometry",()=>ParkedCatalogIntegration.Children(Assert));
    Check("Generated parking visuals require exact classes, schemas and field types",()=>ParkedCatalogIntegration.GeneratedLeaves(Assert));
    Check("Vehicle envelope uses separate world transforms and confirmed ground under both bodies",()=>VehicleEnvelopeIntegration.Frames(Assert));
    Check("Envelope proof cache never bypasses current identity, pose, source or session",()=>VehicleEnvelopeIntegration.CacheAuthority(Assert));
    Check("Auxiliary cargo uses actual transformed models and resolves every accessory",()=>VehicleEnvelopeIntegration.Auxiliary(Assert));
    Check("Unknown, cyclic or malformed child geometry cannot grant clearance",()=>ChildGeometryIntegration.UnsupportedChildren(Assert));
    Check("Renderer-only flares require verified full materials, no collision and no hidden geometry",()=>ChildGeometryIntegration.FlareValidation(Assert));
    Check("Visible additional parts affect geometry independently of payment markers",()=>ModelClearanceIntegration.VisibleParts(Assert));
    Check("Slow model inspection is bounded, detached, cached and nonblocking for route callers",()=>ModelClearanceIntegration.NonBlocking(Assert));
    Check("Lane search finds clear automatic alternatives before rejecting a gate",()=>LaneSearchIntegration.Run(Assert));
    Check("Lane reselection upgrades early applied manual paths and preserves committed lanes",()=>LaneReselectionReviewTests.BeforeAndAfterCommit(Assert));
    Check("Lane reselection failures preserve route ownership",()=>LaneReselectionReviewTests.FailuresRetainOwnership(Assert));
    Check("Connector starts distinguish passed signals from signals and restrictions ahead",()=>ConnectorSignalIntegration.Run(Assert));
    Check("Distant planning gaps do not trip current tracking; nearby gaps still brake",()=>FutureGapIntegration.Run(Assert));
    Check("Tracking faults brake independently of ACC; F9 releases and F8 cannot restart",()=>FaultBrakeIntegration.Run(Assert));
    Check("Toll exit hands over to the connected road lane, not merely a target lane",()=>ExitHandoffIntegration.Run(Assert));
    Check("Curved toll exit remains tracked by the native path projection",()=>ExitProjectionIntegration.Run(Assert));
    Check("Unprepared or faulted F8 cannot silently resume through tick, route, telemetry or ACC",()=>PreparationIntegration.Run(Assert));
    Check("Route measurements isolate PathLib lane-change mutations from native planning",()=>SnapshotIntegration.Run(Assert));
    Check("Synthetic prefab integration: route, right-to-left lanes, tracking, persistence, unknown plaza guard",()=>RouteIntegration.Run(Assert));
    #endif
    Check("Automatic preparation waits for a full second of uninterrupted readiness",()=>{
        var auto=new AutomaticPreparation();
        Assert(!auto.ShouldAttempt(0,true));Assert(!auto.ShouldAttempt(.9,true));
        Assert(!auto.ShouldAttempt(1,false));Assert(!auto.ShouldAttempt(2,true));
        Assert(!auto.ShouldAttempt(2.9,true));Assert(auto.ShouldAttempt(3,true));
    });
    Check("Automatic preparation retries at most once per second until ready",()=>{
        var auto=new AutomaticPreparation();auto.ShouldAttempt(0,true);
        Assert(auto.ShouldAttempt(1,true));Assert(!auto.ShouldAttempt(1.5,true));
        Assert(auto.ShouldAttempt(2,true));Assert(!auto.ShouldAttempt(double.NaN,true));
    });
    Check("Automatic preparation remains cancelled until explicitly requested",()=>{
        var auto=new AutomaticPreparation();auto.ShouldAttempt(0,true);auto.Cancel();
        Assert(!auto.Requested&&!auto.ShouldAttempt(100,true));auto.Request();
        Assert(!auto.ShouldAttempt(101,true));Assert(auto.ShouldAttempt(102,true));
    });
    Check("Only marked short item connectors are excluded from road direction",()=>{
        var points=new System.Numerics.Vector3[]{new(0,0,10),new(0,0,0),new(.2f,0,1),new(.2f,0,-10)};
        var truck=new System.Numerics.Vector3(.1f,0,.5f);
        var actual=new RouteTrace(points);var seam=new RouteTrace(points,new[]{2});
        Assert(new TrackingGuard().Check(actual,truck,0,15)!=null,"Real reversed segment must still stop");
        var guard=new TrackingGuard();Assert(guard.Check(seam,truck,0,15)==null);
        Assert(guard.Check(seam,truck,.5,15)!=null,"Counter-direction remains protected");
        Assert(guard.LastDirectionFailure!=null);
    });
    Check("Connector metadata cannot hide large gaps or erase all driving directions",()=>{
        bool rejected=false;
        try {new RouteTrace(new System.Numerics.Vector3[]{new(0,0,0),new(0,0,3)},new[]{1});}catch(ArgumentException){rejected=true;}
        Assert(rejected);
        var connectorsOnly=new RouteTrace(new System.Numerics.Vector3[]{new(0,0,0),new(0,0,1)},new[]{1});
        Assert(new TrackingGuard().Check(connectorsOnly,System.Numerics.Vector3.Zero,.5,0)!=null);
    });
    Check("Planning horizon grows with speed and retains a minimum reserve",()=>{
        Assert(TrackingGuard.RequiredLookahead(0)==25);
        Assert(TrackingGuard.RequiredLookahead(53/3.6)>80);
        Assert(TrackingGuard.RequiredLookahead(90/3.6)>190);
        Assert(double.IsPositiveInfinity(TrackingGuard.RequiredLookahead(double.NaN)));
    });
    Check("Lane tracking distinguishes vertical reference offset from actual lateral departure",()=>{
        var trace=new RouteTrace([new(0,7.0137f,0),new(0,7.0137f,300)]);
        var truck=new Vector3(.217f,10.010934f,120);
        Assert(trace.Project(truck).Offset>3,"Reproduce the former 3D-distance rejection");
        var projection=trace.ProjectForTracking(truck);
        Assert(projection.Offset<.22&&projection.HeightOffset>2.99);
        Assert(new TrackingGuard().Check(trace,truck,.5,76.365/3.6)==null,"Height must not be counted as leaving the lane");
        Assert(new TrackingGuard().Check(trace,new(3.01f,10.01f,120),.5,20)!=null,"Actual lateral departure still trips tracking");
        Assert(new TrackingGuard().Check(trace,truck,0,20)!=null,"Opposite heading remains rejected");
    });
    Check("Tracking projection retains 3D level selection and handles degenerate traces",()=>{
        var levels=new RouteTrace([new(0,0,0),new(0,0,100),new(10,3,100),new(10,3,0),new(0,3,0),new(0,3,100)]);
        var projection=levels.ProjectForTracking(new(0,3,50));
        Assert(projection.Along>200&&projection.Offset<.001&&Math.Abs(projection.HeightOffset)<.001);
        Assert(new TrackingGuard().Check(new RouteTrace([Vector3.Zero,Vector3.Zero]),Vector3.Zero,0,0)!=null);
    });
    Check("Tracking guard rejects opposite headings and reversed replacement routes",()=>{
        var north=new RouteTrace(new System.Numerics.Vector3[]{new(0,0,20),new(0,0,-100)});
        var south=new RouteTrace(new System.Numerics.Vector3[]{new(0,0,-100),new(0,0,20)});
        var guard=new TrackingGuard();
        Assert(guard.Check(north,System.Numerics.Vector3.Zero,0,10)==null);
        Assert(guard.Check(north,System.Numerics.Vector3.Zero,.5,10)!=null,"Opposite heading must not be folded onto same road axis");
        Assert(guard.Check(south,System.Numerics.Vector3.Zero,.5,10)!=null,"A replan must not silently reverse accepted travel direction");
        guard.Reset();Assert(guard.Check(south,System.Numerics.Vector3.Zero,.5,10)==null,"Manual takeover may start a new journey");
        Assert(guard.Check(south,new(4,0,0),.5,10)!=null,"Lost lateral tracking must stop even with no gate nearby");
        Assert(guard.Check(south,System.Numerics.Vector3.Zero,.5,-2)!=null,"Reverse gear must not drive under autopilot");
        Assert(guard.Check(south,System.Numerics.Vector3.Zero,double.NaN,10)!=null);
    });
    Check("Tracking direction follows bends instead of comparing only with initial compass heading",()=>{
        var route=new RouteTrace(new System.Numerics.Vector3[]{new(0,0,20),new(0,0,0),new(20,0,0),new(20,0,20)});
        var guard=new TrackingGuard();
        Assert(guard.Check(route,new(0,0,10),0,5)==null);
        Assert(guard.Check(route,new(10,0,0),.75,5)==null);
        Assert(guard.Check(route,new(20,0,10),.5,5)==null);
    });
    Check("Automatic payment preference excludes manual, unknown and unreachable lanes",()=>{
        var payment=new Dictionary<string,TollPayment>{[PaymentPolicy.Key("R",4)]=TollPayment.Manual,
            [PaymentPolicy.Key("R",12)]=TollPayment.Automatic};
        Assert(PaymentPolicy.ChooseAutomatic("R",new[]{4,12},4,payment)==12);
        Assert(PaymentPolicy.ChooseAutomatic("R",new[]{4},4,payment)==null);
        Assert(PaymentPolicy.ChooseAutomatic("other",new[]{4,12},12,payment)==null);
        Assert(PaymentPolicy.ChooseAutomatic("R",new[]{15},15,payment)==null);
    });
    Check("Legacy lane settings never become automatic payment without confirmation",()=>{
        var file=Path.Combine(AppContext.BaseDirectory,"legacy-payment-test.json");
        File.WriteAllText(file,"{\"Options\":{},\"Gates\":{\"R\":12}}");
        try {var prefs=new PreferenceStore(file);prefs.Load();
            Assert(prefs.Gates["R"]==12 && prefs.Payments.Count==0);
            Assert(PaymentPolicy.ChooseAutomatic("R",new[]{12},12,prefs.Payments)==null);
        } finally {File.Delete(file);}
    });
    Check("Persistent settings migration preserves confirmed lanes and never overwrites an existing store",()=>{
        var legacyFile=Path.Combine(AppContext.BaseDirectory,"migration-old-settings.json");
        var newFile=Path.Combine(AppContext.BaseDirectory,"migration-new-settings.json");
        try {
            var legacy=new PreferenceStore(legacyFile);
            legacy.Save(new TollOptions{ApproachSpeedKph=18},new(){["synthetic"]=12},new(){[PaymentPolicy.Key("synthetic",12)]=TollPayment.Automatic});
            var persistent=new PreferenceStore(newFile);persistent.MigrateFrom(legacyFile);persistent.Load();
            Assert(persistent.Options.ApproachSpeedKph==18 && persistent.Gates["synthetic"]==12);
            Assert(persistent.Payments[PaymentPolicy.Key("synthetic",12)]==TollPayment.Automatic && File.Exists(legacyFile));
            File.WriteAllText(legacyFile,"invalid");persistent.MigrateFrom(legacyFile);persistent.Load();
            Assert(persistent.Gates["synthetic"]==12,"Existing preferences take precedence over a legacy copy");
            File.Delete(newFile);bool rejected=false;
            try {persistent.MigrateFrom(legacyFile);}catch(System.Text.Json.JsonException){rejected=true;}
            Assert(rejected&&!File.Exists(newFile),"Never persist an invalid legacy preference file");
        } finally {File.Delete(legacyFile);File.Delete(newFile);}
    });
    TollInput Input(double t, double speed=0, double distance=8, BarrierState state=BarrierState.Closed, string key="A", bool enabled=true, bool fresh=true) => new(t,speed,enabled,fresh,new(key,distance,state));
    Check("Passed gate can close behind the truck without ordering another stop",()=>{
        var c=new TollController(new());
        for(int n=0;n<=20;n++)c.Step(Input(n*.1,distance:8,state:BarrierState.Open));
        var crossing=c.Step(Input(2.1,distance:-2,state:BarrierState.Open));Assert(crossing.Phase=="Langsame Durchfahrt");
        var closing=c.Step(Input(2.2,distance:-10,state:BarrierState.Closing));Assert(closing.TargetSpeed==10/3.6);
        Assert(c.Step(Input(2.3,distance:-31,state:BarrierState.Closed)).GateKey==null);
        Assert(c.Step(Input(2.4,distance:8,state:BarrierState.Closed,key:"B")).TargetSpeed==0);
    });
    Check("Passage memory survives route pruning only behind a released, identical gate",()=>{
        var memory=new PassageMemory();
        var route=new RouteTrace(new[]{new System.Numerics.Vector3(0,0,-100),new System.Numerics.Vector3(0,0,100)});
        memory.Capture("A",System.Numerics.Vector3.Zero,route);
        var truck=new System.Numerics.Vector3(0,0,12);
        var exit=memory.AfterCrossing("A",truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,true);
        Assert(exit!=null && Math.Abs(exit.Distance+12)<.001);
        Assert(memory.AfterCrossing("A",-truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,true)==null);
        Assert(memory.AfterCrossing("A",truck,System.Numerics.Vector3.Zero,BarrierState.Open,false,true)==null);
        Assert(memory.AfterCrossing("A",truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,false)==null);
        Assert(memory.AfterCrossing("B",truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,true)==null);
        Assert(memory.AfterCrossing("A",truck+new System.Numerics.Vector3(5,0,0),System.Numerics.Vector3.Zero,BarrierState.Open,true,true)==null);
        memory.Reset();Assert(memory.AfterCrossing("A",truck,System.Numerics.Vector3.Zero,BarrierState.Open,true,true)==null);
    });
    Check("Manual cancellation clears a lost old booth before observing the next plaza",()=>{
        var c=new TollController(new());c.Step(Input(0));
        Assert(c.Step(new(.1,0,true,true,null)).GateKey=="A");
        c.Step(new(.2,0,false,false,null));Assert(!c.Tracking);
        Assert(c.Step(Input(.3,distance:100,key:"B")).GateKey=="B");
    });
    Check("Synthetic plaza: both entries reach the requested gate without crossing carriageways",()=>{
        CurveLink[] curves=[
            new(0,[2,3],20,true,-1), new(1,[2,3],20,true,-1),
            new(2,[4],10,true,12), new(3,[4],10,true,4),
            new(4,[],20,true,-1), new(5,[6],20,true,-1), new(6,[],20,true,-1)
        ];
        foreach(int start in new[]{0,1}) {
            var route=CurveGraph.ViaGate(curves,start,4,12);Assert(route!=null,"No truck path from entry "+start);
            Assert(route!.Where(i=>curves[i].GateId>=0).Select(i=>curves[i].GateId).SequenceEqual(new[]{12}));
        }
        Assert(CurveGraph.ViaGate(curves,5,6,12)==null,"Opposite carriageway must not be crossed");
        var blocked=curves.Select(c=>c.GateId==12?c with{TruckAllowed=false}:c).ToArray();
        Assert(CurveGraph.ViaGate(blocked,0,4,12)==null,"No fallback to a car-only or different gate");
    });
    Check("Curve projection follows the lane instead of the straight chord",()=>{
        var points=Enumerable.Range(0,201).Select(i=> {
            double a=i*Math.PI/400;return new System.Numerics.Vector3((float)(30*Math.Sin(a)),0,(float)(30*Math.Cos(a)));
        }).ToArray();
        var route=new RouteTrace(points);var truck=route.Project(points[100]);var barrier=route.Project(points[180]);
        Assert(truck.Offset<.001);Assert(Math.Abs((barrier.Along-truck.Along)-30*Math.PI/5)<.01);
        Assert(route.Project(points[190]).Along>barrier.Along,"Distance must become negative after passing");
    });
    Check("Closed and opening barriers hold",()=>{
        var c=new TollController(new());
        foreach(var state in new[]{BarrierState.Closed,BarrierState.Closing,BarrierState.Opening,BarrierState.Unknown}) {
            var d=c.Step(Input(0,state:state)); Assert(d.TargetSpeed==0 && d.MaxAcceleration<0);
        }
    });
    Check("Open delay requires continuous fresh frames",()=>{
        var c=new TollController(new());
        for(int i=0;i<20;i++) Assert(c.Step(Input(i*.1,state:BarrierState.Open)).TargetSpeed==0);
        Assert(c.Step(Input(2,state:BarrierState.Open)).Phase=="Langsame Durchfahrt");
        Assert(c.Step(Input(2.1,state:BarrierState.Opening)).TargetSpeed==0);
        Assert(c.Step(Input(2.2,state:BarrierState.Open)).TargetSpeed==0);
    });
    Check("Time jump cannot skip opening delay",()=>{
        var c=new TollController(new()); c.Step(Input(0,state:BarrierState.Open));
        Assert(c.Step(Input(3,state:BarrierState.Open)).TargetSpeed==0);
        Assert(c.Step(Input(1,state:BarrierState.Open)).TargetSpeed==0);
    });
    Check("F9/reset yields no constraint",()=>{
        var c=new TollController(new()); c.Step(Input(0));
        Assert(float.IsNaN(c.Step(Input(.1,enabled:false)).MaxAcceleration)); Assert(!c.Tracking);
        Assert(c.Step(Input(.2,state:BarrierState.Open)).TargetSpeed==0);
    });
    Check("Data loss never grants clearance",()=>{
        var c=new TollController(new());
        for(int i=0;i<=20;i++)c.Step(Input(i*.1,state:BarrierState.Open));
        var stale=c.Step(Input(2.1,state:BarrierState.Open,fresh:false)); Assert(stale.MaxAcceleration<0);
        Assert(c.Step(Input(2.2,state:BarrierState.Open)).TargetSpeed==0);
        Assert(c.Step(new(2.3,0,true,true,null)).RequiresTakeover);
    });
    Check("A different gate cannot release the tracked gate",()=>{
        var c=new TollController(new()); c.Step(Input(0));
        Assert(c.Step(Input(.1,state:BarrierState.Open,key:"B")).RequiresTakeover);
    });
    Check("Exit and reverse release tracking",()=>{
        var c=new TollController(new());c.Step(Input(0));
        Assert(float.IsNaN(c.Step(Input(.1,distance:-31)).MaxAcceleration));Assert(!c.Tracking);
        c.Step(Input(.2)); Assert(float.IsNaN(c.Step(Input(.3,speed:-1)).MaxAcceleration));
    });
    Check("Reject non-finite and out-of-range settings",()=>{
        foreach(var o in new[]{new TollOptions{OpenDelaySeconds=double.NaN},new TollOptions{PassageSpeedKph=50},new TollOptions{StopOffsetMetres=0}}) {
            bool thrown=false;try{o.Validate();}catch(ArgumentException){thrown=true;}Assert(thrown);
        }
    });
    Check("A distant booth and a distant route gap slow the truck before they become urgent",()=>{
        double speed=25;
        var decision=new TollController(new()).Step(Input(0,speed:speed,distance:330));
        Assert(decision.MaxAcceleration is >= -.65f and <= 0);
        double ahead=410;
        var delayed=new Queue<float>(Enumerable.Repeat(0f,20));
        for(int i=0;i<4000 && speed>.01;i++) {
            Assert(ahead>TrackingGuard.RequiredLookahead(speed),"A planned slowdown must stay ahead of the hard-stop threshold");
            delayed.Enqueue(ApproachStop.BeforeRouteGap(speed,ahead));
            speed=Math.Max(0,speed+delayed.Dequeue()*.05);
            ahead-=speed*.05;
        }
        Assert(speed<=.01 && ahead is > 25 and < 40);
    });
    Check("Simulated approaches stop ahead of barrier from 30/60/90 km/h",()=>{
        foreach(double initial in new[]{30d,60d,90d}) {
            var c=new TollController(new());double v=initial/3.6, d=400;var delayed=new Queue<float>(Enumerable.Repeat(0f,20));
            for(int i=0;i<6000;i++) {
                var dec=c.Step(Input(i*.05,speed:v,distance:d));
                float bound=dec.MaxAcceleration;
                delayed.Enqueue(float.IsNaN(bound)?0:bound);
                var accel=delayed.Dequeue();v=Math.Max(0,v+accel*.05);d-=v*.05;
                Assert(d>=7.5,$"{initial}: crossed stop point at {d:F2}m");
                if(v<.01 && d<15)break;
            }
            Assert(v<.01 && d<15,$"{initial}: failed to stop, v={v}, d={d}");
            results.Add($"  simulation {initial} km/h: stop at {d:F2} m from telemetry origin (1 s actuation delay)");
        }
    });
    Check("Gate selection excludes adjacent lanes and refuses ambiguous booths",()=>{
        var lane=new GateProjection(new("A",50,BarrierState.Closed),1);
        var adjacent=new GateProjection(new("B",49,BarrierState.Open),3);
        Assert(GateSelection.Select([lane,adjacent],null)?.Key=="A");
        Assert(GateSelection.Select([lane,adjacent with{LateralOffset=1.8}],null)==null);
        Assert(GateSelection.Select([lane,adjacent with{LateralOffset=1.8}],"A")?.Key=="A");
        Assert(GateSelection.Select([adjacent],"A")==null);
    });
    Check("Recorded 163 m approach is limited from standstill, without an acceleration gap",()=>{
        var c=new TollController(new());
        var d=c.Step(Input(0,distance:163.227));
        Assert(c.Tracking && d.TargetSpeed<=20/3.6 && d.MaxAcceleration<=.35f);
    });
    Check("Recorded toll plaza selects mapped gate 15, excluding neighbouring gate 14",()=>{
        var mapped = new GateProjection(new("15",163.227,BarrierState.Closed),2.663,true);
        var neighbour = new GateProjection(new("14",163.229,BarrierState.Open),3.108);
        Assert(GateSelection.Select([neighbour,mapped],null)?.Key=="15");
        Assert(GateSelection.Select([neighbour,mapped with{RouteMatched=false}],null)==null);
        Assert(GateSelection.Select([mapped with{LateralOffset=6}],null)==null);
        Assert(GateSelection.Select([mapped,neighbour with{RouteMatched=true}],null)==null);
    });
    Check("Behind-gate selection only keeps a previously tracked barrier",()=>{
        var behind=new GateProjection(new("A",-12,BarrierState.Open),1);
        Assert(GateSelection.Select([behind],null)==null);
        Assert(GateSelection.Select([behind],"A")!=null);
    });
    Check("Standstill start waits for loading and rejects an unavailable starting road without an emergency stop",()=>{
        var frame = new DriveFrame(0, 0, true, false, true, RouteFeed.Loading, .01, false, false);
        var control = new DriveSupervisor();
        Assert(control.Step(frame) == DriveAction.AwaitRoute);
        Assert(control.Step(frame with { Time = 6 }) == DriveAction.AwaitRoute);
        Assert(control.Step(frame with { Time = 8 }) == DriveAction.RejectStart);
        control.Reset();
        Assert(control.Step(frame with { Route = RouteFeed.Rejected }) == DriveAction.RejectStart);
        Assert(control.Step(frame with { Speed = 10, Route = RouteFeed.Rejected }) == DriveAction.EmergencyStop);
    });
    Check("Stale disabled path rejects a rolling start without braking",()=>{
        var input = new DriveFrame(0, 29.4 / 3.6, true, false, true, RouteFeed.AssistsOff, .031, false, false);
        var control = new DriveSupervisor();
        Assert(control.Step(input) == DriveAction.RejectStart);
        control.Reset();
        Assert(control.Step(input with { Speed = 0 }) == DriveAction.AwaitRoute);
        var ready = input with { Time = .1, Route = RouteFeed.Valid, DecisionReady = true };
        Assert(control.Step(ready) == DriveAction.Drive);
        Assert(control.Step(input with { Time = .2 }) == DriveAction.EmergencyStop);
    });
    Check("Planner pause transition uses accepted-route age, preserves failures and excludes paused wall time",()=>{
        var ready=new DriveFrame(0,16,true,false,true,RouteFeed.Valid,.01,true,true);
        var pause=ready with {Time=.1,Route=RouteFeed.PlannerPaused,RouteAge=.1,DecisionReady=false};
        var control=new DriveSupervisor();Assert(control.Step(ready)==DriveAction.Drive);
        Assert(control.Step(pause)==DriveAction.Recover);
        Assert(control.Step(pause with {Time=.15,Paused=true})==DriveAction.Paused);
        Assert(control.Step(ready with {Time=60})==DriveAction.Drive);
        foreach(var fault in new[]{pause with {RouteAge=.2},pause with {RouteAge=double.NaN},
            pause with {RouteAge=-.1},pause with {CanRecover=false},pause with {TelemetryFresh=false},
            pause with {Route=RouteFeed.Rejected},pause with {Route=RouteFeed.Loading},
            pause with {Route=RouteFeed.Waiting},pause with {Route=RouteFeed.AssistsOff}}) {
            var guard=new DriveSupervisor();guard.Step(ready);
            Assert(guard.Step(fault)==DriveAction.EmergencyStop);
        }
        var repeated=new DriveSupervisor();repeated.Step(ready);repeated.Step(pause);
        Assert(repeated.Step(pause with {Time=.201,RouteAge=.201})==DriveAction.EmergencyStop);
        var longPause=new DriveSupervisor();longPause.Step(ready);longPause.Step(pause with {Paused=true});
        Assert(longPause.Step(pause with {Time=60,RouteAge=60})==DriveAction.EmergencyStop);
        Assert(new DriveSupervisor().Step(pause with {Speed=0})==DriveAction.RejectStart);
        Assert(control.Step(pause with {Enabled=false})==DriveAction.Idle);
    });
    Check("Brief input loss requires continuous recovery and resets after manual cancellation",()=>{
        var ready = new DriveFrame(0, 16, true, false, true, RouteFeed.Valid, .01, true, true);
        var control = new DriveSupervisor();
        Assert(control.Step(ready) == DriveAction.Drive);
        Assert(control.Step(ready with { Time = .05, DecisionReady = false }) == DriveAction.Recover);
        var resumed = new DriveSupervisor(control.Checkpoint);
        foreach (var (time, fresh, expected) in new[]{
            (.10, true, DriveAction.Recover), (.20, false, DriveAction.Recover),
            (.22, true, DriveAction.Recover), (.38, true, DriveAction.Drive)
        })
        {
            var input = ready with { Time = time, DecisionReady = fresh };
            Assert(control.Step(input) == expected && resumed.Step(input) == expected);
        }
        Assert(control.Step(ready with { Time = .40, Enabled = false, TelemetryFresh = false }) == DriveAction.Idle);
        Assert(control.Step(ready with { Time = .41, Speed = 0, Route = RouteFeed.Waiting }) == DriveAction.AwaitRoute);
    });
    Check("A fresh toll decision ends recovery immediately and applies the normal constraint",()=>{
        var ready = new DriveFrame(0, 81.4 / 3.6, true, false, true, RouteFeed.Valid, .030, true, true);
        var gap = ready with { Time = .320, RouteAge = .001, DecisionReady = false };
        var approach = ready with { Time = .323, RouteAge = .003, CanRecover = false };
        var control = new DriveSupervisor();
        Assert(control.Step(ready) == DriveAction.Drive);
        Assert(control.Step(gap) == DriveAction.Recover);
        var interrupted = control.Checkpoint;
        Assert(control.Step(approach) == DriveAction.Drive);
        Assert(control.Checkpoint.Interrupted == null);
        foreach (var fault in new[]{
            approach with { DecisionReady = false },
            approach with { Route = RouteFeed.Rejected },
            approach with { RouteAge = 1 },
            approach with { Time = .821 }
        })
            Assert(new DriveSupervisor(interrupted).Step(fault) == DriveAction.EmergencyStop);
    });
    Check("Loss of the verified corridor, telemetry, or a toll safety input cannot use the recovery window",()=>{
        var ready = new DriveFrame(0, 25, true, false, true, RouteFeed.Valid, .01, true, true);
        foreach (var fault in new[]{
            ready with { Route = RouteFeed.Rejected },
            ready with { TelemetryFresh = false },
            ready with { DecisionReady = false, CanRecover = false },
            ready with { RouteAge = 1 }
        })
        {
            var control = new DriveSupervisor();
            Assert(control.Step(ready) == DriveAction.Drive);
            Assert(control.Step(fault with { Time = .1 }) == DriveAction.EmergencyStop);
        }
        var timeout = new DriveSupervisor();
        timeout.Step(ready);
        Assert(timeout.Step(ready with { Time = .1, DecisionReady = false }) == DriveAction.Recover);
        Assert(timeout.Step(ready with { Time = .61, DecisionReady = false }) == DriveAction.EmergencyStop);
        var toll = new DriveSupervisor();
        Assert(toll.Step(ready with { CanRecover = false }) == DriveAction.Drive);
        Assert(toll.Step(ready with { Time = .6, RouteAge = .6, CanRecover = false }) == DriveAction.Drive);
        Assert(toll.Step(ready with { Time = 1, RouteAge = 1, CanRecover = false }) == DriveAction.EmergencyStop);
    });
    #if INTEGRATION
    Check("Verified ACC type accepts bridge; attach/detach preserves native constraints",()=>{
        Assert(AccBridge.SupportsVersion("2.1.0")&&AccBridge.SupportsVersion("2.1.1")&&!AccBridge.SupportsVersion("2.2.0"),"Reject unverified ACC versions");
        var a=AssemblyLoadContext.Default.LoadFromAssemblyPath(Path.Combine(references,"AdaptiveCruiseControl.dll"));
        var baseType=a.GetType("AdaptiveCruiseControl.Constraints.Constraint",true)!;
        var accType=a.GetType("AdaptiveCruiseControl.AdaptiveCruiseControlPlugin",true)!;
        var gate=AccBridge.CreateConstraint(baseType,()=>-.75f,()=>"test");
        Assert((float)baseType.GetMethod("GetMaxAcceleration")!.Invoke(gate,null)! == -.75f);
        var log=baseType.GetMethod("GetLogData")!.Invoke(gate,null)!;
        Assert((string)log.GetType().GetProperty("ConstraintName")!.GetValue(log)! == AccBridge.ConstraintName);
        // No ACC constructor, providers, worker threads or game writes are invoked.
        var acc=RuntimeHelpers.GetUninitializedObject(accType);
        var field=accType.GetField("constraints")!;
        var list=(IList)Activator.CreateInstance(field.FieldType)!;
        list.Add(gate);list.Add(gate);list.Add(gate);field.SetValue(acc,list);
        var bridge=new AccBridge();bridge.Attach(acc,()=>-.5f,()=>"active");
        var attached=bridge.Constraint!;
        Assert(((IList)field.GetValue(acc)!).Count==4 && list.Count==3);
        bridge.Attach(acc,()=>1,()=>"duplicate");Assert(((IList)field.GetValue(acc)!).Count==4);
        bridge.Detach();Assert(((IList)field.GetValue(acc)!).Count==3);
        Assert(float.IsNaN((float)baseType.GetMethod("GetMaxAcceleration")!.Invoke(attached,null)!));
    });
    #endif
    foreach(var line in results)Console.WriteLine(line);
    Environment.ExitCode=results.Any(s=>s.StartsWith("FAIL"))?1:0;
}
