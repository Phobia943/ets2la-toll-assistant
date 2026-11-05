using System.Numerics;
using ETS2LA.Game.Data;
using PathLib;
using TruckLib.Models.Ppd;

namespace MautAssistent;

/// <summary>Join a confirmed toll path to an existing continuation of the same route.</summary>
public static class ExitHandoff
{
    internal static int RepairRoadConnections(PlannedPathData data, Vector3 truck)
    {
        int repairs = 0;
        for (int index=0; index+1<data.PlannedPath.Count; index++)
        {
            var before = data.PlannedPath[index];
            var next = data.PlannedPath[index+1];
            if (before.prefabPath is not { } connector || before.endNode.Uid != next.startNode.Uid ||
                next.laneIndex == 0 || next.item is not (ParsedRoad or ParsedRoadList)) continue;
            var end = connector.Interpolate(.99999f,0)?.Position;
            if (end == null) continue;

            float factor = next.direction == Direction.Backward ? 1 : 0;
            var side = next.laneIndex > 0 ? Side.Right : Side.Left;
            int count = next.item is ParsedRoadList roads ? roads.GetLaneCount(side) : ((ParsedRoad)next.item).GetLaneCount(side);
            if (Math.Abs((long)next.laneIndex) > count) continue;
            int lane = Math.Abs(next.laneIndex)-1;
            var entry = next.item is ParsedRoadList list ? list.InterpolateLane(factor,side,lane).Position
                : ((ParsedRoad)next.item).InterpolateLane(factor,side,lane).Position;
            int originalLane = next.laneIndex;
            if (Vector3.Distance(end.Value,entry)>2 && Align(data,index,truck,out _) && next.laneIndex!=originalLane)
                repairs++;
        }
        return repairs;
    }

    public static bool Align(PlannedPathData data,int index,Vector3 truck,out string? error)
    {
        error=null;
        if(index+1>=data.PlannedPath.Count)return true; // Host has not extended the plan yet.
        var toll=data.PlannedPath[index];var road=data.PlannedPath[index+1];
        if(toll.prefabPath==null)return true;
        if(toll.endNode.Uid!=road.startNode.Uid) {error="Mautausfahrt und Anschlussstraße haben keinen gemeinsamen Routenknoten.";return false;}
        var exit=toll.prefabPath.Interpolate(.99999f,0)?.Position;
        var before=toll.prefabPath.Interpolate(.999f,0)?.Position;
        if (road.prefabPath is { } connector)
        {
            if (exit.HasValue && before.HasValue && AlignConnector(road,connector,exit.Value,exit.Value-before.Value,truck))
                return true;
            error="Kein eindeutiger Anschluss am Verbindungsstück hinter der Mautstelle.";
            return false;
        }
        if(road.item is not (ParsedRoad or ParsedRoadList))return true;
        if(!exit.HasValue||!before.HasValue||road.laneIndex==0||!float.IsFinite(road.GetLength())||road.GetLength()<=0) {error="Mautausfahrt oder Fahrtrichtung der Anschlussstraße ist unklar.";return false;}
        bool backward=road.direction==Direction.Backward;
        float entry=backward?1:0;
        var side=road.laneIndex<0?Side.Left:Side.Right;
        int count=road.item is ParsedRoadList list?list.GetLaneCount(side):((ParsedRoad)road.item).GetLaneCount(side);
        if(Math.Abs((long)road.laneIndex)>count) {error="Ungültige Anschluss-Spur hinter dem Mauttor.";return false;}
        Vector3 Point(float t,int lane)=>road.item is ParsedRoadList roads?roads.InterpolateLane(t,side,lane,0).Position:
            ((ParsedRoad)road.item).InterpolateLane(t,side,lane,0).Position;
        var heading=Vector3.Normalize(exit.Value-before.Value);
        if(!float.IsFinite(heading.LengthSquared())) {error="Richtung der Mautausfahrt ist unklar.";return false;}
        var candidates=new List<(int lane,float gap)>();
        for(int lane=0;lane<count;lane++) {
            var p=Point(entry,lane);
            float step=Math.Min(.01f,1/Math.Max(1,road.GetLength()));
            var next=Point(entry+(backward?-step:step),lane);
            var forward=Vector3.Normalize(next-p);
            float alignment=Vector3.Dot(heading,forward);
            if(!float.IsFinite(alignment)||alignment<.8f)continue;
            float gap=Vector3.Distance(exit.Value,p);
            if(float.IsFinite(gap))candidates.Add((Math.Sign(road.laneIndex)*(lane+1),gap));
        }
        var ranked=candidates.OrderBy(c=>c.gap).ToArray();
        if(ranked.Length==0 || ranked[0].gap>1.5f || (ranked.Length>1&&ranked[1].gap-ranked[0].gap<.75f)) {
            error="Keine eindeutig anschließende Fahrspur hinter dem Mauttor.";return false;
        }
        int chosen=ranked[0].lane;
        if(chosen==road.laneIndex)return true;
        float factor=road.GetFactorFor(truck);
        if(!float.IsFinite(factor)) {error="Lkw-Position an der Mautausfahrt ist unklar.";return false;}
        float travel=(backward?1-factor:factor)*road.GetLength();
        var oldPoint=Point(factor,Math.Abs(road.laneIndex)-1);
        if(travel>2 && Vector3.Distance(truck,oldPoint)<3) {
            error="Anschluss-Spurwechsel zu spät; bitte manuell übernehmen.";return false;
        }
        // A target lane is not an entry lane. Start on the physically connected
        // lane; retain a valid later target, with fresh lane-change state.
        int target=road.targetLaneIndex;
        if(target==chosen || Math.Sign(target)!=Math.Sign(chosen) || Math.Abs((long)target)>count)target=0;
        // Pathfinding publishes a new list but shares its PlannedItem objects.
        // Replacing only the event's list entry would leave the host route wrong.
        // Equal current/target lanes reset the host's lane-change state through
        // its public API, before restoring a valid future target.
        road.laneIndex=chosen;
        road.targetLaneIndex=chosen;
        road.GenerateLaneChange(entry,false);
        road.targetLaneIndex=target;
        return true;
    }

    static bool AlignConnector(PlannedItem item, PrefabPath current, Vector3 exit, Vector3 heading, Vector3 truck)
    {
        double Gap(PrefabPath path)
        {
            if (path.StartNode.Uid != item.startNode.Uid || path.EndNode.Uid != item.endNode.Uid)
                return double.PositiveInfinity;
            var entry=path.Interpolate(.00001f,0)?.Position;
            var ahead=path.Interpolate(.01f,0)?.Position;
            if (entry is not { } start || ahead is not { } next ||
                !(Vector3.Dot(Vector3.Normalize(next-start),Vector3.Normalize(heading)) >= .8f))
                return double.PositiveInfinity;
            return Vector3.Distance(start,exit);
        }

        bool TruckAllowed(PrefabPath path) => path.Curves.Count>0 && path.Curves.All(c=>
            (c.AllowedVehicles is AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles) &&
            c.TrafficRule.ToString()!="escort_only");
        if (TruckAllowed(current) && Gap(current) <= 1.5) return true;
        float factor = current.GetFactorForPoint(truck);
        var occupied = current.Interpolate(factor,0)?.Position;
        if (!float.IsFinite(factor) || (factor*current.Length > 2 && occupied is { } point && Vector3.Distance(truck,point) < 3) ||
            current.Curves.Any(c => c.SemaphoreId >= 0))
            return false;

        var candidates=(item.otherPrefabPaths ?? []).Where(path=>TruckAllowed(path) && path.Curves.All(c=>c.SemaphoreId<0))
            .Select(path=>(Path:PrefabRefinement.Refine(path),Gap:Gap(path))).Where(option=>option.Gap<=1.5)
            .DistinctBy(option=>option.Path).OrderBy(option=>option.Gap).Take(2).ToArray();
        if (candidates.Length==0 || (candidates.Length==2 && candidates[1].Gap-candidates[0].Gap<.75)) return false;
        item.prefabPath=candidates[0].Path;
        item.otherPrefabPaths=[item.prefabPath];
        item.allowedToLaneChange=false;
        return true;
    }
}
