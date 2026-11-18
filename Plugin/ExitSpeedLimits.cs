using System.Numerics;
using ETS2LA.Game.Data;
using PathLib;

namespace MautAssistent;

// A route constraint, never permission to drive. It survives a native item
// wrapper refresh and pruning of the preceding toll prefab. Only actual
// passage beyond the road end retires it; targetLane=0 alone is insufficient.
internal sealed class ExitSpeedLimits
{
    sealed record Proof(ulong Start,ulong End,Direction Direction,int From,int To,ParsedRoad[] Roads,
        Vector3 Entry,Vector3 Exit,Vector3 Heading)
    { public bool WasOnRoad {get;set;} }
    readonly List<Proof> proofs=[];
    // Historical topology only, not a speed admission or movement permission.
    // F9 clears active proofs but allows a later fresh recheck of this exact
    // previously repaired exit, without adopting unrelated native lane changes.
    readonly List<Proof> known=[];
    public bool Active=>proofs.Count>0;
    public void Reset()=>proofs.Clear();
    public void ResetAll(){proofs.Clear();known.Clear();}
    static ParsedRoad[] Roads(PlannedItem item)=>item.item is ParsedRoadList list?list.Roads.ToArray():
        item.item is ParsedRoad road?[road]:[];
    static bool Same(Proof proof,PlannedItem item)=>proof.Start==item.startNode.Uid&&proof.End==item.endNode.Uid&&
        proof.Direction==item.direction&&proof.Roads.SequenceEqual(Roads(item));
    public bool Owns(PlannedItem item)=>proofs.Any(p=>Same(p,item)&&item.allowedToLaneChange&&
        item.laneIndex==p.From&&item.targetLaneIndex==p.To);
    public bool Known(PlannedItem item)=>known.Any(p=>Same(p,item)&&item.laneIndex==p.From&&item.targetLaneIndex==p.To);
    public void Remember(PlannedItem item)
    {
        if(proofs.Any(p=>Same(p,item)))return;
        bool reverse=item.direction==Direction.Backward;float start=reverse?1:0,end=1-start;
        var side=item.laneIndex<0?Side.Left:Side.Right;
        Vector3 Point(float factor,int lane)=>item.item is ParsedRoadList list?
            list.InterpolateLane(factor,side,Math.Abs(lane)-1).Position:
            ((ParsedRoad)item.item).InterpolateLane(factor,side,Math.Abs(lane)-1).Position;
        var entry=Point(start,item.laneIndex);var exit=Point(end,item.targetLaneIndex);
        var heading=Vector3.Normalize(exit-Point(end+(reverse?.002f:-.002f),item.targetLaneIndex));
        if(!float.IsFinite(entry.LengthSquared())||!float.IsFinite(exit.LengthSquared())||!float.IsFinite(heading.LengthSquared()))
            throw new InvalidOperationException("Langsamer Mautausfahrt-Wechsel besitzt keine endliche Straßengeometrie.");
        if(proofs.Count>=4)throw new InvalidOperationException("Zu viele gleichzeitig offene Mautausfahrt-Wechsel.");
        var proof=new Proof(item.startNode.Uid,item.endNode.Uid,item.direction,item.laneIndex,item.targetLaneIndex,Roads(item),entry,exit,heading);
        proofs.Add(proof);
        if(!known.Any(p=>Same(p,item)&&p.From==proof.From&&p.To==proof.To)) {
            if(known.Count>=8)known.RemoveAt(0);
            known.Add(proof);
        }
    }
    public TollDecision Limit(PlannedPathData data,RouteTrace trace,Vector3 truck,double speed,TollDecision decision)
    {
        foreach(var proof in proofs.ToArray()) {
            var relative=truck-proof.Exit;
            double beyond=Vector3.Dot(relative,proof.Heading);
            var current=trace.ProjectForTracking(truck);
            var road=data.PlannedPath.FirstOrDefault(i=>Same(proof,i));
            bool passed=false;double ahead=0;
            if(road!=null&&trace.TryItemRange(RouteMatcher.ItemIdentity(road),out double start,out double finish)) {
                if(current.Offset<2&&current.Along>=start&&current.Along<=finish)proof.WasOnRoad=true;
                passed=current.Along>finish+3&&current.Along<finish+80;
                if(current.Offset<3)ahead=Math.Max(0,start-current.Along-Math.Max(0,speed)*1.5);
            } else if(proof.WasOnRoad&&data.PlannedPath.FirstOrDefault() is { } next&&next.startNode.Uid==proof.End&&
                next.prefabPath is { } connector&&connector.Interpolate(.00001f,0)?.Position is Vector3 entry&&
                Vector3.Distance(entry,proof.Exit)<1.5&&trace.TryItemRange(RouteMatcher.ItemIdentity(next),out double a,out double b))
                passed=current.Along>a+3&&current.Along<Math.Min(a+80,b);
            // An earlier part of a looping route near this end cannot retire
            // a future constraint. Use the exact directed item range first.
            if(passed&&beyond>3&&beyond<80&&Math.Abs(relative.Y)<2&&current.Offset<2)
                {proofs.Remove(proof);continue;}
            double cap=Math.Sqrt(TollEntry.ShortChangeSpeed*TollEntry.ShortChangeSpeed+2.4*ahead);
            float limit=(float)Math.Clamp((cap-speed)*.8,-2.5,.35);
            if(!float.IsFinite(decision.MaxAcceleration)||decision.MaxAcceleration>limit)
                decision=decision with{Phase="Tempo für langsamen Mautausfahrt-Wechsel",TargetSpeed=cap,MaxAcceleration=limit};
        }
        return decision;
    }
}
