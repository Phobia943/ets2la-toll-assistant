using System.Numerics;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly IStaticRoadChecks? staticRoadWorker;
    string staticRoadStatus="Statische Straßenobjekte noch nicht geprüft.";
    public string StaticRoadStatus=>staticRoadStatus;

    RigRoadObservation? ReadRoadRigObservation()
    {
        if(!SensorsFresh(clock.Elapsed.TotalSeconds)||speed is <0 or >20d/3.6)return null;
        var observed=readRigObservation?.Invoke();
        // A delivered callback may be waiting for the main lock. Its latest
        // speed/pause/source veto must not wait for the next DrainInputFrames.
        var delivered=Volatile.Read(ref latestDeliveredTelemetry);
        if(delivered!=null&&(!delivered.Active||delivered.Paused||!double.IsFinite(delivered.Speed)||delivered.Speed is <0 or >20d/3.6||
            clock.Elapsed.TotalSeconds-delivered.Arrived is <0 or >.2||
            !StaticRoadWorkerProtocol.Fresh(delivered.Origin,System.Diagnostics.Stopwatch.GetTimestamp())))return null;
        var context=delivered!=null?delivered.Envelope:readRigObservation!=null?observed?.Context:envelopeContext;
        var shape=readRigObservation!=null?observed?.MeasuredShape??observed?.Envelope?.Shape:vehicleGeometry.Read();
        if(context==null||shape==null||context.Special||context.Trailers.Length!=1||context.Trailers[0].Chain!="single"||
            context.TruckHook is not { } truckHook||context.Trailers[0].Hook is not { } trailerHook||
            context.Trailers[0].RotationTurns is not { } turns)return null;
        var result=new RigRoadObservation(shape,context.Identity,context.UtcMs,truckHook,trailerHook,
            WheelAxle.Trailer(trailerHook,context.Trailers[0].Wheels),delivered?.Position??position,context.Trailers[0].Position,
            delivered?.Heading??headingTurns,turns.X,delivered?.Speed??speed);
        return result.Valid(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds())?result:null;
    }

    ParkedSceneAdmission.Result[] EvaluateStaticRoadChecks()
    {
        mapGateResult=null;
        if(staticRoadWorker==null)return [];
        var trace=CurrentMappedCorridor(clock.Elapsed.TotalSeconds);
        if(trace==null){staticRoadStatus="Keine aktuelle Route für statische Straßenobjekte.";return [];}
        long epoch=faultBrake.ControlEpoch;
        double horizon=Math.Clamp(TrackingGuard.RequiredLookahead(speed)+20,35,300);
        var observedRig=ReadRoadRigObservation();
        var result=staticRoadWorker.Read(trace,position,horizon,epoch,observedRig);
        if(result==null){staticRoadStatus=staticRoadWorker.Status+" Kein positiver Hindernisnachweis.";return [];}
        // Hit progress belongs to the original worker route. A freshly
        // published equivalent route may have pruned its tail and rebased Along.
        var current=result.Route.ProjectForTracking(position);
        if(!double.IsFinite(current.Along)||current.Offset>3||Math.Abs(current.HeightOffset)>2)return [];
        var checks=result.Hits.Where(hit=>hit.Along>=current.Along).Select(hit=>
            new ParkedSceneAdmission.Result(false,hit.Rig?"Gemessener Auflieger-/Lkwkörper erreicht ein statisches Kollisionsmodell":"Statisches Kollisionsmodell in der geplanten Standardspur",
                Math.Max(0,hit.Along-current.Along-ApproachStop.BufferMetres)) {
                Kind=SceneCheckKind.ConservativeOccupancy,
                HoldKey=$"static-road:{result.Generation}:{result.MapName}:{hit.Uid}"
                // No AcuteDistance: nominal overlap cannot latch an emergency.
            }).ToArray();
        // Archive reads, collision meshes and BVHs belong solely to the helper.
        // Revalidate the ORIGINAL response lease, selected route and F9 epoch
        // after provider callbacks, which may revoke any one of those identities.
        if(!staticRoadWorker.IsCurrent(result,trace,epoch)||
            result.Hits.Any(h=>h.Rig)&&!staticRoadWorker.IsRigCurrent(result,ReadRoadRigObservation())||!faultBrake.OperationCurrent||
            faultBrake.ControlEpoch!=epoch||!ReferenceEquals(trace,CurrentMappedCorridor(clock.Elapsed.TotalSeconds))) {
            staticRoadStatus="Statische Prüfung durch Quellen-/Routenwechsel verworfen.";return [];
        }
        staticRoadStatus=$"{checks.Length} statische Hindernisse im Standardkorridor oder gemessenen langsamen Gespannweg; "+
            (result.Incomplete||result.Issue!=null?"begrenzte Teilprüfung, ":"")+"keine vollständige Szenen-/Gespannfreigabe.";
        mapGateResult=result;
        return checks;
    }
}
