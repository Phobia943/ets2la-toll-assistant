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

    public static bool TrySeed(IPlugin planner, Vector3 position, double heading, Vector3[] history, Func<Func<bool>, bool> commit)
    {
        var type = planner.GetType();
        if (!planner._IsRunning || planner.Info.Id != "tumppi066.pathfinding" ||
            type.Module.ModuleVersionId != PlannerBuild || typeof(Events).Module.ModuleVersionId != EventBusBuild ||
            typeof(PlannedItem).Module.ModuleVersionId != PathLibBuild) return false;

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
        var next = type.Assembly.GetType("Pathfinding.Utils.PathfindingUtils")?.GetMethod("CreateNextPlannedItem");
        if (next == null) return false;
        var navigation = type.GetField("navigationData", Fields)?.GetValue(planner);
        var plan = Find(nodes, position, heading, seed => next.Invoke(null, [new List<PlannedItem> { seed }, navigation]) as PlannedItem, history);
        if (plan == null) return false;
        return commit(() =>
        {
            if (!planner._IsRunning || !StillStanding() || target.Count != 0) return false;
            target.AddRange(plan);
            return true;
        });
    }

    sealed record Candidate(PlannedItem First, PlannedItem Next, float Offset, Vector3[] Ahead, double HistoryFit);

    public static List<PlannedItem>? Find(IReadOnlyList<Node> nodes, Vector3 position, double heading,
        Func<PlannedItem, PlannedItem?> next, IReadOnlyList<Vector3>? history = null)
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
                if (entry.Uid == exit.Uid || Vector3.Dot(exit.Position - position, forward) < 3) continue;
                var exitRoad = exit.ForwardItem is Road ? exit.ForwardItem : exit.BackwardItem;
                if (exitRoad is not Road) continue;
                var (primary, alternatives) = parsed.GetPathsFromNodeToNode(entry, exit, entry.Position);
                foreach (var original in primary.Concat(alternatives))
                {
                    if (original.Curves.Any(c => c.SemaphoreId >= 0 ||
                        c.AllowedVehicles is not (AllowedVehicles.AllVehicles or AllowedVehicles.LargeVehicles))) continue;
                    if (original.Length < 8 || original.Length > 350) continue;
                    var path = PrefabRefinement.Refine(original);
                    float factor = path.GetFactorForPoint(position);
                    var point = path.Interpolate(factor, 0)?.Position;
                    if (!point.HasValue || factor <= 0 || factor >= 1 || (1-factor)*path.Length < 8) continue;
                    float offset = Vector3.Distance(point.Value, position);
                    var ahead = path.Interpolate(factor + 1/path.Length, 0)?.Position;
                    if (!ahead.HasValue || offset > 1.25f ||
                        Vector3.Dot(Vector3.Normalize(ahead.Value - point.Value), forward) < .9f) continue;

                    var seed = new PlannedItem {
                        item=parsed, startNode=entry, endNode=exit, laneIndex=0,
                        direction=exit.BackwardItem?.Uid == prefab.Uid ? Direction.Forward : Direction.Backward,
                        prefabPath=path, otherPrefabPaths=[path], allowedToLaneChange=false
                    };
                    var nativePoint = seed.Interpolate(seed.GetFactorFor(position), 0, false)?.Position;
                    if (!nativePoint.HasValue || Vector3.Distance(nativePoint.Value, position) > 1.25f) continue;
                    var following = next(seed);
                    if (following?.item is not (ParsedRoad or ParsedRoadList) || following.startNode.Uid != exit.Uid ||
                        following.direction != (following.laneIndex > 0 ? Direction.Forward : Direction.Backward)) continue;
                    var route = new PlannedPathData { PlannedPath=[seed, following], HasError=false };
                    if (!ExitHandoff.Align(route, 0, position, out _)) continue;
                    var trace = RouteMatcher.Sample(route);
                    var local = trace.Project(position);
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
                        candidates.Add(new(seed, following, offset, samples.Select(p => p!.Value).ToArray(), fit));
                    }
                }
            }
        }

        if (candidates.Count == 0) return null;
        var best = candidates.MinBy(c => c.Offset)!;
        if (candidates.Any(c => c.First.endNode.Uid != best.First.endNode.Uid)) return null;
        bool DifferentPath(Candidate a, Candidate b) => a.Next.laneIndex != b.Next.laneIndex ||
            a.Ahead.Zip(b.Ahead).Any(pair => Vector3.Distance(pair.First, pair.Second) > .75f);
        if (candidates.Any(c => c.Offset - best.Offset < .6f && DifferentPath(c, best)))
        {
            var traced = candidates.MinBy(c => c.HistoryFit)!;
            if (traced.HistoryFit > .75 || candidates.Any(c => DifferentPath(c, traced) &&
                (!double.IsFinite(c.HistoryFit) || c.HistoryFit - traced.HistoryFit < .6))) return null;
            best = traced;
        }
        return [best.First, best.Next];
    }
}
