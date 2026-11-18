using System.Numerics;
using ETS2LA.Game.SDK;
using Semaphore=ETS2LA.Game.SDK.Semaphore;

namespace MautAssistent;

public sealed partial class TollPlugin
{
    StaticRoadWorkerClient.Result? mapGateResult;
    internal readonly record struct MapGateWatchKey(ulong Uid,string MapName,Vector3 Anchor,Quaternion Rotation);
    readonly HashSet<MapGateWatchKey> pendingMapGates=[];
    readonly Dictionary<MapGateWatchKey,(RouteTrace Route,double PlaneAlong)> observedMapGateApproaches=[];
    bool mapGateWatchOverflow;
    long pendingMapGateEpoch=-1;
    sealed record MapGateLease(StaticRoadWorkerClient.Result Result,RouteTrace Trace,long Epoch);
    bool MapGateLeaseCurrent(MapGateLease? lease)
    {
        if(lease==null)return true;
        if(staticRoadWorker==null||!ReferenceEquals(lease.Trace,CurrentMappedCorridor(clock.Elapsed.TotalSeconds))||
            faultBrake.ControlEpoch!=lease.Epoch||!staticRoadWorker.IsCurrent(lease.Result,lease.Trace,lease.Epoch))return false;
        // A test/provider callback can change the local route or manual epoch.
        // These final checks are local and never renew the original response.
        return ReferenceEquals(lease.Trace,CurrentMappedCorridor(clock.Elapsed.TotalSeconds))&&faultBrake.ControlEpoch==lease.Epoch&&
            StaticRoadWorkerProtocol.Fresh(lease.Result.OriginalTimestamp,System.Diagnostics.Stopwatch.GetTimestamp());
    }
    bool MapGateLeaseLocalCurrent(MapGateLease lease)=>staticRoadWorker?.IsLocallyCurrent(lease.Result)==true&&
        ReferenceEquals(lease.Trace,CurrentMappedCorridor(clock.Elapsed.TotalSeconds))&&faultBrake.ControlEpoch==lease.Epoch&&
        StaticRoadWorkerProtocol.Fresh(lease.Result.OriginalTimestamp,System.Diagnostics.Stopwatch.GetTimestamp());

    TrafficSpeedDecision ReadStandaloneMapGates(Semaphore[] signals,List<object> observations,out MapGateLease? lease)
    {
        lease=null;
        var trace=CurrentMappedCorridor(clock.Elapsed.TotalSeconds);
        var result=mapGateResult;
        if(pendingMapGateEpoch!=faultBrake.ControlEpoch) {
            pendingMapGates.Clear();observedMapGateApproaches.Clear();mapGateWatchOverflow=false;pendingMapGateEpoch=faultBrake.ControlEpoch;
        }
        if(observedMapGateApproaches.Count>0&&trace!=null&&result!=null&&SensorsFresh(clock.Elapsed.TotalSeconds)&&
            MapGateLeaseCurrent(new(result,trace,faultBrake.ControlEpoch)))foreach(var pair in observedMapGateApproaches.ToArray())
            if(pair.Key.MapName==result.MapName&&MapGatePassed(pair.Value.Route,pair.Value.PlaneAlong,trace,position))
                observedMapGateApproaches.Remove(pair.Key);
        // Remember a previously observed approach, including an open gate,
        // only as a requirement for fresh observation. No old distance or
        // opening state acquires a new timestamp or positive authority.
        pendingMapGates.Clear();pendingMapGates.UnionWith(observedMapGateApproaches.Keys);
        TrafficSpeedDecision Pending(string reason) {
            if(pendingMapGates.Count==0&&!mapGateWatchOverflow)return new(float.NaN,true,reason);
            double? stop=null;string? identity=null;
            if(trace!=null&&SensorsFresh(clock.Elapsed.TotalSeconds))foreach(var key in pendingMapGates) {
                if(!observedMapGateApproaches.TryGetValue(key,out var watch))continue;
                var remaining=MapGateRemainingStop(watch.Route,watch.PlaneAlong,trace,position);
                if(remaining is double distance&&(stop==null||distance<stop)){stop=distance;identity=$"map-gate:{key.Uid}";}
            }
            // The static plane is retained solely as a negative constraint.
            // Derive distance from the current coherent pose/corridor, never
            // reuse a stale distance or treat the old opening as fresh.
            return new(-.35f,false,"Beobachtete Grenzschranke: aktuelle Bindung/Öffnung noch ausstehend – geplanter Halt, F9 zur Übernahme",stop,identity);
        }
        if(result==null||result.Gates.Count==0||trace==null)return Pending("Keine gebundene eigenständige Grenzschranke");
        var candidateLease=new MapGateLease(result,trace,faultBrake.ControlEpoch);
        if(!MapGateLeaseCurrent(candidateLease))return Pending("Eigenständige Schrankenquelle nicht aktuell; keine Öffnungsbestätigung");
        var current=result.Route.ProjectForTracking(position);
        if(current.Offset>3||Math.Abs(current.HeightOffset)>2)return Pending("Keine eigene Schranken-Zufahrt bestätigt");
        var decision=EvaluateStandaloneMapGates(result.Gates,current.Along,signals,observations);
        if(!MapGateLeaseCurrent(candidateLease))return Pending("Schrankenbindung während Prüfung verworfen");
        foreach(var gate in result.Gates.Where(g=>g.PlaneAlong>current.Along)) {
            var key=new MapGateWatchKey(gate.Uid,result.MapName,gate.Anchor,gate.Rotation);
            if(observedMapGateApproaches.Count<64||observedMapGateApproaches.ContainsKey(key))
                observedMapGateApproaches[key]=(result.Route,gate.PlaneAlong);
            else mapGateWatchOverflow=true;
            // This key has a current directed binding. Its current state
            // (open or a finite planned stop) is already represented above.
            // Only missing bindings need the separate no-gas wait; the watch
            // remains to restore that wait if a later observation is lost.
            pendingMapGates.Remove(key);
        }
        lease=candidateLease;
        var pending=Pending("Keine ausstehende Grenzschranke");
        bool nearer=pending.StopDistance is { } pendingDistance&&(decision.StopDistance==null||pendingDistance<decision.StopDistance);
        return new(Minimum(decision.Acceleration,pending.Acceleration),decision.Clear&&pending.Clear,
            nearer||!pending.Clear&&decision.Clear?pending.Reason:decision.Reason,
            nearer?pending.StopDistance:decision.StopDistance,nearer?pending.StopIdentity:decision.StopIdentity);
    }
    internal static bool MapGatePassed(RouteTrace original,double planeAlong,RouteTrace current,Vector3 position)
    {
        if(!double.IsFinite(planeAlong)||!original.IdentityKnown||!current.IdentityKnown)return false;
        var at=original.ProjectForTracking(position);var currentAt=current.ProjectForTracking(position);
        return at.Offset<=1&&currentAt.Offset<=1&&Math.Abs(at.HeightOffset)<=1&&Math.Abs(currentAt.HeightOffset)<=1&&at.Along>planeAlong+1&&
            Vector3.Dot(original.DirectionAt(at.Along),current.DirectionAt(currentAt.Along))>.99&&
            original.MatchesAhead(current,position,10,out _,out _);
    }
    internal static double? MapGateRemainingStop(RouteTrace original,double planeAlong,RouteTrace current,Vector3 position)
    {
        if(!double.IsFinite(planeAlong)||!original.IdentityKnown||!current.IdentityKnown)return null;
        var at=original.ProjectForTracking(position);var currentAt=current.ProjectForTracking(position);
        double ahead=planeAlong-at.Along;
        if(at.Offset>1||currentAt.Offset>1||Math.Abs(at.HeightOffset)>1||Math.Abs(currentAt.HeightOffset)>1||ahead is <-1 or >300||
            Vector3.Dot(original.DirectionAt(at.Along),current.DirectionAt(currentAt.Along))<=.99||
            !original.MatchesAhead(current,position,Math.Max(10,ahead+1),out _,out _))return null;
        return Math.Max(0,ahead-MapGateApproach.FrontReserve);
    }
    internal static TrafficSpeedDecision EvaluateStandaloneMapGates(IReadOnlyList<MapGateCandidate> candidates,double along,
        Semaphore[] signals,List<object>? observations=null)
    {
        double? stop=null;string? identity=null;
        foreach(var gate in candidates) {
            if(gate.PlaneAlong<=along)continue;
            // Native standalone actors carry the default ID 0; the ID alone
            // is never a location or lane identity. Route direction is bound
            // by the map activation area; actor rotation is not assumed equal
            // to the map node or used to discard a contradictory actor state.
            var states=MapGateStates(gate,signals);
            bool closed=states.Any(s=>s is >=0 and <=2),open=states.Length>0&&states.All(s=>s==3);
            if(observations is {Count:<64})observations.Add(new {kind="standalone-map-gate",gate.Uid,
                anchor=new{x=gate.Anchor.X,y=gate.Anchor.Y,z=gate.Anchor.Z},gate.PlaneAlong,gate.StopAlong,
                distance=Math.Max(0,gate.StopAlong-along),closed,open,states});
            // The explicit map gate is already bound to this approach. Its
            // absence from the bounded SDK feed is not an opening proof.
            if(open)continue;
            double distance=Math.Max(0,gate.StopAlong-along);
            if(stop==null||distance<stop){stop=distance;identity=$"map-gate:{gate.Uid}";}
        }
        return stop is { } distanceToStop?new(float.NaN,false,"Geschlossene oder noch nicht offen bestätigte Grenzschranke auf der eigenen Zufahrt – geplanter Halt",distanceToStop,identity):
            new(float.NaN,true,"Keine aktuell geschlossene gebundene Grenzschranke; keine allgemeine Durchfahrtsfreigabe");
    }
    static int[] MapGateStates(MapGateCandidate gate,Semaphore[] signals)=>signals.Where(s=>s.type==SemaphoreType.GATE&&s.id==0&&
        Vector3.Distance(s.GetWorldCoordinates(),gate.Anchor)<=.25f).Select(s=>s.state).ToArray();
}
