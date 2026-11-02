namespace MautAssistent;

public sealed record GateProjection(Gate Gate, double LateralOffset, bool RouteMatched = false);
public static class GateSelection
{
    public static Gate? Select(IEnumerable<GateProjection> projected, string? tracked)
    {
        var valid = projected.Where(p => double.IsFinite(p.Gate.Distance) &&
            double.IsFinite(p.LateralOffset) && p.LateralOffset >= 0 && p.LateralOffset <= (p.RouteMatched ? 4 : 2) &&
            (tracked != null ? p.Gate.Key == tracked : p.Gate.Distance >= 0))
            .ToArray();
        var candidates = (valid.Any(p=>p.RouteMatched) ? valid.Where(p=>p.RouteMatched) : valid)
            .OrderBy(p=>p.Gate.Distance).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length > 1 && Math.Abs(candidates[1].Gate.Distance-candidates[0].Gate.Distance) < 6) return null;
        return candidates[0].Gate;
    }
}
