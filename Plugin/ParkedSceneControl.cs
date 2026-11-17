using ETS2LA.Game.Output;
using System.Reflection;
namespace MautAssistent;
public sealed partial class TollPlugin
{
    readonly ParkedSceneReader parkedScene=new();
    readonly SceneStopHold sceneHold=new();
    readonly TollModelEvidence? modelEvidence;
    bool sceneStopping;
    string sceneStatus="Parkfahrzeug-Prüfung noch nicht benötigt.";
    BarrierBindingResult? barrierBinding;
    public string ParkedSceneStatus=>sceneStatus+" · "+parkedScene.Status;
    Gate? ApplyBarrierBinding(Gate? gate,BarrierBindingResult binding)
    {
        if(binding.Passed&&router.SelectedBarrier is { } plan&&
            tracked==FormattableString.Invariant($"{plan.GateId}:{plan.Anchor.X:F1}:{plan.Anchor.Z:F1}")) {
            string completed=tracked;controller.Reset();tracked=null;passage.Reset();
            if(gate?.Key==completed)gate=null;
        } else if(binding.LiveGate is { } bound) {
            if(tracked!=null&&tracked!=bound.Key&&passage.PositionFor(tracked) is System.Numerics.Vector3 oldAnchor&&
                binding.LiveSample is { } sample&&System.Numerics.Vector3.Distance(oldAnchor,sample.GetWorldCoordinates())<=1) {
                controller.Reset();tracked=null;passage.Reset();
            }
            gate=bound;
            if(binding.LiveSample!=null&&safetyTrace!=null)passage.Capture(gate.Key,binding.LiveSample.GetWorldCoordinates(),safetyTrace);
        } else if(binding.LanePlanned&&!binding.Passed&&!binding.LiveBarrierBound) {
            // Missing live state of the same mapped booth is a normal bound
            // stop. An unrelated raw SDK gate must not seed a different owner.
            controller.Reset();tracked=null;passage.Reset();gate=null;
        }
        candidate=gate;return gate;
    }
    BarrierBindingResult ReadBarrierBinding()
    {
        double now=clock.Elapsed.TotalSeconds;
        var plan=router.SelectedBarrier;
        if(plan!=null&&path?.PlannedPath.Any(i=>i.prefabPath?.Prefab.Prefab.Uid==plan.PrefabUid)!=true)plan=null;
        return BarrierBinding.Check(plan,now-lastAcceptedPlanAt<.2?safetyTrace:null,position,speed,gates,
            SensorsFresh(now)&&now-gatesAt<.2,options);
    }
    static readonly FieldInfo? trafficAt=typeof(GameOutput).GetField("MautTrafficAt"),
        parkedAt=typeof(GameOutput).GetField("MautParkedAt"),semaphoreAt=typeof(GameOutput).GetField("MautSemaphoreAt");
    static readonly FieldInfo? trafficData=typeof(GameOutput).GetField("MautTrafficData"),
        parkedData=typeof(GameOutput).GetField("MautParkedData"),semaphoreData=typeof(GameOutput).GetField("MautSemaphoreData");
    static bool HostTrafficFresh()
    {
        long now=System.Diagnostics.Stopwatch.GetTimestamp();
        foreach(var pair in new[]{(trafficAt,trafficData),(parkedAt,parkedData),(semaphoreAt,semaphoreData)}) {
            if(pair.Item1?.GetValue(null) is not long at||at<=0||now-at<0||now-at>System.Diagnostics.Stopwatch.Frequency*.2||pair.Item2?.GetValue(null)==null)return false;
        }
        return true;
    }
    float LimitForParkedScene(float original)
    {
        if(path==null||safetyTrace==null)return original;
        // Capture output generation before any source/geometry work. Publishing
        // a hold after F9 or mode change cannot acquire the newer generation.
        PathLib.PlannedItem[] prefabs=modelEvidence==null?[]:path.PlannedPath.Where(i=>i.prefabPath!=null).Where(i=>{
            bool? required=modelEvidence.ParkedRequirement(i.prefabPath!.Prefab);
            return required.HasValue||router.CurrentKey?.StartsWith(i.prefabPath.Prefab.Prefab.Uid+":",StringComparison.Ordinal)==true;
        }).ToArray();
        var extra=new List<ParkedSceneAdmission.Result>();
        barrierBinding=ReadBarrierBinding();
        if(barrierBinding.LanePlanned&&!barrierBinding.Passed&&!barrierBinding.LiveBarrierBound&&float.IsFinite(barrierBinding.Decision.MaxAcceleration))
            extra.Add(new(false,barrierBinding.Decision.Phase,Math.Max(0,(barrierBinding.Distance??0)-options.StopOffsetMetres))
                {HoldKey=barrierBinding.HoldKey??"mapped-barrier-unresolved",AccelerationLimit=barrierBinding.Decision.MaxAcceleration});
        if(router.ClearanceHoldRequested)
            extra.Add(new(false,"Aktuelle Fahrzeug-Durchfahrtsprüfung noch nicht bestätigt – geplanter Halt",0){HoldKey="proof:"+(router.ClearanceHoldKey??router.CurrentKey)});
        if(safetyTrace.EndsAtGap) {
            double remaining=safetyTrace.Length-safetyTrace.ProjectForTracking(position).Along;
            var end=safetyTrace.End;
            extra.Add(new(false,"Geplanter Halt vor einer nicht verbundenen Kartenstrecke",Math.Max(0,remaining-TrackingGuard.RequiredLookahead(0)-10))
                {HoldKey=FormattableString.Invariant($"route-gap:{end.X:F1}:{end.Y:F1}:{end.Z:F1}"),AccelerationLimit=ApproachStop.BeforeRouteGap(speed,remaining)});
        }
        if(controller.Tracking&&!controller.Released&&candidate is { } gate&&decision.TargetSpeed.HasValue) {
            double offset=router.SelectedGate is int id&&router.PaymentFor(id)==TollPayment.Manual?
                router.ManualStopOffset(options.StopOffsetMetres)??options.StopOffsetMetres:options.StopOffsetMetres;
            extra.Add(new(false,decision.Phase,Math.Max(0,gate.Distance-offset))
                {HoldKey="live-barrier:"+gate.Key,AccelerationLimit=decision.MaxAcceleration});
        }
        if(prefabs.Length==0&&extra.Count==0){sceneStatus="Keine aktuelle Maut-Parkfahrzeug-Begrenzung.";ClearSceneHold();return original;}
        var sceneChecks=EvaluateRigChecks(prefabs);
        if(sceneChecks.Any(c=>SceneStopPolicy.NeedsEmergency(speed,c.AcuteDistance))) {
            Disarm("Aktuell gemessenes Fahrzeug im unmittelbaren Fahrkorridor");return -5f;
        }
        var checks=sceneChecks.Where(c=>!c.Clear).Concat(extra).OrderBy(c=>c.StopDistance).ToArray();
        if(checks.Length==0){sceneStatus=options.DetailedRigProtection?
            "Aktuelle Maut-Parkfahrzeuge außerhalb der Gespannhülle.":
            "Aktuelle Maut-Parkfahrzeuge außerhalb des Standard-Spurkorridors; Ladungshülle nicht bestätigt.";ClearSceneHold();return original;}
        var holds=checks.Where(c=>c.RequiresPhysicalHold).ToArray();
        var check=sceneHold.Holding?holds.FirstOrDefault(c=>c.HoldKey==sceneHold.OwnerKey)??holds.FirstOrDefault():holds.FirstOrDefault();
        sceneStopping=true;sceneStatus=(check??checks[0]).Reason;
        if(checks.Any(c=>SceneStopPolicy.NeedsEmergency(speed,c.AcuteDistance))) {
            Disarm("Aktuell gemessenes stehendes Fahrzeug im unmittelbaren Fahrkorridor: normaler Bremsweg reicht nicht.");return -5f;
        }
        // Unavailable scene evidence is a planned slowdown, not a latched
        // tracking emergency. The driver can always cancel using brake/F9.
        float limit=checks.Select(c=>c.AccelerationLimit??SceneStopPolicy.PlannedLimit(speed,c.StopDistance)).Min();
        bool permitted=faultBrake.OperationCurrent&&_IsRunning&&active&&!preview&&State.EnableAssists&&!faultBrake.Latched&&!brakeHeld&&!paused&&
            SensorsFresh(clock.Elapsed.TotalSeconds)&&clock.Elapsed.TotalSeconds-pathAt<.2&&
            State.DrivingMode!=ETS2LA.State.DrivingMode.LaneAssistOnly;
        // Hold progress is measured against the truck's fixed physical pose,
        // never a recomputed target/body distance or a changing motion reserve.
        if(check!=null){scenePedalFloor=sceneHold.Step(clock.Elapsed.TotalSeconds,speed,position,check.StopDistance,permitted,check.HoldKey);limit=sceneHold.Limit(limit);}
        else {sceneHold.Reset();scenePedalFloor=0;}
        return float.IsFinite(original)?Math.Min(original,limit):limit;
    }
    void ClearSceneHold()
    {
        sceneStopping=false;sceneHold.Reset();scenePedalFloor=0;
    }
}
