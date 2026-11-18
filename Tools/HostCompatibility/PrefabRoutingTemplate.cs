using System.Numerics;
using TruckLib.Models.Ppd;

namespace MautHostCompatibility;

// Transplanted only into the pinned ParsedPrefab type. CurveDirection controls
// native interpolation bookkeeping; even its Backward paths are stored as a
// sequence of NavCurve.StartPosition -> EndPosition. PreviousLines are therefore
// not additional outgoing driving edges.
internal sealed class PrefabRoutingTemplate
{
    public PrefabDescriptor? Descriptor = null;

    public List<NavCurve> TraverseCurveUntilTarget(NavCurve start, NavCurve target)
    {
        var curves = Descriptor?.NavCurves;
        if (curves == null) return [];
        int first = curves.IndexOf(start), last = curves.IndexOf(target);
        if (first < 0 || last < 0) return [];
        var previous = new Dictionary<int, int> { [first] = -1 };
        var pending = new Queue<int>();
        pending.Enqueue(first);
        while (pending.TryDequeue(out int index))
        {
            var curve = curves[index];
            if (!float.IsFinite(curve.Length) || curve.Length <= 0 ||
                !float.IsFinite(curve.StartPosition.LengthSquared()) ||
                !float.IsFinite(curve.EndPosition.LengthSquared())) continue;
            if (index == last)
            {
                var result = new List<NavCurve>();
                for (int at = last; at >= 0; at = previous[at]) result.Add(curves[at]);
                result.Reverse();
                return result;
            }
            foreach (int next in curve.NextLines)
            {
                if (next < 0 || next >= curves.Count || previous.ContainsKey(next)) continue;
                float gap = Vector3.Distance(curve.EndPosition, curves[next].StartPosition);
                if (!float.IsFinite(gap) || gap > 1.5f) continue;
                previous.Add(next, index);
                pending.Enqueue(next);
            }
        }
        return [];
    }
}
