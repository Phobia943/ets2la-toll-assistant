using System.Numerics;

namespace MautAssistent;

public sealed record BarrierBindingResult(bool LanePlanned,bool MapCorridorKnown,bool LiveBarrierBound,
    Gate? LiveGate,ETS2LA.Game.SDK.Semaphore? LiveSample,double? Distance,string? HoldKey,
    TollDecision Decision,bool Holding,bool Passed=false);

public static class BarrierBinding
{
    static bool Finite(Vector3 p)=>float.IsFinite(p.X)&&float.IsFinite(p.Y)&&float.IsFinite(p.Z);

    public static BarrierBindingResult Check(TollBarrierPlan? plan,RouteTrace? trace,Vector3 truck,double speed,
        ETS2LA.Game.SDK.Semaphore[] gates,bool gateFeedFresh,TollOptions options)
    {
        if(plan==null)return new(false,false,false,null,null,null,null,TollDecision.Idle("Keine geplante Mautschranke"),false);
        bool known=false;double? distance=null;ETS2LA.Game.SDK.Semaphore? live=null;Gate? actual=null;
        if(Finite(truck)&&Finite(plan.Anchor)&&Finite(plan.Forward)&&plan.Forward.LengthSquared()>.9f&&
            trace!=null&&plan.RouteIdentity!=null&&trace.TryItemRange(plan.RouteIdentity,out var from,out var to))
        {
            // Restrict the booth projection to its exact directed prefab item;
            // another loop occurrence, carriageway or deck cannot lend an anchor.
            var target=trace.ProjectNearForTracking(plan.Anchor,(from+to)/2,(to-from)/2+.001);
            var current=UniqueCurrent(trace,truck,target.Along);
            known=current!=null&&target.Offset*target.Offset+target.HeightOffset*target.HeightOffset<=16&&
                target.Along>=from-.01&&target.Along<=to+.01&&
                Vector3.Dot(trace.DirectionAt(target.Along),Vector3.Normalize(plan.Forward))>=.8f&&
                !(trace.EndsAtGap&&target.Along>=trace.Length-.01);
            if(known) {
                distance=target.Along-current!.Along;
                // A map item can remain in the planner after the complete
                // passage. A directed, well past anchor is not a future stop.
                if(distance < -Math.Max(30,options.ExitDistanceMetres))
                    return new(true,true,false,null,null,distance,plan.HoldKey,TollDecision.Idle("Geplante Mautschranke passiert"),false,true);
                if(gateFeedFresh) {
                    live=TollSignals.ReadLive(gates,plan.GateId,plan.Anchor);
                    if(live!=null&&live.type==ETS2LA.Game.SDK.SemaphoreType.GATE&&live.id==plan.GateId&&
                        Vector3.Distance(live.GetWorldCoordinates(),plan.Anchor)<=1) {
                        actual=new(RouteMatcher.Key(live),distance.Value,
                            live.state is >=0 and <=3?(BarrierState)live.state:BarrierState.Unknown);
                    }else live=null;
                }
            }
        }
        if(!known&&Finite(truck)&&Finite(plan.Anchor)) {
            // Only a lower bound is used while the exact directed item has not
            // reached the accepted trace. It may trigger an earlier normal
            // stop, never certify a binding or make an in-range gate disappear.
            distance=Math.Max(0,Vector3.Distance(truck,plan.Anchor)-4);
        }
        var limit=TollBarrierBinding.Evaluate(speed,distance,actual!=null,options);
        return new(true,known,actual!=null,actual,live,distance,plan.HoldKey,limit.Decision,limit.Holding);
    }

    internal static TraceProjection? UniqueCurrent(RouteTrace trace,Vector3 truck,double targetAlong)
    {
        TraceProjection? Probe(double start,double end) {
            if(end-start<.001)return null;
            var p=trace.ProjectNearForTracking(truck,(start+end)/2,(end-start)/2+.000001);
            return p.Offset<=3&&Math.Abs(p.HeightOffset)<=2?p:null;
        }
        var before=Probe(0,targetAlong);var after=Probe(targetAlong,trace.Length);
        // A later crossing/parallel occurrence can be closer than the real
        // approach. It must not turn the current along into a false 'passed'.
        if(before!=null&&after!=null&&Math.Abs(before.Along-after.Along)>6)return null;
        var current=before==null?after:after==null?before:
            before.Offset*before.Offset+before.HeightOffset*before.HeightOffset<=after.Offset*after.Offset+after.HeightOffset*after.HeightOffset?before:after;
        if(current==null)return null;
        // Also reject multiple near occurrences on the same side of the gate.
        if(Probe(0,current.Along-6)!=null||Probe(current.Along+6,trace.Length)!=null)return null;
        return current;
    }
}
