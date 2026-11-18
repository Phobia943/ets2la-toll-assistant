using System.Numerics;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    readonly IStaticRoadChecks? staticRoadWorker;
    string staticRoadStatus="Statische Straßenobjekte noch nicht geprüft.";
    public string StaticRoadStatus=>staticRoadStatus;

    ParkedSceneAdmission.Result[] EvaluateStaticRoadChecks()
    {
        if(staticRoadWorker==null)return [];
        var trace=CurrentMappedCorridor(clock.Elapsed.TotalSeconds);
        if(trace==null){staticRoadStatus="Keine aktuelle Route für statische Straßenobjekte.";return [];}
        long epoch=faultBrake.ControlEpoch;
        double horizon=Math.Clamp(TrackingGuard.RequiredLookahead(speed)+20,35,300);
        var result=staticRoadWorker.Read(trace,position,horizon,epoch);
        if(result==null){staticRoadStatus=staticRoadWorker.Status+" Kein positiver Hindernisnachweis.";return [];}
        // Hit progress belongs to the original worker route. A freshly
        // published equivalent route may have pruned its tail and rebased Along.
        var current=result.Route.ProjectForTracking(position);
        if(!double.IsFinite(current.Along)||current.Offset>3||Math.Abs(current.HeightOffset)>2)return [];
        var checks=result.Hits.Where(hit=>hit.Along>=current.Along).Select(hit=>
            new ParkedSceneAdmission.Result(false,"Statisches Kollisionsmodell in der geplanten Standardspur",
                Math.Max(0,hit.Along-current.Along-ApproachStop.BufferMetres)) {
                Kind=SceneCheckKind.ConservativeOccupancy,
                HoldKey=$"static-road:{result.Generation}:{result.MapName}:{hit.Uid}"
                // No AcuteDistance: nominal overlap cannot latch an emergency.
            }).ToArray();
        // Archive reads, collision meshes and BVHs belong solely to the helper.
        // Revalidate the ORIGINAL response lease, selected route and F9 epoch
        // after provider callbacks, which may revoke any one of those identities.
        if(!staticRoadWorker.IsCurrent(result,trace,epoch)||!faultBrake.OperationCurrent||
            faultBrake.ControlEpoch!=epoch||!ReferenceEquals(trace,CurrentMappedCorridor(clock.Elapsed.TotalSeconds))) {
            staticRoadStatus="Statische Prüfung durch Quellen-/Routenwechsel verworfen.";return [];
        }
        staticRoadStatus=$"{checks.Length} statische Hindernisse in der Standardspur; "+
            (result.Incomplete||result.Issue!=null?"begrenzte Teilprüfung, ":"")+"keine vollständige Szenen-/Gespannfreigabe.";
        return checks;
    }
}
