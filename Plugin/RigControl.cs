using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using PathLib;
namespace MautAssistent;
internal sealed record RigRuntimeObservation(EnvelopeContext? Context,PredictionEnvelope? Envelope,SceneSnapshot? Scene);

public sealed partial class TollPlugin
{
    readonly SceneMotionPrediction rigMotions=new();
    readonly RigModelMonitor rigMonitor=new();
    readonly Func<RigRuntimeObservation>? readRigObservation;
    RigRuntimePrediction? previousRigPrediction;
    string? previousRigKey;
    string rigStatus="Gespannvorhersage noch nicht benötigt";
    public string RigPredictionStatus=>rigStatus;
    void ResetRigEvaluation()
    {
        rigMotions.Reset();rigMonitor.Reset();previousRigPrediction=null;previousRigKey=null;
    }
    static string RigKey(string value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    ParkedSceneAdmission.Result[] EvaluateRigChecks(PlannedItem[] prefabs)
    {
        if(prefabs.Length==0){ResetRigEvaluation();return [];}
        if(safetyTrace==null||envelopeEvidence==null&&readRigObservation==null)return [new(false,"Gespann- oder Routenquelle noch nicht bereit",0)];
        var trace=safetyTrace;RigRuntimeObservation observedSource;
        if(readRigObservation!=null)observedSource=readRigObservation();
        else {
            _=ReadEnvelopeProfile();var currentContext=envelopeContext;
            var currentEnvelope=envelopeEvidence!.ReadForPrediction(currentContext,lastEnvelopeShape,position,speed,headingTurns);
            observedSource=new(currentContext,currentEnvelope,currentEnvelope!=null&&HostTrafficFresh()?parkedScene.Read(currentEnvelope.Shape.ProcessId):null);
        }
        var context=observedSource.Context;var envelope=observedSource.Envelope;
        double nativeAlong=envelope==null?trace.ProjectForTracking(position).Along:trace.ProjectForTracking(envelope.Shape.Truck.Position).Along;
        var runtime=RigPredictionRuntime.Build(trace,nativeAlong,envelope,context,position,headingTurns,speed,
            DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),0,.08);
        var scene=observedSource.Scene;
        if(scene!=null)rigMotions.Capture(scene,DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        var checks=new List<ParkedSceneAdmission.Result>();
        string pathKey=RigKey(string.Join("|",prefabs.Select(p=>RouteMatcher.TryItemIdentity(p,out var id)?id:"unidentified")));
        string wheelKey=RigKey(context?.Identity??"missing");
        bool SourceCurrent() {
            var current=readRigObservation?.Invoke();
            return envelope!=null&&context!=null&&ReferenceEquals(envelope.AssetIdentity,current?.Envelope?.AssetIdentity??envelopeEvidence?.AssetIdentity)&&
                (current?.Context??envelopeContext)?.Identity==context.Identity&&DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-envelope.Shape.UtcMs is >=0 and <=150&&
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()-context.UtcMs is >=0 and <=150&&faultBrake.OperationCurrent;
        }
        if(runtime.Supported&&runtime.Shape!=null&&runtime.Input!=null&&runtime.Prediction!=null&&runtime.Axle!=null) {
            bool accepted;
            if(previousRigPrediction?.Trace is { } oldTrace&&previousRigKey!=null&&!rigMonitor.Faulted) {
                // Old model parameters belong to its retained, translated
                // root trace, not the newly pruned route or latest SCS pose.
                var point=runtime.Shape.Truck.Position-previousRigPrediction.AnchorShift;
                double oldAlong=oldTrace.ProjectForTracking(point).Along;
                var observed=rigMonitor.Check(runtime.Shape,previousRigKey,wheelKey,runtime.Axle,oldAlong,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                accepted=observed.WithinModel;
                if(accepted&&pathKey!=previousRigKey)rigMonitor.Reset();
                if(!accepted)rigStatus="Gespannbewegung weicht vom vorherigen Modell ab: "+observed.Reason;
            } else accepted=!rigMonitor.Faulted;
            if(rigMonitor.Faulted&&Math.Abs(speed)<=.1&&SourceCurrent())
                accepted=rigMonitor.RebaseAtStandstill(runtime.Input,runtime.Prediction,runtime.Shape,pathKey,wheelKey,runtime.Axle,speed,
                    DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            else if(accepted&&SourceCurrent())accepted=rigMonitor.Capture(runtime.Input,runtime.Prediction,runtime.Shape,pathKey,wheelKey,runtime.Axle,
                DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            else accepted=false;
            if(accepted){previousRigPrediction=runtime;previousRigKey=pathKey;rigStatus="Getrennte Gespannvorhersage und vorherige Bewegung bestätigt";}
            else runtime=runtime with{Issue="Gespannmodell wartet auf bestätigte Bewegung/Kontakt oder erneute Prüfung im Stand",IssueKind=RigRuntimeIssue.MissingEvidence};
        }
        double? Height(double along)=>trace.TryAtAlong(along,out var p)?p.Position.Y:null;
        RigGroundPlane? Ground(RigSweptCell cell,VehicleBody body)=>RigRoadSurface.ForBody(trace,cell,body);
        var acuteCorridor=scene==null||envelope==null?null:MeasuredTruckCorridor.Create(envelope.Shape.Truck,trace,position,speed);
        foreach(var item in prefabs.Take(16)) {
            var path=item.prefabPath!;
            if(!RouteMatcher.TryItemIdentity(item,out string identity)){checks.Add(new(false,"Gerichtete Mautidentität fehlt",0));continue;}
            bool dynamic=modelEvidence!.ParkedRequirement(path.Prefab)!=false;
            float h=envelope?.PlanningHeight??float.NaN,w=envelope?.PlanningWidth??float.NaN;
            float front=envelope==null?float.NaN:envelope.Shape.Truck.Corners().Max(c=>Vector3.Dot(c-envelope.Shape.Truck.Position,TrackingGuard.TruckForward(headingTurns)));
            var local=ParkedSceneAdmission.CheckRig(path,scene,position,trace,speed,
                physicsObservation?.SimulatedTime??0,physicsObservation?.RenderTime??0,runtime,rigMotions,identity,dynamic,h,w,front,envelope?.Shape,acuteCorridor);
            checks.Add(local); // Keep acute positive evidence even when Clear.
            if(!runtime.Supported||runtime.Prediction==null)continue;
            if(!trace.TryItemRange(identity,out double start,out double end))continue;
            var current=BarrierBinding.UniqueCurrent(trace,position,(start+end)/2);
            if(current==null||end<current.Along-60||start>current.Along+runtime.RequiredAhead+30)continue;
            var inspection=modelEvidence.ReadInspection(path.Prefab);
            double wait=Math.Max(0,start-current.Along-ApproachStop.BufferMetres);
            string owner="static-rig:"+path.Prefab.Prefab.Uid;
            if(inspection?.Issue!=null||inspection?.StaticGeometry==null){checks.Add(new(false,"Statische Mautgeometrie wird geprüft",wait){HoldKey=owner});continue;}
            if(!PrefabModelTransform.TryRead(path,out var transform)){checks.Add(new(false,"Mautmodell nicht an die Weltspur gebunden",wait){HoldKey=owner});continue;}
            var solid=inspection.StaticGeometry.Check(runtime.Prediction,transform,Height,bodyRoadPlane:Ground);
            var movers=RigStaticGeometry.CheckMovers(runtime.Prediction,inspection.BoundedMovers,transform);
            foreach(var hit in new[]{solid,movers})if(!hit.Clear)
                checks.Add(new(false,hit.Issue??"Geplantes Gespann passt hier noch nicht an der Mautgeometrie vorbei",
                    hit.ConflictAlong is double along?Math.Max(0,along-current.Along-3):wait){HoldKey=owner});
        }
        if(prefabs.Length>16)checks.Add(new(false,"Zu viele Mautanlagen im begrenzten Prüfbereich",0));
        if(envelope!=null&&!SourceCurrent())return [new(false,"Gespannquelle während der Prüfung geändert oder veraltet",0)];
        return checks.ToArray();
    }
}
