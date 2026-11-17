using System.Numerics;
using PathLib;
namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly SceneMotionPrediction standardMotions=new();
    RigRuntimeObservation ReadStandardObservation()
    {
        if(readRigObservation!=null)return readRigObservation();
        // Do not obtain either the process ID or the scene through cargo
        // evidence: an unsupported load must not hide a valid native source.
        var raw=vehicleGeometry.Read();
        return new(null,null,raw!=null&&HostTrafficFresh()?parkedScene.Read(raw.ProcessId):null){MeasuredShape=raw};
    }
    bool StandardShapeCurrent(VehicleGeometry shape)
    {
        long now=DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        double age=((double)now-shape.UtcMs)/1000;
        var forward=Vector3.Transform(-Vector3.UnitZ,Quaternion.Normalize(shape.Truck.Rotation));forward.Y=0;
        return shape.ProcessId!=0&&shape.Session!=0&&shape.Sequence!=0&&(shape.Sequence&1)==0&&
            age is >=-.025 and <=.2&&double.IsFinite(speed)&&double.IsFinite(headingTurns)&&
            Vector3.Distance(shape.Truck.Position,position)<=.5+Math.Abs(speed)*Math.Max(0,age)&&
            forward.LengthSquared()>.9&&Vector3.Dot(Vector3.Normalize(forward),TrackingGuard.TruckForward(headingTurns))>=.98;
    }
    ParkedSceneAdmission.Result[] EvaluateStandardChecks(PlannedItem[] prefabs)
    {
        // A previous detailed-monitor latch has no authority in standard mode.
        ResetRigEvaluation();
        rigStatus="Standard-Spurkorridor; kein Nachweis der vollständigen Ladungs- oder Gespannhülle";
        if(prefabs.Length==0)return [];
        if(safetyTrace==null)return [new(false,"Bestätigter Straßenverlauf fehlt",0)];
        var observation=ReadStandardObservation();
        var raw=observation.MeasuredShape;
        if(raw!=null&&!StandardShapeCurrent(raw))raw=null;
        var checks=new List<ParkedSceneAdmission.Result>();
        foreach(var item in prefabs.Take(16)) {
            if(!RouteMatcher.TryItemIdentity(item,out var identity)) {checks.Add(new(false,"Gerichtete Mautidentität fehlt",0));continue;}
            var path=item.prefabPath!;
            checks.Add(StandardSceneAdmission.Check(path,observation.Scene,position,safetyTrace,speed,
                physicsObservation?.SimulatedTime??0,physicsObservation?.RenderTime??0,identity,
                modelEvidence!.ParkedRequirement(path.Prefab)!=false,raw,standardMotions));
        }
        if(prefabs.Length>16)checks.Add(new(false,"Zu viele Mautanlagen im begrenzten Prüfbereich",0));
        if(raw!=null&&checks.Any(c=>c.UsesLiveSource)) {
            var current=ReadStandardObservation();var next=current.MeasuredShape;
            bool sameSource=StandardShapeCurrent(raw)&&next!=null&&StandardShapeCurrent(next)&&next.ProcessId==raw.ProcessId&&next.Session==raw.Session;
            // These are separate producers: compare each epoch only with its
            // own later capture, never geometry.Session with scene.Session.
            if(observation.Scene is { } scene)
                sameSource&=(decimal)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-scene.UtcMs is >=-25 and <=200&&
                    current.Scene is { } renewed&&renewed.ProcessId==scene.ProcessId&&renewed.Session==scene.Session&&renewed.Generation==scene.Generation;
            if(!sameSource)return [new(false,"Örtliche Messquelle während der Prüfung geändert",0)];
        }
        if(!faultBrake.OperationCurrent)return [];
        return checks.ToArray();
    }
}
