using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using ETS2LA.Backend.Events;
using ETS2LA.Game.Data;
using ETS2LA.Game.Telemetry;
using ETS2LA.Shared;
using PathLib;
using TruckLib.Models.Ppd;
using TruckLib.ScsMap;

namespace MautAssistent;

/// <summary>Seeds an empty, compatible Pathfinding plan from a verified local connector.</summary>
public static class ConnectorStart
{
    public const string MissingRoad = "No suitable starting road found for path planning.";
    static readonly Guid PlannerBuild = new("c0f35d70-ef3f-4fa1-9361-fdedc23bbe2d");
    static readonly Guid EventBusBuild = new("e4372644-7355-4bb8-a350-61dcbecccbcb");
    static readonly Guid PathLibBuild = new("8d0821da-1971-4caa-a8be-78ea1093e51d");
    const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;

    static bool Compatible(IPlugin planner) => planner._IsRunning && planner.Info.Id == "tumppi066.pathfinding" &&
        planner.GetType().Module.ModuleVersionId == PlannerBuild && typeof(Events).Module.ModuleVersionId == EventBusBuild &&
        typeof(PlannedItem).Module.ModuleVersionId == PathLibBuild;

    internal static bool IsLoadingMap(IPlugin planner) => Compatible(planner) &&
        planner.GetType().GetField("nearbyNodes",Fields)?.GetValue(planner) is not IReadOnlyList<Node> { Count: > 0 };

    internal static ulong NavigationKey(IPlugin? planner)
    {
        var data = planner?.GetType().GetField("navigationData",Fields)?.GetValue(planner) as ETS2LA.Game.SDK.NavigationData;
        if (data?.entries is not { } entries) return 0;
        ulong key = 14695981039346656037;
        foreach (var entry in entries)
            key = unchecked((key ^ entry.nodeUid) * 1099511628211);
        return key;
    }

    internal static PlannedPathData? Probe(IPlugin planner, Vector3 position, double heading, Vector3[] history)
    {
        if (!Compatible(planner))
            throw new InvalidOperationException("Diese Pathfinding-Version unterstützt die Routenprobe nicht.");
        var type = planner.GetType();
        if (type.GetField("nearbyNodes", Fields)?.GetValue(planner) is not IReadOnlyList<Node> { Count: > 0 } nodes)
            throw new InvalidOperationException("Pathfinding hat die lokale Karte noch nicht geladen.");
        var utils = type.Assembly.GetType("Pathfinding.Utils.PathfindingUtils", throwOnError: true)!;
        var start = utils.GetMethod("CreateFirstPlannedItem")!;
        var next = utils.GetMethod("CreateNextPlannedItem")!;
        var navigation = type.GetField("navigationData", Fields)?.GetValue(planner);
        PlannedItem? Extend(List<PlannedItem> items) => next.Invoke(null, [items, navigation]) as PlannedItem;

        var first = start.Invoke(null, [nodes, position, new Vector3((float)heading, 0, 0)]) as PlannedItem;
        var items = first == null ? Find(nodes, position, heading, Extend, history,
            road => ConfirmRoadLane(road,nodes,start)) : [first];
        if (items == null) return null;
        while (items.Count < 8 && items.Sum(i => i.GetLength()) < 1500)
        {
            var following = Extend(items);
            if (following == null || items.Any(i => i.endNode.Uid == following.endNode.Uid)) break;
            items.Add(following);
        }
        return new PlannedPathData { HasError = false, PlannedPath = items };
    }

    internal static bool TrimPassedStart(PlannedPathData planned,Vector3 position)
    {
        int selected=-1;double best=double.PositiveInfinity;
        for(int index=0;index<planned.PlannedPath.Count;index++)
        {
            var item=planned.PlannedPath[index];
            var point=item.Interpolate(item.GetFactorFor(position),0,false);
            if(!point.HasValue || Vector2.Distance(new(point.Value.Position.X,point.Value.Position.Z),new(position.X,position.Z))>1.25)continue;
            double distance=Vector3.Distance(point.Value.Position,position);
            if(distance<best){best=distance;selected=index;}
        }
        if(selected<0)return false;
        if(selected>0)planned.PlannedPath.RemoveRange(0,selected);
        return true;
    }

    internal static bool TryInstallStart(IPlugin planner,PlannedPathData planned,PlannedPathData published,
        Vector3 position,double heading)
    {
        var type=planner.GetType();
        if(!Compatible(planner) || planned.HasError || planned.PlannedPath.Count==0 ||
            !published.HasError || published.ErrorMessage!="Assists are disabled." ||
            !new StackTrace().GetFrames().Any(f=>f.GetMethod() is {Name:"Tick"} m && m.DeclaringType==type) ||
            type.GetField("plannedPath",Fields)?.GetValue(planner) is not List<PlannedItem> {Count:0} target ||
            type.GetField("telemetryData",Fields)?.GetValue(planner) is not GameTelemetryData data ||
            !data.sdkActive || data.paused || !float.IsFinite(data.truckFloat.speed) || data.truckFloat.speed<0 ||
            Vector3.Distance(position,data.truckPlacement.coordinate.ToVector3())>.25 ||
            Vector3.Dot(TrackingGuard.TruckForward(heading),TrackingGuard.TruckForward(data.truckPlacement.rotation.X))<.995)
            return false;
        // Verified Tick: Clear -> synchronous Publish(disabled) -> return.
        // Install before enabling assists so its next Tick uses this exact plan.
        target.AddRange(planned.PlannedPath);
        published.PlannedPath=target;
        published.ErrorMessage=null;
        published.HasError=false;
        return true;
    }

    public static bool TrySeed(IPlugin planner, Vector3 position, double heading, Vector3[] history, Func<Func<bool>, bool> commit)
    {
        var type = planner.GetType();
        if (!Compatible(planner)) return false;

        // Publish is synchronous in the verified EventBus build. Only touch the
        // host list on its own Tick stack, at the empty-plan error return.
        if (!new StackTrace().GetFrames().Any(f => f.GetMethod() is { Name: "Tick" } m && m.DeclaringType == type))
            return false;
        if (type.GetField("plannedPath", Fields)?.GetValue(planner) is not List<PlannedItem> { Count: 0 } target ||
            type.GetField("nearbyNodes", Fields)?.GetValue(planner) is not IReadOnlyList<Node> nodes) return false;

        var telemetry = type.GetField("telemetryData", Fields);
        bool StillStanding() => telemetry?.GetValue(planner) is GameTelemetryData data &&
            data.sdkActive && !data.paused && Math.Abs(data.truckFloat.speed) <= .1 &&
            Vector3.Distance(position, data.truckPlacement.coordinate.ToVector3()) <= .25 &&
            Vector3.Dot(TrackingGuard.TruckForward(heading), TrackingGuard.TruckForward(data.truckPlacement.rotation.X)) > .995;
        if (!StillStanding()) return false;
        var utils = type.Assembly.GetType("Pathfinding.Utils.PathfindingUtils");
        var next = utils?.GetMethod("CreateNextPlannedItem");
        var start = utils?.GetMethod("CreateFirstPlannedItem");
        if (next == null || start == null) return false;
        var navigation = type.GetField("navigationData", Fields)?.GetValue(planner);
        var plan = Find(nodes, position, heading, items => next.Invoke(null, [items, navigation]) as PlannedItem, history,
            road => ConfirmRoadLane(road,nodes,start));
        if (plan == null) return false;
        return commit(() =>
        {
            if (!planner._IsRunning || !StillStanding() || target.Count != 0) return false;
            target.AddRange(plan);
            return true;
        });
    }

    sealed record Candidate(List<PlannedItem> Plan, float Offset, Vector3[] Ahead, double HistoryFit);

    static bool IsTruckConnector(PrefabPath path) => path.Curves.Count != 0 && path.Curves.All(c => c.SemaphoreId < 0 &&
        (c.AllowedVehicles is AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles) &&
        c.TrafficRule.ToString() != "escort_only");

    internal static bool ConfirmRoadLane(PlannedItem item, IReadOnlyList<Node> nodes, MethodInfo nativeStart)
    {
        if (item.item is not ParsedRoadList { Roads.Count: > 0 } list || item.laneIndex == 0) return false;
        bool forward = item.direction == Direction.Forward;
        var road = forward ? list.Roads[0] : list.Roads[^1];
        var side = item.laneIndex > 0 ? Side.Right : Side.Left;
        var opposite = side == Side.Right ? Side.Left : Side.Right;
        int lane = Math.Abs(item.laneIndex)-1;
        if (lane >= road.GetLaneCount(side) || road.GetLaneCount(opposite) == 0 || road.Road.Length < 15) return false;

        // Spline direction and the signed lane index are independent on some
        // map items. Require two interior samples that agree with the map's
        // traffic side and also restart on the same native lane and direction.
        foreach (float fraction in new[] { .15f,.3f })
        {
            float t = forward ? fraction : 1-fraction;
            var point = road.InterpolateLane(t,side,lane).Position;
            var next = road.InterpolateLane(t+(forward?.01f:-.01f),side,lane).Position;
            var tangent = next-point;
            tangent.Y=0;
            var heading = Vector3.Normalize(tangent);
            var travelSide = Vector3.Cross(heading,Vector3.UnitY) * (road.Road.LeftHandTraffic ? -1 : 1);
            var center = road.Interpolate(t).Position;
            if (!(Vector3.Dot(point-center,travelSide)>1) || Enumerable.Range(0,road.GetLaneCount(opposite)).Any(i =>
                    !(Vector3.Dot(road.InterpolateLane(t,opposite,i).Position-center,travelSide)<-.5f)))
                return false;
            var turns = MathF.Atan2(-heading.X,-heading.Z)/MathF.Tau;
            var check = nativeStart.Invoke(null,[nodes,point,new Vector3(turns,0,0)]) as PlannedItem;
            if (check?.item is not ParsedRoadList confirmed || check.direction != item.direction ||
                check.laneIndex != item.laneIndex || !confirmed.Roads.Any(r=>r.Road.Uid==road.Road.Uid))
                return false;
        }
        return true;
    }

    internal static List<PlannedItem>? BuildPlan(PlannedItem seed, Vector3 position, Func<List<PlannedItem>, PlannedItem?> next,
        Func<PlannedItem,bool>? confirmRoad = null)
    {
        var items = new List<PlannedItem> { seed };
        var visited = new HashSet<ulong> { seed.startNode.Uid, seed.endNode.Uid };
        while (items.Count <= 4)
        {
            var previous = items[^1];
            var following = next(items);
            if (following == null || following.startNode.Uid != previous.endNode.Uid || !visited.Add(following.endNode.Uid))
                return null;
            items.Add(following);
            var data = new PlannedPathData { HasError = false, PlannedPath = items };
            if (following.item is ParsedRoad or ParsedRoadList)
            {
                int direction = following.direction == Direction.Forward ? 1 : -1;
                if (Math.Sign(following.laneIndex) != direction)
                {
                    if (confirmRoad != null && ExitHandoff.Align(data,items.Count-2,position,out _) && confirmRoad(following))
                        return items;
                    if (items.Count == 2) return null;
                    // Treat the native lane guess as unverified. Align must
                    // establish a unique physical match on the travel side.
                    following.laneIndex = direction;
                    following.targetLaneIndex = 0;
                }
                return ExitHandoff.Align(data, items.Count - 2, position, out _) ? items : null;
            }
            if (!ExitHandoff.Align(data, items.Count - 2, position, out _) ||
                following.prefabPath is not { } path || !IsTruckConnector(path) ||
                !(path.Length >= 1 && path.Length <= 350))
                return null;
        }
        return null;
    }

    public static List<PlannedItem>? Find(IReadOnlyList<Node> nodes, Vector3 position, double heading,
        Func<List<PlannedItem>, PlannedItem?> next, IReadOnlyList<Vector3>? history = null,
        Func<PlannedItem,bool>? confirmRoad = null)
    {
        if (!double.IsFinite(heading) || !float.IsFinite(position.LengthSquared())) return null;
        var forward = TrackingGuard.TruckForward(heading);
        var nearby = nodes.Where(n => Vector3.Distance(n.Position, position) <= 180)
            .SelectMany(n => new[] { n.ForwardItem, n.BackwardItem }).OfType<Prefab>()
            .DistinctBy(p => p.Uid).Take(33).ToArray();
        if (nearby.Length > 32) return null;

        var candidates = new List<Candidate>();
        foreach (var prefab in nearby)
        {
            var parsed = new ParsedPrefab(prefab);
            if (parsed.Descriptor == null) continue;
            var ends = prefab.Nodes.OfType<Node>().ToArray();
            foreach (var entry in ends)
            foreach (var exit in ends)
            {
                if (entry.Uid == exit.Uid) continue;
                var exitItem = exit.ForwardItem?.Uid == prefab.Uid ? exit.BackwardItem : exit.ForwardItem;
                if (exitItem is not (Road or Prefab)) continue;
                var (primary, alternatives) = parsed.GetPathsFromNodeToNode(entry, exit, entry.Position);
                foreach (var original in primary.Concat(alternatives))
                {
                    if (!IsTruckConnector(original)) continue;
                    if (original.Length < 2 || original.Length > 350) continue;
                    var path = PrefabRefinement.Refine(original);
                    float factor = Math.Clamp(path.GetFactorForPoint(position),.00001f,.99999f);
                    var point = path.Interpolate(factor, 0)?.Position;
                    if (!point.HasValue) continue;
                    float offset = Vector3.Distance(point.Value, position);
                    var ahead = path.Interpolate(Math.Min(.99999f,factor+.5f/path.Length), 0)?.Position;
                    var behind = path.Interpolate(Math.Max(.00001f,factor-.5f/path.Length), 0)?.Position;
                    if (!ahead.HasValue || !behind.HasValue || offset > 1.25f ||
                        !(Vector3.Dot(Vector3.Normalize(ahead.Value - behind.Value), forward) >= .9f)) continue;

                    var seed = new PlannedItem {
                        item=parsed, startNode=entry, endNode=exit, laneIndex=0,
                        direction=exit.BackwardItem?.Uid == prefab.Uid ? Direction.Forward : Direction.Backward,
                        prefabPath=path, otherPrefabPaths=[path], allowedToLaneChange=false
                    };
                    var nativePoint = seed.Interpolate(seed.GetFactorFor(position), 0, false)?.Position;
                    if (!nativePoint.HasValue || Vector3.Distance(nativePoint.Value, position) > 1.25f) continue;
                    var plan = BuildPlan(seed, position, next, confirmRoad);
                    if (plan == null) continue;
                    RouteTrace trace;
                    try { trace = RouteMatcher.Sample(new() { PlannedPath=plan, HasError=false }); }
                    catch (InvalidOperationException) { continue; }
                    var local = trace.ProjectForTracking(position);
                    if (local.Offset > 1.25 || trace.Length - local.Along < 25 ||
                        new TrackingGuard().Check(trace, position, heading, 0) != null) continue;
                    var samples = Enumerable.Range(0, 9)
                        .Select(i => path.Interpolate(Math.Min(.99999f, factor + (1-factor)*i/8), 0)?.Position)
                        .ToArray();
                    if (samples.All(p => p.HasValue))
                    {
                        double fit = history is { Count: >= 7 }
                            ? ConnectorHistory.Fit(RouteMatcher.Sample(new() { HasError=false, PlannedPath=[seed] }), history)
                            : double.PositiveInfinity;
                        candidates.Add(new(plan, offset, samples.Select(p => p!.Value).ToArray(), fit));
                    }
                }
            }
        }

        if (candidates.Count == 0) return null;
        var best = candidates.MinBy(c => c.Offset)!;
        if (candidates.Any(c => c.Plan[0].endNode.Uid != best.Plan[0].endNode.Uid)) return null;
        bool DifferentPath(Candidate a, Candidate b) => a.Plan[^1].laneIndex != b.Plan[^1].laneIndex ||
            !a.Plan.Select(i => i.endNode.Uid).SequenceEqual(b.Plan.Select(i => i.endNode.Uid)) ||
            a.Ahead.Zip(b.Ahead).Any(pair => Vector3.Distance(pair.First, pair.Second) > .75f);
        if (candidates.Any(c => c.Offset - best.Offset < .6f && DifferentPath(c, best)))
        {
            var traced = candidates.MinBy(c => c.HistoryFit)!;
            if (traced.HistoryFit > .75 || candidates.Any(c => DifferentPath(c, traced) &&
                (!double.IsFinite(c.HistoryFit) || c.HistoryFit - traced.HistoryFit < .6))) return null;
            best = traced;
        }
        return best.Plan;
    }
}
