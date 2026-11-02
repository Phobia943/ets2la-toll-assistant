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
