using System.Reflection;
using PathLib;

namespace MautAssistent;

/// <summary>
/// PathLib interpolation may initialize/reset lane-change state even when its
/// completion flag is false. Measurements must never mutate the host's items.
/// Geometry is shared; mutable planning containers and lane state are copied.
/// This snapshot is for measurements only, never published as a steering route.
/// </summary>
public static class RouteSnapshot
{
    static readonly MethodInfo clone = typeof(object).GetMethod("MemberwiseClone", BindingFlags.Instance | BindingFlags.NonPublic)!;
    static readonly FieldInfo? laneChangeStart=typeof(PlannedItem).Module.ModuleVersionId==new Guid("8d0821da-1971-4caa-a8be-78ea1093e51d")?
        typeof(PlannedItem).GetField("laneChangeStartFactor",BindingFlags.Instance|BindingFlags.NonPublic):null;

    // Describe a detached snapshot, never the live native steering containers.
    internal static object Describe(PlannedPathData snapshot)=>snapshot.PlannedPath.Select(i=> {
        var original=i.prefabPath!=null?PrefabRefinement.OriginalFor(i.prefabPath):null;
        float? start=laneChangeStart?.GetValue(i) is float f&&float.IsFinite(f)?f:null;
        return new {
            kind=i.item.GetType().Name,start=i.startNode.Uid,end=i.endNode.Uid,
            lane=i.laneIndex,targetLane=i.targetLaneIndex,direction=i.direction.ToString(),
            i.allowedToLaneChange,laneChangeStartFactor=start,laneStateBuildVerified=laneChangeStart!=null,
            prefab=original?.Prefab.Prefab.Uid,
            curves=original?.Curves.Select(c=>original.Prefab.Descriptor?.NavCurves.IndexOf(c)??-1).ToArray(),
            roads=i.item is ETS2LA.Game.Data.ParsedRoadList list?list.Roads.Select(r=>r.Road.Uid).ToArray():
                i.item is ETS2LA.Game.Data.ParsedRoad road?new[]{road.Road.Uid}:Array.Empty<ulong>()
        };
    }).ToArray();

    public static PlannedPathData Capture(PlannedPathData source)
    {
        var result = (PlannedPathData)clone.Invoke(source, null)!;
        var originals = source.PlannedPath.ToArray();
        var copies = new Dictionary<PlannedItem, PlannedItem>();
        foreach (var item in originals)
            if (!copies.ContainsKey(item)) copies[item] = (PlannedItem)clone.Invoke(item, null)!;
        foreach (var item in originals)
        {
            var copy = copies[item];
            if (item.lastItem != null && copies.TryGetValue(item.lastItem, out var previous)) copy.lastItem = previous;
            copy.otherPrefabPaths = item.otherPrefabPaths?.ToArray()!;
        }
        result.PlannedPath = originals.Select(item => copies[item]).ToList();
        return result;
    }
}
