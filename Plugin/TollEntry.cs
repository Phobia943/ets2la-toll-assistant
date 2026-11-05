using System.Numerics;
using ETS2LA.Game.Data;
using PathLib;
using TruckLib.Models.Ppd;

namespace MautAssistent;

public static class TollEntry
{
    public sealed record Changes(List<Action> Apply, PlannedItem? SlowRoad = null);
    public const double ShortChangeSpeed = 20 / 3.6;

    public static Changes? Plan(PlannedPathData plan, int index, PrefabPath original, PrefabPath replacement, Vector3 truck, double speed=0)
    {
        var before = original.Interpolate(.00001f, 0)?.Position;
        var entry = replacement.Interpolate(.00001f, 0)?.Position;
        var forward = replacement.Interpolate(.002f, 0)?.Position;
        if (!before.HasValue || !entry.HasValue || !forward.HasValue) return null;
        if (Vector3.Distance(before.Value, entry.Value) <= 1) return new([]);
        return Connect(index - 1, replacement.StartNode.Uid, entry.Value, forward.Value - entry.Value, 4);

        Changes? Connect(int i, ulong node, Vector3 target, Vector3 heading, int depth)
        {
            if (i < 0 || depth == 0) return null;
            var item = plan.PlannedPath[i];
            if (item.item is ParsedRoad or ParsedRoadList)
                return FromRoad(i, item, node, target, heading, depth);
            if (item.endNode.Uid != node || item.prefabPath == null || !IsAhead(i)) return null;

            foreach (var candidate in new[] { item.prefabPath }.Concat(item.otherPrefabPaths ?? []))
            {
                if (candidate.StartNode.Uid != item.startNode.Uid || candidate.EndNode.Uid != node ||
                    candidate.Curves.Any(c => c.SemaphoreId >= 0 ||
                        c.AllowedVehicles is not (AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles) ||
                        c.TrafficRule.ToString() == "escort_only")) continue;
                var path = PrefabRefinement.Refine(candidate);
                var end = path.Interpolate(.99999f, 0)?.Position;
                var nearEnd = path.Interpolate(.998f, 0)?.Position;
                var start = path.Interpolate(.00001f, 0)?.Position;
                var nearStart = path.Interpolate(.002f, 0)?.Position;
                if (!end.HasValue || !nearEnd.HasValue || !start.HasValue || !nearStart.HasValue ||
                    !Joins(end.Value, end.Value-nearEnd.Value, target, heading)) continue;
                var changes = Connect(i-1, item.startNode.Uid, start.Value, nearStart.Value-start.Value, depth-1);
                if (changes == null) continue;
                changes.Apply.Add(() =>
                {
                    item.prefabPath=path;
                    item.otherPrefabPaths=[path];
                    item.allowedToLaneChange=false;
                });
                return changes;
            }
            return null;
        }

        Changes? FromRoad(int i, PlannedItem road, ulong node, Vector3 target, Vector3 heading, int depth)
        {
            if (road.laneIndex == 0) return null;
            bool backward = road.direction == Direction.Backward;
            var ends = road.item is ParsedRoadList roads ? (roads.StartNode, roads.EndNode)
                : (((ParsedRoad)road.item).StartNode, ((ParsedRoad)road.item).EndNode);
            if ((backward ? ends.Item1 : ends.Item2).Uid != node) return null;
            float end = backward ? 0 : 1;
            float start = 1-end;
            float step = backward ? -.002f : .002f;
            var side = road.laneIndex < 0 ? Side.Left : Side.Right;
            int count = road.item is ParsedRoadList list ? list.GetLaneCount(side) : ((ParsedRoad)road.item).GetLaneCount(side);
            Vector3 Point(float factor, int lane) => road.item is ParsedRoadList list
                ? list.InterpolateLane(factor, side, lane).Position
                : ((ParsedRoad)road.item).InterpolateLane(factor, side, lane).Position;

            var lanes = Enumerable.Range(0, count).Where(lane =>
            {
                var last = Point(end,lane);
                return Joins(last,last-Point(end-step,lane),target,heading);
            }).OrderBy(lane => Vector3.Distance(Point(end,lane),target)).ToArray();
            if (lanes.Length != 1) return null;
            int lane = lanes[0];
            int wanted = Math.Sign(road.laneIndex)*(lane+1);
            if ((road.targetLaneIndex == 0 ? road.laneIndex : road.targetLaneIndex) == wanted) return new([]);
            if (road.targetLaneIndex != 0 || Math.Abs(road.laneIndex) > count) return null;

            float factor = road.GetFactorFor(truck);
            if (!float.IsFinite(factor)) return null;
            bool onRoad = Vector3.Distance(Point(factor,Math.Abs(road.laneIndex)-1),truck) <= 3;
            if (onRoad && road.allowedToLaneChange && Math.Abs(wanted-road.laneIndex) == 1 &&
                Math.Abs(end-factor)*road.GetLength() >= 160)
                return new([() => road.targetLaneIndex=wanted]);

            double beforeRoad = DistanceBefore(i);
            double brakingDistance = Math.Max(0,(speed*speed-ShortChangeSpeed*ShortChangeSpeed)/2.4) + Math.Max(0,speed)*1.5;
            bool nearBeginning = Math.Abs(start-factor)*road.GetLength() < 3;
            if (road.allowedToLaneChange && Math.Abs(wanted-road.laneIndex)==1 &&
                Math.Abs(end-factor)*road.GetLength() >= 45 &&
                (onRoad && speed <= ShortChangeSpeed || !onRoad && nearBeginning && beforeRoad >= Math.Max(5,brakingDistance)))
                return new([() => road.targetLaneIndex=wanted],road);

            if (onRoad || !IsAhead(i)) return null;
            var entryPoint = Point(start,lane);
            ulong startNode = (backward ? ends.Item2 : ends.Item1).Uid;
            var changes = Connect(i-1,startNode,entryPoint,Point(start+step,lane)-entryPoint,depth-1);
            if (changes == null) return null;
            changes.Apply.Add(() => road.laneIndex=wanted);
            return changes;
        }

        bool IsAhead(int itemIndex) => DistanceBefore(itemIndex) >= 5;

        double DistanceBefore(int itemIndex)
        {
            if (itemIndex == 0) return -1;
            var prefix = new PlannedPathData { HasError=false, PlannedPath=plan.PlannedPath.Take(itemIndex).ToList() };
            try
            {
                var trace = RouteMatcher.Sample(prefix);
                var projection = trace.ProjectForTracking(truck);
                return projection.Offset <= 3 ? trace.Length - projection.Along : -1;
            }
            catch (InvalidOperationException) { return -1; }
        }
    }

    static bool Joins(Vector3 a, Vector3 aHeading, Vector3 b, Vector3 bHeading) =>
        Vector3.Distance(a,b) <= 1.5f &&
        Vector3.Dot(Vector3.Normalize(aHeading),Vector3.Normalize(bHeading)) >= .8f;
}
